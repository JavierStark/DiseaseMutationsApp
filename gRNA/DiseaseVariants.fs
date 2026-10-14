/// Finds candidate rsIDs for a disease. A disease name, OMIM number or MONDO id is resolved through the Monarch Initiative
/// API (keyless), and the variants come from NCBI ClinVar through E-utilities (elink OMIM to ClinVar, an esearch filter on
/// the clinical classification, then batched esummary calls). This replaces the OMIM scraper that a CAPTCHA ended.
///
/// The module stands alone: it needs only `GrnaEnvironment` (API key and contact) and `ReportSchema` (CSV quoting). The
/// parsers are pure functions over JSON text so they can be tested against captured responses; the network sits in
/// `DiseaseVariantClient`, which takes its `HttpClient` so a test can stub the services.
module gRNA.DiseaseVariants

open System
open System.Globalization
open System.Net.Http
open System.Text
open System.Text.Json
open System.Text.RegularExpressions
open System.Threading
open System.Threading.Tasks

// ---- model ------------------------------------------------------------------------------------------------

/// Which ClinVar germline classifications are kept.
type MinSignificance =
    /// Pathogenic, Likely pathogenic and Pathogenic/Likely pathogenic (the default).
    | PathogenicOrLikely
    /// Pathogenic and Pathogenic/Likely pathogenic only.
    | PathogenicOnly
    /// Everything ClinVar links to the disease, including benign and uncertain records.
    | AnyClassification

/// A disease as Monarch describes it.
type DiseaseRef = { Id: string; Name: string }

type DiseaseMatch =
    { /// MONDO id such as MONDO:0009861, or OMIM:261600 when the user typed an OMIM number.
      Id: string
      Name: string
      /// Numeric OMIM numbers cross-referenced by Monarch ("261600").
      OmimNumbers: string list
      /// OMIM phenotypic series ("PS227650"): a group of diseases whose numeric OMIM numbers sit on the subtypes.
      PhenotypicSeries: string list
      /// Direct subtypes (only filled by an entity lookup, not by a search).
      Subtypes: DiseaseRef list }

/// The numeric OMIM numbers that will be queried, and how many subtypes were consulted to find them.
type ResolvedDisease =
    { Disease: DiseaseMatch
      OmimNumbers: string list
      SubtypesChecked: int
      SubtypesTotal: int }

type DiseaseVariant =
    { ClinVarId: string
      /// ClinVar's name for the variant, for example NM_000518.5(HBB):c.20A>T (p.Glu7Val).
      Name: string
      Gene: string option
      /// "rs334" (with the prefix), when ClinVar cross-references dbSNP.
      RsId: string option
      /// Genomic HGVS on GRCh38 derived from ClinVar's canonical SPDI, for simple variants.
      Hgvs: string option
      /// ClinVar's germline classification, or "" when the record has none.
      Significance: string
      ReviewStatus: string
      Traits: string list }

type DiseaseVariantResult =
    { OmimNumbers: string list
      /// How many ClinVar records matched the classification filter (may exceed `Variants` when `Truncated`).
      TotalMatching: int
      Variants: DiseaseVariant list
      Truncated: bool }

/// Progress of a variant search: a stage label and how many of the steps are done.
type VariantProgress = { Stage: string; Done: int; Total: int }

/// What the user typed.
type DiseaseQuery =
    | ByOmim of string
    | ByMondo of string
    | ByName of string

// ---- small helpers ------------------------------------------------------------------------------------------

let private isDigits (s: string) = s.Length > 0 && s |> Seq.forall (fun c -> c >= '0' && c <= '9')

let private omimPrefixPattern = Regex(@"^(?:OMIM\s*:?\s*|MIM\s*:?\s*|#\s*)(\d+)$", RegexOptions.IgnoreCase ||| RegexOptions.CultureInvariant)
let private mondoPattern = Regex(@"^MONDO\s*:?\s*(\d+)$", RegexOptions.IgnoreCase ||| RegexOptions.CultureInvariant)

/// Classifies the text the user typed. Numbers and MONDO ids are validated strictly (they go into URLs and request bodies);
/// a name is only bounded in length and checked for control characters (it is percent-encoded wherever it is sent).
let parseQuery (text: string) : DiseaseQuery =
    let t = if isNull text then "" else text.Trim()
    if t = "" then raise (GrnaInputException "Type a disease name, an OMIM number (for example 261600) or a MONDO id.")
    if t.Length > 120 then raise (GrnaInputException "That search text is too long (120 characters at most).")
    if t |> Seq.exists Char.IsControl then raise (GrnaInputException "The search text contains control characters.")
    let omimNumber (digits: string) =
        if digits.Length < 4 || digits.Length > 9 then
            raise (GrnaInputException(sprintf "'%s' is not an OMIM number (they have 6 digits, for example 261600)." digits))
        ByOmim digits
    let m = omimPrefixPattern.Match t
    if m.Success then omimNumber m.Groups.[1].Value
    else
        let mm = mondoPattern.Match t
        if mm.Success then
            let digits = mm.Groups.[1].Value
            if digits.Length > 7 then raise (GrnaInputException(sprintf "'%s' is not a MONDO id (MONDO: followed by 7 digits)." t))
            ByMondo("MONDO:" + digits.PadLeft(7, '0'))
        elif t.StartsWith("OMIM", StringComparison.OrdinalIgnoreCase) && t.Contains ':' then
            raise (GrnaInputException(sprintf "'%s' is not an OMIM number (OMIM: followed by digits, for example OMIM:261600)." t))
        elif isDigits t then omimNumber t
        else ByName t

// ---- JSON access (tolerant: a missing field is an empty value, never an exception) ---------------------------

let private tryGet (name: string) (el: JsonElement) : JsonElement option =
    if el.ValueKind = JsonValueKind.Object then
        match el.TryGetProperty name with
        | true, v when v.ValueKind <> JsonValueKind.Null -> Some v
        | _ -> None
    else None

let private str (el: JsonElement option) : string =
    match el with
    | Some v when v.ValueKind = JsonValueKind.String -> v.GetString()
    | Some v when v.ValueKind = JsonValueKind.Number -> v.GetRawText()
    | _ -> ""

let private elements (el: JsonElement option) : JsonElement list =
    match el with
    | Some v when v.ValueKind = JsonValueKind.Array -> [ for x in v.EnumerateArray() -> x ]
    | _ -> []

let private strings (el: JsonElement option) : string list =
    elements el |> List.choose (fun x -> if x.ValueKind = JsonValueKind.String then Some(x.GetString()) else None)

/// Parses `text` or raises GrnaUpstreamException naming the service (an HTML error page is not JSON).
let private withJson (what: string) (text: string) (f: JsonElement -> 'a) : 'a =
    let doc =
        try
            JsonDocument.Parse text
        with :? JsonException ->
            raise (GrnaUpstreamException(sprintf "%s did not return JSON (the service may be unavailable)." what))
    use doc = doc
    f doc.RootElement

// ---- Monarch ------------------------------------------------------------------------------------------------

let private afterPrefix (prefix: string) (x: string) =
    if x.StartsWith(prefix, StringComparison.Ordinal) && isDigits (x.Substring prefix.Length) then Some(x.Substring prefix.Length) else None

let private matchOfNode (el: JsonElement) : DiseaseMatch option =
    let id = str (tryGet "id" el)
    if id = "" then None
    else
        let xrefs = strings (tryGet "xref" el)
        Some
            { Id = id
              Name = str (tryGet "name" el)
              OmimNumbers = xrefs |> List.choose (afterPrefix "OMIM:") |> List.distinct
              PhenotypicSeries = xrefs |> List.choose (afterPrefix "OMIMPS:") |> List.map (fun n -> "PS" + n) |> List.distinct
              Subtypes = [] }

let private hasOmim (m: DiseaseMatch) = not m.OmimNumbers.IsEmpty || not m.PhenotypicSeries.IsEmpty

/// Monarch `/search` response to disease matches. Diseases with an OMIM link come first, then the rest (Monarch's own
/// order is kept inside each group); non-disease categories are dropped.
let parseMonarchSearch (json: string) : DiseaseMatch list =
    withJson "Monarch" json (fun root ->
        elements (tryGet "items" root)
        |> List.filter (fun it ->
            match tryGet "category" it with
            | Some c -> str (Some c) = "biolink:Disease"
            | None -> true)
        |> List.choose matchOfNode
        |> List.sortBy (fun m -> if hasOmim m then 0 else 1))

/// Monarch `/entity/{id}` response: the disease with its direct subtypes.
let parseMonarchEntity (json: string) : DiseaseMatch =
    withJson "Monarch" json (fun root ->
        match matchOfNode root with
        | None -> raise (GrnaUpstreamException "Monarch returned an entity without an id.")
        | Some m ->
            let subtypes =
                tryGet "node_hierarchy" root
                |> Option.bind (tryGet "sub_classes")
                |> elements
                |> List.choose (fun s ->
                    let id = str (tryGet "id" s)
                    if id.StartsWith("MONDO:", StringComparison.Ordinal) then Some { Id = id; Name = str (tryGet "name" s) } else None)
            { m with Subtypes = subtypes })

// ---- ClinVar (NCBI E-utilities) -------------------------------------------------------------------------------

/// elink with cmd=neighbor_history: the history handle holding the ClinVar records linked to the OMIM numbers, or None
/// when OMIM links to no ClinVar record.
let parseElinkHistory (json: string) : (string * string) option =
    withJson "NCBI elink" json (fun root ->
        match tryGet "error" root with
        | Some e -> raise (GrnaUpstreamException(sprintf "NCBI elink failed: %s" (str (Some e))))
        | None ->
            let linkset = elements (tryGet "linksets" root) |> List.tryHead
            match linkset with
            | None -> None
            | Some ls ->
                let webEnv = str (tryGet "webenv" ls)
                let key =
                    elements (tryGet "linksetdbhistories" ls)
                    |> List.tryPick (fun h -> if str (tryGet "dbto" h) = "clinvar" then Some(str (tryGet "querykey" h)) else None)
                match key with
                | Some k when k <> "" && webEnv <> "" -> Some(webEnv, k)
                | _ -> None)

/// esearch response: how many records matched and the ids returned (at most the retmax requested).
let parseEsearch (json: string) : int * string list =
    withJson "NCBI esearch" json (fun root ->
        match tryGet "esearchresult" root with
        | None ->
            let reason = match tryGet "error" root with Some e -> str (Some e) | None -> "unexpected response"
            raise (GrnaUpstreamException(sprintf "NCBI esearch failed: %s" reason))
        | Some r ->
            match tryGet "ERROR" r with
            | Some e -> raise (GrnaUpstreamException(sprintf "NCBI esearch failed: %s" (str (Some e))))
            | None ->
                let count = match Int32.TryParse(str (tryGet "count" r), NumberStyles.None, CultureInfo.InvariantCulture) with | true, n -> n | _ -> 0
                count, strings (tryGet "idlist" r))

let private accessionPattern = Regex(@"^NC_\d{6}\.\d{1,2}$", RegexOptions.CultureInvariant)

let private isBases (s: string) = s |> Seq.forall (fun c -> c = 'A' || c = 'C' || c = 'G' || c = 'T')

let private commonPrefixLength (a: string) (b: string) =
    let n = min a.Length b.Length
    let mutable i = 0
    while i < n && a.[i] = b.[i] do i <- i + 1
    i

let private commonSuffixLength (a: string) (b: string) =
    let n = min a.Length b.Length
    let mutable i = 0
    while i < n && a.[a.Length - 1 - i] = b.[b.Length - 1 - i] do i <- i + 1
    i

/// Converts a ClinVar canonical SPDI (`NC_000012.12:102877504:TT:T`: accession, 0-based position, deleted bases, inserted
/// bases) to a genomic HGVS that Diana accepts (`NC_000012.12:g.102877506del`). The common prefix and suffix of the two
/// alleles are removed first. Left-aligned, not 3'-shifted: the same sequence results either way. Anything that is not a
/// plain A/C/G/T change on a chromosome accession gives None.
let spdiToHgvs (spdi: string) : string option =
    if String.IsNullOrWhiteSpace spdi then None
    else
        match spdi.Trim().Split(':') with
        | [| accession; posText; deleted; inserted |]
            when accessionPattern.IsMatch accession && isBases deleted && isBases inserted ->
            match Int32.TryParse(posText, NumberStyles.None, CultureInfo.InvariantCulture) with
            | true, pos ->
                let prefix = commonPrefixLength deleted inserted
                let del1 = deleted.Substring prefix
                let ins1 = inserted.Substring prefix
                let suffix = commonSuffixLength del1 ins1
                let del2 = del1.Substring(0, del1.Length - suffix)
                let ins2 = ins1.Substring(0, ins1.Length - suffix)
                let first = int64 pos + int64 prefix + 1L // 1-based position of the first deleted base
                let at (p: int64) = sprintf "%s:g.%d" accession p
                if del2 = "" && ins2 = "" then None
                elif del2.Length = 1 && ins2.Length = 1 then Some(sprintf "%s%s>%s" (at first) del2 ins2)
                elif ins2 = "" then
                    let last = first + int64 del2.Length - 1L
                    Some(if del2.Length = 1 then sprintf "%sdel" (at first) else sprintf "%s_%ddel" (at first) last)
                elif del2 = "" then
                    // An insertion between 1-based positions (first - 1) and first. When the inserted bases repeat the bases
                    // just before the insertion point (known only from the stripped prefix) it is a duplication.
                    let left = first - 1L
                    let len = int64 ins2.Length
                    if left < 1L then None
                    elif int64 prefix >= len && deleted.Substring(prefix - ins2.Length, ins2.Length) = ins2 then
                        let dupFirst = left - len + 1L
                        Some(if len = 1L then sprintf "%sdup" (at dupFirst) else sprintf "%s_%ddup" (at dupFirst) left)
                    else Some(sprintf "%s_%dins%s" (at left) first ins2)
                else
                    let last = first + int64 del2.Length - 1L
                    Some(if del2.Length = 1 then sprintf "%sdelins%s" (at first) ins2 else sprintf "%s_%ddelins%s" (at first) last ins2)
            | _ -> None
        | _ -> None

/// ClinVar review status to the 0 to 4 stars ClinVar shows (its published star definitions).
let reviewStars (reviewStatus: string) : int =
    match (if isNull reviewStatus then "" else reviewStatus.Trim().ToLowerInvariant()) with
    | "practice guideline" -> 4
    | "reviewed by expert panel" -> 3
    | "criteria provided, multiple submitters, no conflicts" -> 2
    | "criteria provided, single submitter"
    | "criteria provided, conflicting classifications"
    | "criteria provided, conflicting interpretations" -> 1
    | _ -> 0

/// The pathogenicity terms of a germline classification. ClinVar joins terms with "/" ("Pathogenic/Likely pathogenic") and
/// appends other categories after ";" ("Pathogenic; drug response", "Pathogenic/Likely pathogenic; risk factor"), so only the
/// part before the first ";" says how pathogenic the variant is.
let private pathogenicityTerms (classification: string) : string list =
    let first = (if isNull classification then "" else classification).Split(';').[0].Trim().ToLowerInvariant()
    first.Split('/') |> Array.map (fun t -> t.Trim()) |> Array.toList

let private isPathogenicTerm (t: string) = t = "pathogenic" || t.StartsWith("pathogenic,", StringComparison.Ordinal)
let private isLikelyPathogenicTerm (t: string) = t = "likely pathogenic" || t.StartsWith("likely pathogenic,", StringComparison.Ordinal)

/// True when a germline classification satisfies the minimum. Conflicting, uncertain and benign records never count as
/// pathogenic, even though their text contains "pathogenic". Extra categories after a ";" do not matter, so
/// "Pathogenic; drug response" is pathogenic.
let meetsSignificance (min: MinSignificance) (classification: string) : bool =
    let terms = pathogenicityTerms classification
    let allPathogenicFamily = terms |> List.forall (fun t -> isPathogenicTerm t || isLikelyPathogenicTerm t)
    match min with
    | AnyClassification -> true
    | PathogenicOnly -> allPathogenicFamily && terms |> List.exists isPathogenicTerm
    | PathogenicOrLikely -> allPathogenicFamily

let private variantOfRecord (rec': JsonElement) : DiseaseVariant option =
    let uid = str (tryGet "uid" rec')
    if uid = "" then None
    else
        let set = elements (tryGet "variation_set" rec') |> List.tryHead
        let rs =
            set
            |> Option.bind (fun s ->
                elements (tryGet "variation_xrefs" s)
                |> List.tryPick (fun x ->
                    let id = str (tryGet "db_id" x)
                    if str (tryGet "db_source" x) = "dbSNP" && isDigits id then Some("rs" + id) else None))
        let hgvs = set |> Option.bind (fun s -> spdiToHgvs (str (tryGet "canonical_spdi" s)))
        let gene =
            match str (tryGet "gene_sort" rec') with
            | "" -> elements (tryGet "genes" rec') |> List.tryHead |> Option.map (fun g -> str (tryGet "symbol" g)) |> Option.filter (fun s -> s <> "")
            | g -> Some g
        let germline = tryGet "germline_classification" rec'
        Some
            { ClinVarId = uid
              Name = str (tryGet "title" rec')
              Gene = gene
              RsId = rs
              Hgvs = hgvs
              Significance = str (germline |> Option.bind (tryGet "description"))
              ReviewStatus = str (germline |> Option.bind (tryGet "review_status"))
              Traits =
                elements (germline |> Option.bind (tryGet "trait_set"))
                |> List.map (fun t -> str (tryGet "trait_name" t))
                |> List.filter (fun n -> n <> "") }

/// esummary response (db=clinvar, retmode=json) to variants, in the order NCBI lists them.
let parseEsummary (json: string) : DiseaseVariant list =
    withJson "NCBI esummary" json (fun root ->
        match tryGet "result" root with
        | None ->
            let reason = match tryGet "error" root with Some e -> str (Some e) | None -> "unexpected response"
            raise (GrnaUpstreamException(sprintf "NCBI esummary failed: %s" reason))
        | Some result ->
            strings (tryGet "uids" result)
            |> List.choose (fun uid -> tryGet uid result |> Option.bind variantOfRecord))

// ---- exports (pure) -----------------------------------------------------------------------------------------

/// One `rsNNN` per line, de-duplicated, for `grna design --input` and the web "download list".
let formatRsList (variants: DiseaseVariant list) : string =
    let sb = StringBuilder()
    for rs in variants |> List.choose (fun v -> v.RsId) |> List.distinct do
        sb.Append(rs).Append('\n') |> ignore
    sb.ToString()

let csvHeader = "rsID,HGVS,Gene,Classification,Review status,Stars,ClinVar ID,Variant,Conditions"

let private cell (value: string) = ReportSchema.csvField value

let formatCsv (variants: DiseaseVariant list) : string =
    let sb = StringBuilder()
    sb.Append(csvHeader).Append('\n') |> ignore
    for v in variants do
        let fields =
            [ cell (defaultArg v.RsId "")
              cell (defaultArg v.Hgvs "")
              cell (defaultArg v.Gene "")
              cell v.Significance
              cell v.ReviewStatus
              string (reviewStars v.ReviewStatus)
              v.ClinVarId
              cell v.Name
              cell (String.Join("; ", v.Traits)) ]
        sb.Append(String.Join(",", fields)).Append('\n') |> ignore
    sb.ToString()

let formatTsv (variants: DiseaseVariant list) : string =
    let clean (s: string) = s.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ')
    let sb = StringBuilder()
    sb.Append(csvHeader.Replace(',', '\t')).Append('\n') |> ignore
    for v in variants do
        let fields =
            [ defaultArg v.RsId ""; defaultArg v.Hgvs ""; defaultArg v.Gene ""; v.Significance; v.ReviewStatus
              string (reviewStars v.ReviewStatus); v.ClinVarId; v.Name; String.Join("; ", v.Traits) ]
        sb.Append(String.Join("\t", fields |> List.map clean)).Append('\n') |> ignore
    sb.ToString()

let formatJson (result: DiseaseVariantResult) : string =
    let items =
        result.Variants
        |> List.map (fun v ->
            {| rsId = Option.toObj v.RsId
               hgvs = Option.toObj v.Hgvs
               gene = Option.toObj v.Gene
               classification = v.Significance
               reviewStatus = v.ReviewStatus
               stars = reviewStars v.ReviewStatus
               clinvarId = v.ClinVarId
               name = v.Name
               conditions = v.Traits |> List.toArray |})
        |> List.toArray
    let doc =
        {| omimNumbers = result.OmimNumbers |> List.toArray
           totalMatching = result.TotalMatching
           truncated = result.Truncated
           variants = items |}
    JsonSerializer.Serialize(doc, JsonSerializerOptions(WriteIndented = true))

// ---- client -------------------------------------------------------------------------------------------------

type ClientOptions =
    { MonarchBase: string
      EutilsBase: string
      /// Most ClinVar records fetched for one search; more is reported as Truncated.
      MaxRecords: int
      /// Records per esummary call.
      BatchSize: int
      /// Most subtypes consulted when a disease has none of its own OMIM numbers.
      MaxSubtypes: int
      /// Minimum spacing between NCBI calls without / with an API key (NCBI allows 3 and 10 requests per second).
      MinInterval: TimeSpan
      MinIntervalWithKey: TimeSpan
      RetryDelay: TimeSpan
      Delay: TimeSpan -> CancellationToken -> Task
      Now: unit -> DateTimeOffset }

module ClientOptions =
    let defaults =
        { MonarchBase = "https://api.monarchinitiative.org/v3/api"
          EutilsBase = "https://eutils.ncbi.nlm.nih.gov/entrez/eutils"
          MaxRecords = 3000
          BatchSize = 200
          MaxSubtypes = 40
          MinInterval = TimeSpan.FromMilliseconds 340.0
          MinIntervalWithKey = TimeSpan.FromMilliseconds 110.0
          RetryDelay = TimeSpan.FromSeconds 2.0
          Delay = fun d ct -> Task.Delay(d, ct)
          Now = fun () -> DateTimeOffset.UtcNow }

    /// For C# hosts and tests: other service roots and delegates for waiting and for the clock.
    let create (monarchBase: string, eutilsBase: string, delay: Func<TimeSpan, CancellationToken, Task>, now: Func<DateTimeOffset>) =
        { defaults with
            MonarchBase = monarchBase
            EutilsBase = eutilsBase
            Delay = (fun d ct -> delay.Invoke(d, ct))
            Now = (fun () -> now.Invoke()) }

    /// Replaces the record, batch and subtype limits.
    let withLimits (maxRecords: int) (batchSize: int) (maxSubtypes: int) (options: ClientOptions) =
        { options with MaxRecords = maxRecords; BatchSize = batchSize; MaxSubtypes = maxSubtypes }

/// Serialises NCBI calls and keeps the minimum spacing between them. NCBI's limit is per client address, so one gate is
/// shared by every request the process makes through this module.
type RateGate(options: ClientOptions) =
    let sem = new SemaphoreSlim(1, 1)
    let mutable last = DateTimeOffset.MinValue

    member _.WaitAsync(interval: TimeSpan, ct: CancellationToken) : Task =
        task {
            do! sem.WaitAsync ct
            try
                let wait = last + interval - options.Now()
                if wait > TimeSpan.Zero then do! options.Delay wait ct
                last <- options.Now()
            finally
                sem.Release() |> ignore
        }

type DiseaseVariantClient(http: HttpClient, options: ClientOptions, gate: RateGate) =
    static let sharedHttp = new HttpClient(Timeout = TimeSpan.FromSeconds 60.0)
    static let shared = lazy (DiseaseVariantClient(sharedHttp, ClientOptions.defaults, RateGate ClientOptions.defaults))

    /// The process-wide client the web app and the CLI use.
    static member Shared = shared.Value

    /// Sends a request, retrying once on a network failure, 429 or 5xx. Ok body, or Error with the HTTP status (0 for a
    /// network failure).
    member private _.Execute(mk: unit -> HttpRequestMessage, ncbi: bool, ct: CancellationToken) : Task<Result<string, int>> =
        let env = GrnaEnvironment.getCurrent ()
        let interval = if env.NcbiApiKey.IsSome then options.MinIntervalWithKey else options.MinInterval
        let rec go (attempt: int) : Task<Result<string, int>> =
            task {
                if ncbi then do! gate.WaitAsync(interval, ct)
                let! outcome =
                    task {
                        try
                            use req = mk ()
                            use! resp = http.SendAsync(req, ct)
                            if resp.IsSuccessStatusCode then
                                let! body = resp.Content.ReadAsStringAsync ct
                                return Ok body
                            else
                                return Error(int resp.StatusCode)
                        with
                        | :? HttpRequestException -> return Error 0
                        | :? TaskCanceledException when not ct.IsCancellationRequested -> return Error 0
                    }
                match outcome with
                | Error code when attempt < 2 && (code = 0 || code = 429 || code >= 500) ->
                    do! options.Delay options.RetryDelay ct
                    return! go (attempt + 1)
                | other -> return other
            }
        go 1

    member private this.Require(what: string, outcome: Result<string, int>) : string =
        match outcome with
        | Ok body -> body
        | Error 0 -> raise (GrnaUpstreamException(sprintf "%s could not be reached." what))
        | Error code -> raise (GrnaUpstreamException(sprintf "%s returned HTTP %d." what code))

    member private this.MonarchGet(path: string, ct: CancellationToken) : Task<Result<string, int>> =
        this.Execute((fun () -> new HttpRequestMessage(HttpMethod.Get, options.MonarchBase + path)), false, ct)

    member private this.NcbiPost(endpoint: string, fields: (string * string) list, ct: CancellationToken) : Task<string> =
        task {
            let env = GrnaEnvironment.getCurrent ()
            let common =
                [ yield "tool", "gRNA"
                  yield "retmode", "json"
                  match env.NcbiContact with Some c -> yield "email", c | None -> ()
                  match env.NcbiApiKey with Some k -> yield "api_key", k | None -> () ]
            let mk () =
                let req = new HttpRequestMessage(HttpMethod.Post, sprintf "%s/%s" options.EutilsBase endpoint)
                req.Content <- new FormUrlEncodedContent(fields @ common |> List.map (fun (k, v) -> System.Collections.Generic.KeyValuePair(k, v)))
                req
            let! outcome = this.Execute(mk, true, ct)
            return this.Require("NCBI " + endpoint.Replace(".fcgi", ""), outcome)
        }

    /// Disease matches for what the user typed: an OMIM number needs no lookup, a MONDO id is fetched, a name is searched.
    member this.FindDiseasesAsync(text: string, ct: CancellationToken) : Task<DiseaseMatch list> =
        task {
            match parseQuery text with
            | ByOmim n ->
                return [ { Id = "OMIM:" + n; Name = "OMIM " + n; OmimNumbers = [ n ]; PhenotypicSeries = []; Subtypes = [] } ]
            | ByMondo id ->
                let! outcome = this.MonarchGet(sprintf "/entity/%s" id, ct)
                match outcome with
                | Error 404 -> return []
                | other -> return [ parseMonarchEntity (this.Require("Monarch", other)) ]
            | ByName name ->
                let! outcome =
                    this.MonarchGet(sprintf "/search?q=%s&category=biolink:Disease&limit=10" (Uri.EscapeDataString name), ct)
                return parseMonarchSearch (this.Require("Monarch", outcome))
        }

    /// The numeric OMIM numbers to query for a disease. A disease with none of its own (a phenotypic series such as Fanconi
    /// anemia) or with `includeSubtypes` set is expanded through its direct subtypes, up to `MaxSubtypes`.
    member this.ResolveOmimNumbersAsync(disease: DiseaseMatch, includeSubtypes: bool, ct: CancellationToken) : Task<ResolvedDisease> =
        task {
            if disease.Id.StartsWith("OMIM:", StringComparison.Ordinal) then
                return { Disease = disease; OmimNumbers = disease.OmimNumbers; SubtypesChecked = 0; SubtypesTotal = 0 }
            elif not disease.OmimNumbers.IsEmpty && not includeSubtypes then
                return { Disease = disease; OmimNumbers = disease.OmimNumbers; SubtypesChecked = 0; SubtypesTotal = 0 }
            else
                let! outcome = this.MonarchGet(sprintf "/entity/%s" (Uri.EscapeDataString disease.Id), ct)
                let full = parseMonarchEntity (this.Require("Monarch", outcome))
                let chosen = full.Subtypes |> List.truncate options.MaxSubtypes
                use throttle = new SemaphoreSlim(4)
                let fetch (s: DiseaseRef) =
                    task {
                        do! throttle.WaitAsync ct
                        try
                            let! o = this.MonarchGet(sprintf "/entity/%s" (Uri.EscapeDataString s.Id), ct)
                            match o with
                            | Error 404 -> return []
                            | other -> return (parseMonarchEntity (this.Require("Monarch", other))).OmimNumbers
                        finally
                            throttle.Release() |> ignore
                    }
                let! lists = chosen |> List.map fetch |> Task.WhenAll
                let numbers = (full.OmimNumbers @ (lists |> Array.toList |> List.concat)) |> List.distinct
                return
                    { Disease = { full with Subtypes = [] }
                      OmimNumbers = numbers
                      SubtypesChecked = chosen.Length
                      SubtypesTotal = full.Subtypes.Length }
        }

    /// The ClinVar variants linked to the OMIM numbers, filtered by classification.
    member this.GetVariantsAsync(omimNumbers: string list, minSignificance: MinSignificance, progress: IProgress<VariantProgress>, ct: CancellationToken)
        : Task<DiseaseVariantResult> =
        task {
            let numbers = omimNumbers |> List.map (fun n -> n.Trim()) |> List.distinct
            if numbers.IsEmpty then raise (GrnaInputException "There is no OMIM number to look up.")
            if numbers.Length > 100 then raise (GrnaInputException "Too many OMIM numbers in one search (100 at most).")
            for n in numbers do
                if not (isDigits n) || n.Length > 9 then raise (GrnaInputException(sprintf "'%s' is not an OMIM number." n))
            let report stage done' total =
                if not (isNull (box progress)) then progress.Report { Stage = stage; Done = done'; Total = total }

            report "Finding ClinVar records" 0 0
            let! linkJson = this.NcbiPost("elink.fcgi", [ "dbfrom", "omim"; "db", "clinvar"; "id", String.Join(",", numbers); "cmd", "neighbor_history" ], ct)
            match parseElinkHistory linkJson with
            | None -> return { OmimNumbers = numbers; TotalMatching = 0; Variants = []; Truncated = false }
            | Some(webEnv, queryKey) ->
                let term =
                    match minSignificance with
                    | PathogenicOrLikely -> "#1 AND (clinsig_pathogenic[prop] OR clinsig_likely_pathogenic[prop])"
                    | PathogenicOnly -> "#1 AND clinsig_pathogenic[prop]"
                    | AnyClassification -> "#1"
                report "Filtering by classification" 0 0
                let! searchJson =
                    this.NcbiPost("esearch.fcgi", [ "db", "clinvar"; "term", term; "WebEnv", webEnv; "query_key", queryKey; "retmax", string options.MaxRecords ], ct)
                let total, ids = parseEsearch searchJson
                let batches = ids |> List.chunkBySize (max 1 options.BatchSize)
                let collected = ResizeArray<DiseaseVariant>()
                let mutable processed = 0
                report "Reading variant details" 0 ids.Length
                for batch in batches do
                    let! summaryJson = this.NcbiPost("esummary.fcgi", [ "db", "clinvar"; "id", String.Join(",", batch) ], ct)
                    collected.AddRange(parseEsummary summaryJson)
                    processed <- processed + batch.Length
                    report "Reading variant details" processed ids.Length
                let variants =
                    collected
                    |> Seq.filter (fun v -> meetsSignificance minSignificance v.Significance)
                    |> Seq.distinctBy (fun v -> v.ClinVarId)
                    |> Seq.toList
                return { OmimNumbers = numbers; TotalMatching = total; Variants = variants; Truncated = total > ids.Length }
        }

/// How many of the variants carry an rsID.
let countWithRsId (variants: DiseaseVariant list) = variants |> List.filter (fun v -> v.RsId.IsSome) |> List.length

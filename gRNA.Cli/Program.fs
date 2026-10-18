module gRNA.Cli.Program

open System
open System.IO
open System.Text
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open gRNA

// Exit codes: distinguish bad input, a missing native dependency, and an upstream failure.
[<Literal>]
let ExitOk = 0
[<Literal>]
let ExitFailure = 1
[<Literal>]
let ExitBadInput = 2
[<Literal>]
let ExitDependency = 3
[<Literal>]
let ExitUpstream = 4

let usage =
    """grna - headless gRNA design pipeline

Usage:
  grna doctor [--network]
  grna resolve <rsID> [<rsID> ...] [--all-alleles]
  grna disease <name | OMIM number | MONDO id> [--list] [--pick 1] [--subtypes] [--min-significance pathogenic-likely|pathogenic|any]
                                              [--format rs|csv|tsv|json] [--out file]
  grna design --hgvs <HGVS> [--spacer 28] [--seed 10-17] [--complement] [--format csv|json|tsv] [--out file]
  grna design --input <list.txt|session.json> --out <report.csv> [--spacer 28] [--seed 10-17] [--complement] [--concurrency 2] [--all-alleles]
                                      [--snp-pos 3] [--mismatch-pos 4]
  grna fold <RNA sequence>
  grna pool --guides <report.csv> [--capacity 5] [--plate 96|384] [--model auto|2df|2dm|3d] [--out plan.csv]
  grna pool --count <V>          [--capacity 5] [--plate 96|384] [--model auto|2df|2dm|3d] [--out plan.csv]

Environment: GRNA_BOWTIE_BINARY, GRNA_BOWTIE_INDEX, GRNA_PYTHON, GRNA_NCBI_API_KEY, GRNA_NCBI_CONTACT
Exit codes: 0 ok, 1 failure, 2 bad input, 3 missing native dependency, 4 upstream failure
A design report in csv format is accepted unchanged by `grna pool --guides` and by the web Pooling tab.
`grna disease 603903 --format rs --out sickle.txt` followed by `grna design --input sickle.txt --out report.csv` goes from a disease to guides."""

exception BadArgs of string

/// Minimal option parsing: `--name value` pairs, bare `--flag`s and positional arguments.
type Args =
    { Positional: string list
      Options: Map<string, string>
      Flags: Set<string> }

let private flagNames = set [ "complement"; "network"; "help"; "list"; "subtypes"; "all-alleles" ]

let parseArgs (argv: string list) : Args =
    let rec go (rest: string list) (pos: string list) (opts: Map<string, string>) (flags: Set<string>) =
        match rest with
        | [] -> { Positional = List.rev pos; Options = opts; Flags = flags }
        | a :: tail when a.StartsWith "--" ->
            let name = a.Substring 2
            if flagNames.Contains name then
                go tail pos opts (Set.add name flags)
            else
                match tail with
                | v :: tail2 -> go tail2 pos (Map.add name v opts) flags
                | [] -> raise (BadArgs(sprintf "Option --%s needs a value." name))
        | a :: tail -> go tail (a :: pos) opts flags

    go argv [] Map.empty Set.empty

/// Options and flags each command understands; anything else is a typo and must not be silently ignored.
let private allowed =
    Map.ofList
        [ "doctor", (Set.empty, set [ "network"; "help" ])
          "resolve", (Set.empty, set [ "all-alleles"; "help" ])
          "disease", (set [ "pick"; "min-significance"; "format"; "out" ], set [ "list"; "subtypes"; "help" ])
          "design", (set [ "hgvs"; "input"; "spacer"; "seed"; "format"; "out"; "concurrency"; "snp-pos"; "mismatch-pos" ], set [ "complement"; "all-alleles"; "help" ])
          "fold", (Set.empty, set [ "help" ])
          "pool", (set [ "guides"; "count"; "capacity"; "plate"; "model"; "out" ], set [ "help" ]) ]

/// Throws BadArgs naming the first option or flag the command does not take.
let checkKnownOptions (command: string) (args: Args) : unit =
    match Map.tryFind command allowed with
    | None -> ()
    | Some (options, flags) ->
        for name in args.Options |> Map.toList |> List.map fst do
            if not (options.Contains name) then
                raise (BadArgs(sprintf "Unknown option --%s for '%s'. Run `grna --help` for the options." name command))
        for name in args.Flags do
            if not (flags.Contains name) then
                raise (BadArgs(sprintf "Option --%s does not apply to '%s'. Run `grna --help` for the options." name command))

let private opt (args: Args) name = Map.tryFind name args.Options

let private intOpt (args: Args) name (defaultValue: int) =
    match opt args name with
    | None -> defaultValue
    | Some v ->
        match Int32.TryParse v with
        | true, n -> n
        | _ -> raise (BadArgs(sprintf "--%s must be an integer, got '%s'." name v))

let parseSeed (text: string) =
    match text.Split('-', 2) with
    | [| a; b |] ->
        match Int32.TryParse a, Int32.TryParse b with
        | (true, s), (true, e) -> s, e
        | _ -> raise (BadArgs(sprintf "--seed must look like 10-17, got '%s'." text))
    | _ -> raise (BadArgs(sprintf "--seed must look like 10-17, got '%s'." text))

let private writeOut (out: string option) (text: string) =
    match out with
    | Some path -> File.WriteAllText(path, text, UTF8Encoding(false))
    | None -> Console.Out.Write text

// ---- commands ---------------------------------------------------------------------------------------

let doctor (args: Args) (ct: CancellationToken) : Task<int> =
    task {
        let env = GrnaEnvironment.getCurrent ()
        let! results = Diagnostics.runAll env (args.Flags.Contains "network")
        for r in results do
            let tag =
                match r.Status with
                | Diagnostics.Pass -> "PASS"
                | Diagnostics.Warn -> "WARN"
                | Diagnostics.Fail -> "FAIL"
            printfn "[%s] %s: %s" tag r.Name r.Detail
            if r.Remedy <> "" && r.Status <> Diagnostics.Pass then printfn "       -> %s" r.Remedy
        return if Diagnostics.isHealthy results then ExitOk else ExitDependency
    }

let private rsPattern = System.Text.RegularExpressions.Regex(@"^rs\d+$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)

/// True for "rs334"; a bare "rs" or "rs12x" is not an rsID.
let isRsId (input: string) = rsPattern.IsMatch input

let resolve (args: Args) (ct: CancellationToken) : Task<int> =
    task {
        if args.Positional.IsEmpty then raise (BadArgs "resolve needs at least one rsID.")
        for rs in args.Positional do
            if not (isRsId rs) && not (rs |> Seq.forall Char.IsDigit && rs <> "") then
                raise (BadArgs(sprintf "'%s' is not an rsID (expected rs334 or 334)." rs))
            let id = if rs.StartsWith("rs", StringComparison.OrdinalIgnoreCase) then rs.Substring 2 else rs
            let! hgvs = (if args.Flags.Contains "all-alleles" then SNP.getAllHgvsNotationsAsync id ct else SNP.getHgvsNotationsAsync id ct)
            if hgvs.IsEmpty then eprintfn "%s: no NG_ notations found" rs
            for h in hgvs do printfn "%s\t%s" rs h
        return ExitOk
    }

/// The Monarch/NCBI client the `disease` command uses. A test replaces it with one over a stubbed HTTP layer.
let mutable diseaseClient : DiseaseVariants.DiseaseVariantClient = DiseaseVariants.DiseaseVariantClient.Shared

let private diseaseFormats = set [ "rs"; "csv"; "tsv"; "json" ]

let private parseMinSignificance (text: string) : DiseaseVariants.MinSignificance =
    match text.ToLowerInvariant() with
    | "pathogenic-likely" -> DiseaseVariants.PathogenicOrLikely
    | "pathogenic" -> DiseaseVariants.PathogenicOnly
    | "any" -> DiseaseVariants.AnyClassification
    | _ -> raise (BadArgs(sprintf "--min-significance must be pathogenic-likely, pathogenic or any, got '%s'." text))

let private describeMatch (m: DiseaseVariants.DiseaseMatch) =
    let numbers =
        if not m.OmimNumbers.IsEmpty then "OMIM " + String.Join(", ", m.OmimNumbers)
        elif not m.PhenotypicSeries.IsEmpty then "phenotypic series " + String.Join(", ", m.PhenotypicSeries) + " (use --subtypes)"
        else "no OMIM number"
    sprintf "%s	%s	%s" m.Id m.Name numbers

/// Finds the ClinVar variants of a disease: name, OMIM number or MONDO id in; rsIDs (or a table) out. Every option is
/// checked before the first request, so a typo costs nothing.
let disease (args: Args) (ct: CancellationToken) : Task<int> =
    task {
        let query =
            match args.Positional with
            | [ q ] -> q
            | [] -> raise (BadArgs "disease needs a disease name, an OMIM number or a MONDO id, for example: grna disease 603903")
            | _ -> raise (BadArgs "disease takes one search text; put a name with spaces in quotes.")
        let format = (opt args "format" |> Option.defaultValue "rs").ToLowerInvariant()
        if not (diseaseFormats.Contains format) then raise (BadArgs(sprintf "--format must be rs, csv, tsv or json, got '%s'." format))
        let minSignificance = opt args "min-significance" |> Option.defaultValue "pathogenic-likely" |> parseMinSignificance
        let pick = intOpt args "pick" 1
        if pick < 1 then raise (BadArgs "--pick must be 1 or more.")
        DiseaseVariants.parseQuery query |> ignore   // a malformed OMIM number or MONDO id is refused here, before any request

        let! matches = diseaseClient.FindDiseasesAsync(query, ct)
        if matches.IsEmpty then
            eprintfn "error: no disease found for '%s'." query
            return ExitBadInput
        elif args.Flags.Contains "list" then
            matches |> List.iteri (fun i m -> printfn "%d	%s" (i + 1) (describeMatch m))
            return ExitOk
        elif pick > matches.Length then
            return raise (BadArgs(sprintf "--pick %d, but only %d matches were found (see --list)." pick matches.Length))
        else
            let chosen = matches.[pick - 1]
            if matches.Length > 1 && not (args.Options.ContainsKey "pick") then
                eprintfn "note: using match 1 of %d: %s (%s). Use --list to see the others, --pick N to choose." matches.Length chosen.Name chosen.Id
            let! resolved = diseaseClient.ResolveOmimNumbersAsync(chosen, args.Flags.Contains "subtypes", ct)
            if resolved.SubtypesTotal > 0 then
                eprintfn "note: consulted %d of %d subtypes of %s." resolved.SubtypesChecked resolved.SubtypesTotal chosen.Name
            if resolved.OmimNumbers.IsEmpty then
                eprintfn "error: %s (%s) has no numeric OMIM number%s." chosen.Name chosen.Id
                    (if chosen.PhenotypicSeries.IsEmpty || args.Flags.Contains "subtypes" then "" else "; it is a phenotypic series, try --subtypes")
                return ExitBadInput
            else
                eprintfn "Searching ClinVar for OMIM %s ..." (String.Join(", ", resolved.OmimNumbers))
                let progress =
                    { new IProgress<DiseaseVariants.VariantProgress> with
                        member _.Report p = if p.Total > 0 then eprintfn "  %s: %d of %d" p.Stage p.Done p.Total }
                let! result = diseaseClient.GetVariantsAsync(resolved.OmimNumbers, minSignificance, progress, ct)
                let withRs = DiseaseVariants.countWithRsId result.Variants
                eprintfn "%d ClinVar records matched; %d listed, %d with an rsID, %d without." result.TotalMatching result.Variants.Length withRs (result.Variants.Length - withRs)
                if result.Truncated then
                    eprintfn "warning: only part of the %d matching records was read, so the list is incomplete. Narrow the search (--min-significance pathogenic) or search the OMIM numbers one at a time." result.TotalMatching
                if format = "rs" && withRs < result.Variants.Length then
                    eprintfn "note: records without an rsID are left out of the rs list; --format csv shows their HGVS."
                let text =
                    match format with
                    | "csv" -> DiseaseVariants.formatCsv result.Variants
                    | "tsv" -> DiseaseVariants.formatTsv result.Variants
                    | "json" -> DiseaseVariants.formatJson result
                    | _ -> DiseaseVariants.formatRsList result.Variants
                writeOut (opt args "out") text
                return ExitOk
    }

type DesignRow =
    { Hgvs: string
      RsId: string option
      Complement: bool
      Result: Main.ResultFromHGVS }

let private jsonOf (rows: DesignRow list) =
    let opts = JsonSerializerOptions(WriteIndented = true)
    let items =
        rows
        |> List.map (fun r ->
            let cands (xs: SpacerFinder.gRNAResult list) =
                xs
                |> List.map (fun g ->
                    {| rank = g.Rank
                       sequence = g.Sequence
                       score = g.Score
                       gcContent = g.GCContent
                       alignments = g.Allignments
                       seedRegion = g.SeedRegion
                       homopolymers = g.HomopolymerCount
                       energy = g.RnaFoldResult.Energy
                       wtMismatches = g.WtMismatches
                       engineered = g.Engineered
                       scaffoldIntact = g.ScaffoldIntact
                       spacerUnpaired = g.SpacerUnpaired
                       structure = g.RnaFoldResult.Structure |})
            {| hgvs = r.Hgvs
               rsId = Option.toObj r.RsId
               strand = if r.Complement then "Complement" else "Normal"
               notices = r.Result.notices
               mutated = cands r.Result.gRNA
               original = cands r.Result.originalGRNA |})
    JsonSerializer.Serialize(items, opts)

let private csvOf (rows: DesignRow list) (rule: Main.SpecialRuleOptions) (spacer, seedS, seedE) (includeHeader: bool) =
    let sb = StringBuilder()
    if includeHeader then
        sb.AppendLine(ReportSchema.provenanceWithRule rule.SnpPos rule.MismatchPos spacer seedS seedE) |> ignore
        sb.AppendLine ReportSchema.header |> ignore
    for r in rows do
        for g in r.Result.gRNA do
            sb.AppendLine(ReportSchema.row r.RsId r.Hgvs ReportSchema.mutatedType r.Complement g) |> ignore
        for g in r.Result.originalGRNA do
            sb.AppendLine(ReportSchema.row r.RsId r.Hgvs ReportSchema.originalType r.Complement g) |> ignore
    sb.ToString()

let private tsvOf (rows: DesignRow list) (rule: Main.SpecialRuleOptions) =
    // Split each CSV row into fields first: a plain Replace(',', '\t') would also break quoted fields that contain commas.
    (csvOf rows rule (0, 0, 0) false).Split([| '\n' |], StringSplitOptions.RemoveEmptyEntries)
    |> Array.map (fun l -> l.TrimEnd('\r') |> ReportSchema.splitCsvLine |> String.concat "\t")
    |> String.concat "\n"
    |> fun s -> ReportSchema.header.Replace(',', '\t') + "\n" + s + "\n"

/// A web-app session document (.json) carries the inputs and the spacer/seed used; see SessionDocument in the app.
let private readSession (file: string) =
    if not (File.Exists file) then raise (BadArgs(sprintf "Input file '%s' not found." file))
    let text = File.ReadAllText file
    if text.TrimStart().StartsWith "{" then
        try
            use doc = JsonDocument.Parse text
            let root = doc.RootElement
            let str (n: string) = match root.TryGetProperty n with | true, v when v.ValueKind = JsonValueKind.String -> Some(v.GetString()) | _ -> None
            let num (n: string) = match root.TryGetProperty n with | true, v when v.ValueKind = JsonValueKind.Number -> Some(v.GetInt32()) | _ -> None
            Some(defaultArg (str "input") "", num "spacer", str "seed")
        with :? JsonException -> raise (BadArgs(sprintf "'%s' is not a valid session file." file))
    else
        None

let design (args: Args) (ct: CancellationToken) : Task<int> =
    task {
        let session = opt args "input" |> Option.bind (fun f -> if f.EndsWith(".json", StringComparison.OrdinalIgnoreCase) then readSession f else None)
        let sessionSpacer = session |> Option.bind (fun (_, sp, _) -> sp)
        let sessionSeed = session |> Option.bind (fun (_, _, sd) -> sd)
        let spacer = intOpt args "spacer" (defaultArg sessionSpacer 28)
        let seedS, seedE = parseSeed (defaultArg (opt args "seed") (defaultArg sessionSeed "10-17"))
        // Same bounds as the library and the web app (spacer 18-60, seed inside the spacer); a GrnaInputException ends as exit 2.
        Main.validateSpacerParameters spacer seedS seedE
        // Where the substitution rule puts the variant and its extra mismatch, counted from the spacer's 3' end (1 = last letter).
        let rule : Main.SpecialRuleOptions =
            { SnpPos = intOpt args "snp-pos" Main.defaultSpecialRule.SnpPos
              MismatchPos = intOpt args "mismatch-pos" Main.defaultSpecialRule.MismatchPos }
        Main.validateSpecialRule spacer rule
        let complement = args.Flags.Contains "complement"
        let concurrency = max 1 (min 4 (intOpt args "concurrency" 2))
        let out = opt args "out"
        let format = (defaultArg (opt args "format") "csv").ToLowerInvariant()
        if not (List.contains format [ "csv"; "json"; "tsv" ]) then raise (BadArgs "--format must be csv, json or tsv.")

        let inputs =
            match opt args "hgvs", opt args "input" with
            | Some h, None -> [ h ]
            | None, Some file when session.IsSome ->
                let (input, _, _) = session.Value
                input.Split([| ','; ';'; '\n'; '\r' |], StringSplitOptions.RemoveEmptyEntries)
                |> Array.map _.Trim()
                |> Array.filter (fun l -> l <> "")
                |> Array.distinct
                |> List.ofArray
            | None, Some file ->
                if not (File.Exists file) then raise (BadArgs(sprintf "Input file '%s' not found." file))
                File.ReadAllLines file
                |> Array.collect (fun l -> l.Split([| ','; ';' |]))
                |> Array.map _.Trim()
                |> Array.filter (fun l -> l <> "" && not (l.StartsWith "#"))
                |> Array.distinct
                |> List.ofArray
            | _ -> raise (BadArgs "design needs exactly one of --hgvs or --input.")

        if inputs.IsEmpty then
            raise (BadArgs(sprintf "No variants found in '%s'." (defaultArg (opt args "input") "the input")))

        // Resumable batch: with an existing csv report, variants already present are skipped and rows appended.
        // A variant is identified by rsID, HGVS and strand, and a report made with another spacer/seed cannot be resumed
        // (its rows would be mixed with rows designed under different parameters).
        let isBatchCsv = opt args "input" |> Option.isSome && format = "csv" && out.IsSome
        let strandName = if complement then ReportSchema.complementStrand else ReportSchema.normalStrand
        let existingLines =
            if isBatchCsv && File.Exists out.Value then File.ReadAllLines out.Value |> Array.filter (fun l -> l.Trim() <> "") else [||]
        match existingLines |> Array.tryHead |> Option.bind ReportSchema.tryParseProvenance with
        | Some (sp, ss, se) when (sp, ss, se) <> (spacer, seedS, seedE) ->
            raise (BadArgs(sprintf "'%s' was made with spacer=%d seed=%d-%d, not spacer=%d seed=%d-%d. Use a new --out file or the same parameters." out.Value sp ss se spacer seedS seedE))
        | _ -> ()
        // Rows made with another substitution rule would be mixed with these: refuse, like a different spacer or seed.
        match existingLines |> Array.tryHead |> Option.bind ReportSchema.tryParseRule with
        | Some (sn, mm) when (sn, mm) <> (rule.SnpPos, rule.MismatchPos) ->
            raise (BadArgs(sprintf "'%s' was made with --snp-pos %d --mismatch-pos %d, not --snp-pos %d --mismatch-pos %d. Use a new --out file or the same positions." out.Value sn mm rule.SnpPos rule.MismatchPos))
        | _ -> ()
        let alreadyDone =
            existingLines
            |> Array.filter (fun l -> not (l.StartsWith "#"))
            |> Array.skip (min 1 (existingLines |> Array.filter (fun l -> not (l.StartsWith "#")) |> Array.length))
            |> Array.choose (fun l ->
                match ReportSchema.splitCsvLine l with
                | rs :: hgvs :: _ :: _ :: _ :: _ :: _ :: _ :: _ :: _ :: _ :: strand :: _ -> Some(rs + "|" + hgvs + "|" + strand)
                | rs :: hgvs :: _ -> Some(rs + "|" + hgvs + "|" + ReportSchema.normalStrand)   // reports from before the Strand column
                | _ -> None)
            |> Set.ofArray

        let bowtie = Services.BowtieService()
        let results = System.Collections.Concurrent.ConcurrentBag<int * DesignRow>()
        let errors = System.Collections.Concurrent.ConcurrentBag<int * exn>()

        // Expand rsIDs to HGVS notations (sequentially: pure HTTP, rate limited by NCBI anyway).
        let jobs = ResizeArray<int * string option * string>()
        let mutable idx = 0
        for input in inputs do
            if isRsId input then
                let! hgvs = (if args.Flags.Contains "all-alleles" then SNP.getAllHgvsNotationsAsync (input.Substring 2) ct else SNP.getHgvsNotationsAsync (input.Substring 2) ct)
                if hgvs.IsEmpty then eprintfn "%s: no NG_ notations found" input
                for h in hgvs do
                    jobs.Add((idx, Some(input.Substring 2), h))
                    idx <- idx + 1
            else
                jobs.Add((idx, None, input))
                idx <- idx + 1

        let work =
            jobs
            |> Seq.filter (fun (_, rs, h) -> not (alreadyDone.Contains((defaultArg rs "") + "|" + h + "|" + strandName)))
            |> List.ofSeq

        let sem = new SemaphoreSlim(concurrency)
        let tasks =
            work
            |> List.map (fun (i, rs, h) ->
                task {
                    do! sem.WaitAsync ct
                    try
                        try
                            let! r = Main.getBestgRNAFromHGVSWithRule rule h spacer seedS seedE bowtie ct complement
                            results.Add((i, { Hgvs = h; RsId = rs; Complement = complement; Result = r }))
                            eprintfn "ok   %s" h
                            for notice in r.notices do eprintfn "note %s: %s" h notice
                        with
                        | :? OperationCanceledException as oce -> raise oce
                        | ex ->
                            errors.Add((i, ex))
                            eprintfn "FAIL %s: %s" h ex.Message
                    finally
                        sem.Release() |> ignore
                }
                :> Task)
        do! Task.WhenAll tasks

        let rows = results |> Seq.sortBy fst |> Seq.map snd |> List.ofSeq
        let body =
            match format with
            | "json" -> jsonOf rows
            | "tsv" -> tsvOf rows rule
            | _ -> csvOf rows rule (spacer, seedS, seedE) (not isBatchCsv || not (File.Exists out.Value) || alreadyDone.IsEmpty)

        if isBatchCsv && File.Exists out.Value && not alreadyDone.IsEmpty then
            File.AppendAllText(out.Value, body, UTF8Encoding(false))
        else
            writeOut out body

        // Nothing succeeded: the exit code is that of the FIRST failed variant in input order (deterministic, unlike
        // "whichever task finished last"). Some succeeded, some failed: plain failure, the report holds the successes.
        if errors.Count > 0 && rows.IsEmpty then
            match errors |> Seq.sortBy fst |> Seq.map snd |> Seq.tryHead with
            | Some (:? GrnaDependencyException) -> return ExitDependency
            | Some (:? GrnaInputException) -> return ExitBadInput
            | Some (:? GrnaUpstreamException) -> return ExitUpstream
            | _ -> return ExitFailure
        else
            return (if errors.Count > 0 then ExitFailure else ExitOk)
    }

let fold (args: Args) (ct: CancellationToken) : Task<int> =
    task {
        match args.Positional with
        | [ seq ] ->
            let rna = seq.Trim().ToUpperInvariant().Replace('T', 'U')
            if rna = "" || rna |> Seq.exists (fun c -> not ("ACGU".Contains c)) then
                raise (BadArgs "fold expects an RNA/DNA sequence of A, C, G, U/T.")
            let! r = RNAFoldWrapper.fold rna ct
            printfn "%s\n%s\t%.2f" rna r.Structure r.Energy
            return ExitOk
        | _ -> return raise (BadArgs "fold takes exactly one sequence.")
    }

/// Reads a Builder CSV report: one guide per variant (best-ranked mutated spacer of the first strand seen).
let private guidesFromReport (path: string) =
    if not (File.Exists path) then raise (BadArgs(sprintf "Guide file '%s' not found." path))
    let lines =
        File.ReadAllLines path
        |> Array.map _.Trim()
        |> Array.filter (fun l -> l <> "" && not (l.StartsWith "#"))
    if lines.Length = 0 then []
    else
        let delim = if (lines[0] |> Seq.filter ((=) '\t') |> Seq.length) > (lines[0] |> Seq.filter ((=) ',') |> Seq.length) then '\t' else ','
        let isReport =
            let f = lines[0].Split delim |> Array.map _.Trim()
            f.Length >= 3 && f[0] = "RS ID" && f[1] = "HGVS" && f[2] = "Sequence Type"
        if not isReport then
            lines |> Array.collect (fun l -> l.Split(',', StringSplitOptions.RemoveEmptyEntries)) |> Array.map _.Trim() |> Array.distinct |> List.ofArray
        else
            // One guide per variant, in report order: the first strand seen for a variant is the one kept.
            let strandCol = ReportSchema.columns |> List.findIndex ((=) "Strand")
            let strand = Collections.Generic.Dictionary<string, string>()
            let order = ResizeArray<string>()
            for l in lines |> Array.skip 1 do
                let f = l.Split delim
                if f.Length >= 5 && f[2].Trim() = ReportSchema.mutatedType then
                    let h = f[1].Trim()
                    let s = if f.Length > strandCol then f[strandCol].Trim() else ""
                    if not (strand.ContainsKey h) then
                        strand[h] <- s
                        order.Add h
            List.ofSeq order

let pool (args: Args) (ct: CancellationToken) : Task<int> =
    task {
        let capacity = intOpt args "capacity" 5
        let format =
            match (defaultArg (opt args "plate") "96") with
            | "96" -> Pooling.plate96
            | "384" -> Pooling.plate384
            | p -> raise (BadArgs(sprintf "--plate must be 96 or 384, got '%s'." p))

        let labels =
            match opt args "guides", opt args "count" with
            | Some file, None -> guidesFromReport file
            | None, Some c ->
                match Int32.TryParse c with
                | true, n when n > 0 && n <= Pooling.MaxGuideCount -> [ for i in 1..n -> sprintf "Guide %d" i ]
                | _ -> raise (BadArgs(sprintf "--count must be an integer from 1 to %d." Pooling.MaxGuideCount))
            | _ -> raise (BadArgs "pool needs exactly one of --guides or --count.")

        let v = labels.Length
        if v = 0 then raise (BadArgs "No guides found.")

        // The library rejects impossible sizes with ArgumentException; report those as bad input, never as a crash.
        let model =
            try
                match (defaultArg (opt args "model") "auto").ToLowerInvariant() with
                | "auto" -> Pooling.bestModel v capacity
                | "2df" -> Pooling.TwoDFragmented
                | "2dm" -> Pooling.TwoDMatrix
                | "3d" -> Pooling.ThreeD
                | m -> raise (BadArgs(sprintf "--model must be auto, 2df, 2dm or 3d, got '%s'." m))
            with :? ArgumentException as ex -> raise (BadArgs ex.Message)

        let plan =
            try Pooling.buildPlan model format v capacity
            with :? ArgumentException as ex -> raise (BadArgs ex.Message)

        let sb = StringBuilder()
        sb.AppendLine "Tube ID,Mixture Name,Guides in Tandem,Well" |> ignore
        for p in plan.Pools do
            let contents =
                if p.GuideIndices.IsEmpty then "Empty - do not prepare"
                else p.GuideIndices |> List.map (fun i -> labels[i - 1]) |> String.concat "; "
            sb.AppendLine(String.Join(",", [ string p.Id; ReportSchema.csvField p.Name; ReportSchema.csvField contents; ReportSchema.csvField p.Well.Label ])) |> ignore
        writeOut (opt args "out") (sb.ToString())
        eprintfn "%s: V=%d K=%d -> %d wells (%d to prepare), each guide in %d" (Pooling.modelName model) v capacity plan.TotalWells plan.NonEmptyWells (Pooling.repetitions model)
        return ExitOk
    }

let run (argv: string[]) (ct: CancellationToken) : Task<int> =
    task {
        match List.ofArray argv with
        | [] ->
            printfn "%s" usage
            return ExitBadInput
        | ("-h" | "--help" | "help") :: _ ->
            printfn "%s" usage
            return ExitOk
        | cmd :: rest ->
            try
                let args = parseArgs (rest |> List.filter (fun a -> a <> "-h"))
                let wantsHelp = rest |> List.contains "-h" || args.Flags.Contains "help"
                if not wantsHelp then checkKnownOptions cmd args
                match cmd with
                | _ when wantsHelp ->
                    printfn "%s" usage
                    return ExitOk
                | "doctor" -> return! doctor args ct
                | "resolve" -> return! resolve args ct
                | "disease" -> return! disease args ct
                | "design" -> return! design args ct
                | "fold" -> return! fold args ct
                | "pool" -> return! pool args ct
                | other ->
                    eprintfn "Unknown command '%s'.\n\n%s" other usage
                    return ExitBadInput
            with
            | BadArgs msg ->
                eprintfn "error: %s" msg
                return ExitBadInput
            | :? GrnaInputException as ex ->
                eprintfn "error: %s" ex.Message
                return ExitBadInput
            | :? GrnaDependencyException as ex ->
                eprintfn "error: %s\nRun `grna doctor` for details." ex.Message
                return ExitDependency
            | :? GrnaUpstreamException as ex ->
                eprintfn "error: %s" ex.Message
                return ExitUpstream
            | :? OperationCanceledException ->
                eprintfn "cancelled."
                return ExitFailure
    }

[<EntryPoint>]
let main argv =
    use cts = new CancellationTokenSource()
    Console.CancelKeyPress.Add(fun e ->
        e.Cancel <- true
        cts.Cancel())
    (run argv cts.Token).GetAwaiter().GetResult()

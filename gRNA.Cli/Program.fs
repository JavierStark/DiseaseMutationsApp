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
  grna design --hgvs <HGVS> [--spacer 23] [--seed 10-17] [--repeat default|none|<RNA>] [--complement] [--format csv|json|tsv] [--out file]
  grna design --input <list.txt|session.json> --out <report.csv> [--spacer 23] [--seed 10-17] [--repeat default|none|<RNA>] [--complement] [--concurrency 2] [--all-alleles]
                                      [--snp-pos 9] [--mismatch-pos 10] [--mismatches 1|2] [--bases A>A,G>A,C>C,U>C] [--no-rule]
  grna fold <RNA sequence>
  grna pool --guides <report.csv> [--capacity 5] [--plate 96|384] [--model auto|2df|2dm|3d] [--out plan.csv]
  grna pool --count <V>          [--capacity 5] [--plate 96|384] [--model auto|2df|2dm|3d] [--out plan.csv]
  grna export <session.json | report.csv> [--format genbank|sbol|sbol2] [--out file] [--t7] [--top 1] [--namespace <URL>] [--date yyyy-MM-dd]
                                          [--dr lwa|none] [--frida] [--biobricks] [--custom5 <seq>] [--custom3 <seq>]

--repeat is the 5' context every spacer is folded with while screening: default (the 36 nt Cas13 scaffold), none (the spacer alone)
or your own direct repeat as an RNA/DNA sequence. It changes the energy, hairpin and unpaired-share columns, not the alignments.

Environment: GRNA_BOWTIE_BINARY, GRNA_BOWTIE_INDEX, GRNA_PYTHON, GRNA_NCBI_API_KEY, GRNA_NCBI_CONTACT
Exit codes: 0 ok, 1 failure, 2 bad input, 3 missing native dependency, 4 upstream failure
A design report in csv format is accepted unchanged by `grna pool --guides` and by the web Pooling tab.
`grna export` writes the complete guides (what would be ordered) as a GenBank file or an SBOL 3 document. It reads the shortlist and
construct of a session saved by the web app, or the best guides of a design report (then --dr/--frida/--biobricks/--custom5/--custom3 build
the construct). It needs no Bowtie, ViennaRNA or network.
`grna disease 603903 --format rs --out sickle.txt` followed by `grna design --input sickle.txt --out report.csv` goes from a disease to guides."""

exception BadArgs of string

/// Minimal option parsing: `--name value` pairs, bare `--flag`s and positional arguments.
type Args =
    { Positional: string list
      Options: Map<string, string>
      Flags: Set<string> }

let private flagNames = set [ "complement"; "network"; "help"; "list"; "subtypes"; "all-alleles"; "t7"; "frida"; "biobricks" ]

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
          "design", (set [ "hgvs"; "input"; "spacer"; "seed"; "repeat"; "format"; "out"; "concurrency"; "snp-pos"; "mismatch-pos"; "mismatches"; "bases" ], set [ "complement"; "all-alleles"; "no-rule"; "help" ])
          "fold", (Set.empty, set [ "help" ])
          "pool", (set [ "guides"; "count"; "capacity"; "plate"; "model"; "out" ], set [ "help" ])
          "export", (set [ "format"; "out"; "top"; "dr"; "custom5"; "custom3"; "namespace"; "date" ], set [ "t7"; "frida"; "biobricks"; "help" ]) ]

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
                       offtargets = g.Offtargets
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

let private csvOf (rows: DesignRow list) (rule: Main.SpecialRuleOptions) (repeat: string) (spacer, seedS, seedE) (includeHeader: bool) =
    let sb = StringBuilder()
    if includeHeader then
        sb.AppendLine(ReportSchema.provenanceWithRuleAndRepeat rule repeat spacer seedS seedE) |> ignore
        sb.AppendLine ReportSchema.header |> ignore
    for r in rows do
        for g in r.Result.gRNA do
            sb.AppendLine(ReportSchema.row r.RsId r.Hgvs ReportSchema.mutatedType r.Complement g) |> ignore
        for g in r.Result.originalGRNA do
            sb.AppendLine(ReportSchema.row r.RsId r.Hgvs ReportSchema.originalType r.Complement g) |> ignore
    sb.ToString()

let private tsvOf (rows: DesignRow list) (rule: Main.SpecialRuleOptions) =
    // Split each CSV row into fields first: a plain Replace(',', '\t') would also break quoted fields that contain commas.
    (csvOf rows rule "" (0, 0, 0) false).Split([| '\n' |], StringSplitOptions.RemoveEmptyEntries)
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
            Some(defaultArg (str "input") "", num "spacer", str "seed", str "repeat")
        with :? JsonException -> raise (BadArgs(sprintf "'%s' is not a valid session file." file))
    else
        None

let design (args: Args) (ct: CancellationToken) : Task<int> =
    task {
        let session = opt args "input" |> Option.bind (fun f -> if f.EndsWith(".json", StringComparison.OrdinalIgnoreCase) then readSession f else None)
        let sessionSpacer = session |> Option.bind (fun (_, sp, _, _) -> sp)
        let sessionSeed = session |> Option.bind (fun (_, _, sd, _) -> sd)
        let sessionRepeat = session |> Option.bind (fun (_, _, _, rp) -> rp)
        let spacer = intOpt args "spacer" (defaultArg sessionSpacer SpacerFinder.DefaultSpacerSize)
        let seedS, seedE = parseSeed (defaultArg (opt args "seed") (defaultArg sessionSeed "10-17"))
        // Same bounds as the library and the web app (spacer 18-60, seed inside the spacer); a GrnaInputException ends as exit 2.
        Main.validateSpacerParameters spacer seedS seedE
        // default = the Cas13 scaffold, none = the spacer alone, anything else a direct repeat (validated like in the web app).
        let repeat =
            match (defaultArg (opt args "repeat") (defaultArg sessionRepeat "default")).Trim() with
            | r when r.Equals("default", StringComparison.OrdinalIgnoreCase) -> SpacerFinder.scaffold
            | r when r.Equals("none", StringComparison.OrdinalIgnoreCase) -> ""
            | r -> SpacerFinder.normaliseRepeat r
        // Where the substitution rule puts the variant and its engineered mismatches, counted from the spacer's 3' end (1 = last
        // letter): --mismatch-pos is the first one, --mismatches 2 adds the next, --bases is the "sequence base > guide base" table.
        let rule : Main.SpecialRuleOptions =
            { SnpPos = intOpt args "snp-pos" Main.defaultSpecialRule.SnpPos
              MismatchPos = intOpt args "mismatch-pos" Main.defaultSpecialRule.MismatchPos
              MismatchCount = intOpt args "mismatches" Main.defaultSpecialRule.MismatchCount
              EngineeredBases = defaultArg (opt args "bases") Main.defaultSpecialRule.EngineeredBases
              Enabled = not (args.Flags.Contains "no-rule") }
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
                let (input, _, _, _) = session.Value
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
        match existingLines |> Array.tryHead |> Option.bind ReportSchema.tryParseRepeat with
        | Some earlier when earlier <> repeat ->
            raise (BadArgs(sprintf "'%s' was made with another direct repeat (%s), not %s. Use a new --out file or the same --repeat." out.Value (ReportSchema.repeatToken earlier) (ReportSchema.repeatToken repeat)))
        | _ -> ()
        // Rows made with another substitution rule would be mixed with these: refuse, like a different spacer or seed.
        match existingLines |> Array.tryHead |> Option.bind ReportSchema.tryParseRule with
        | Some earlier when not (ReportSchema.sameRule earlier rule) ->
            raise (BadArgs(sprintf "'%s' was made with another substitution rule (%s), not %s. Use a new --out file or the same --snp-pos, --mismatch-pos, --mismatches and --bases." out.Value (ReportSchema.ruleToken earlier) (ReportSchema.ruleToken rule)))
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
                            let! r = Main.getBestgRNAFromHGVSWithRuleAndRepeat rule repeat h spacer seedS seedE bowtie ct complement
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
            | _ -> csvOf rows rule repeat (spacer, seedS, seedE) (not isBatchCsv || not (File.Exists out.Value) || alreadyDone.IsEmpty)

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
        sb.AppendLine "Tube ID,Guides in Tandem,Well" |> ignore
        for p in plan.Pools do
            let contents =
                if p.GuideIndices.IsEmpty then "Empty - do not prepare"
                else p.GuideIndices |> List.map (fun i -> labels[i - 1]) |> String.concat "; "
            sb.AppendLine(String.Join(",", [ string p.Id; ReportSchema.csvField contents; ReportSchema.csvField p.Well.Label ])) |> ignore
        writeOut (opt args "out") (sb.ToString())
        eprintfn "%s: V=%d K=%d -> %d wells (%d to prepare), each guide in %d" (Pooling.modelName model) v capacity plan.TotalWells plan.NonEmptyWells (Pooling.repetitions model)
        return ExitOk
    }

// ---- export -----------------------------------------------------------------------------------------

let private mkGuide hgvs rsId complement spacer rank score offtargets : Export.ExportGuide =
    { Hgvs = hgvs; RsId = rsId; Complement = complement; Spacer = spacer; Rank = rank; Score = score; Offtargets = offtargets }

let private mkConstruct dr frida bioBricks custom5 custom3 : Construct.ConstructOptions =
    { DrId = dr; Frida = frida; BioBricks = bioBricks; Custom5 = custom5; Custom3 = custom3 }

/// The shortlist and construct of a session saved by the web app (camelCase JSON).
let private guidesFromSession (text: string) (file: string) : Export.ExportGuide list * Construct.ConstructOptions option =
    try
        use doc = JsonDocument.Parse text
        let root = doc.RootElement
        let str (e: JsonElement) (n: string) =
            match e.TryGetProperty n with
            | true, v when v.ValueKind = JsonValueKind.String -> Some(v.GetString())
            | _ -> None
        let flag (e: JsonElement) (n: string) =
            match e.TryGetProperty n with
            | true, v -> v.ValueKind = JsonValueKind.True
            | _ -> false
        let number (e: JsonElement) (n: string) =
            match e.TryGetProperty n with
            | true, v when v.ValueKind = JsonValueKind.Number -> Some v
            | _ -> None
        let guides =
            match root.TryGetProperty "shortlist" with
            | true, list when list.ValueKind = JsonValueKind.Array ->
                [ for item in list.EnumerateArray() ->
                    match str item "hgvs", str item "spacer" with
                    | Some h, Some sp ->
                        mkGuide h (str item "rsId" |> Option.filter (fun r -> r <> "")) (flag item "isComplement") sp None
                            (number item "score" |> Option.map _.GetDouble()) (number item "offtargets" |> Option.map _.GetInt32())
                    | _ -> raise (BadArgs(sprintf "'%s' has a shortlist entry without hgvs or spacer." file)) ]
            | _ -> []
        let construct =
            match root.TryGetProperty "construct" with
            | true, c when c.ValueKind = JsonValueKind.Object ->
                Some(mkConstruct (defaultArg (str c "drId") Construct.LwaId) (flag c "frida") (flag c "bioBricks") (defaultArg (str c "custom5") "") (defaultArg (str c "custom3") ""))
            | _ -> None
        guides, construct
    with :? JsonException -> raise (BadArgs(sprintf "'%s' is not a valid session file." file))

/// The first `top` Mutated rows of each variant and strand of a design report, in report order (engineered guide first).
let private guidesFromDesignReport (path: string) (top: int) : Export.ExportGuide list =
    let lines =
        File.ReadAllLines path
        |> Array.map _.Trim()
        |> Array.filter (fun l -> l <> "" && not (l.StartsWith "#"))
    if lines.Length = 0 then raise (BadArgs(sprintf "'%s' is empty." path))
    let tabs = lines[0] |> Seq.filter ((=) '\t') |> Seq.length
    let commas = lines[0] |> Seq.filter ((=) ',') |> Seq.length
    let split (l: string) = if tabs > commas then List.ofArray (l.Split '\t') else ReportSchema.splitCsvLine l
    let header = split lines[0] |> List.map _.Trim()
    if header.Length < 5 || List.take 3 header <> List.take 3 ReportSchema.columns then
        raise (BadArgs(sprintf "'%s' is neither a session file nor a Diana design report (expected the header '%s')." path ReportSchema.header))
    let strandCol = ReportSchema.columns |> List.findIndex ((=) "Strand")
    let counts = Collections.Generic.Dictionary<string, int>()
    let inv = Globalization.CultureInfo.InvariantCulture
    let cell (f: string list) i = if i < f.Length then f[i].Trim() else ""
    [ for l in lines |> Array.skip 1 do
        let f = split l
        if f.Length >= 5 && cell f 2 = ReportSchema.mutatedType then
            let strand = cell f strandCol
            let key = cell f 1 + "|" + strand
            let seen = match counts.TryGetValue key with | true, n -> n | _ -> 0
            if seen < top then
                counts[key] <- seen + 1
                yield
                    mkGuide (cell f 1) (match cell f 0 with | "" -> None | r -> Some r) (strand = ReportSchema.complementStrand) (cell f 4)
                        (match Int32.TryParse(cell f 3, Globalization.NumberStyles.Integer, inv) with | true, n -> Some n | _ -> None)
                        (match Double.TryParse(cell f 5, Globalization.NumberStyles.Float, inv) with | true, x -> Some x | _ -> None)
                        (match Int32.TryParse(cell f 7, Globalization.NumberStyles.Integer, inv) with | true, n -> Some n | _ -> None) ]

/// Writes the complete guides (what would be ordered) as GenBank or SBOL 3. Reads a session file or a design report; offline.
let exportCommand (args: Args) (ct: CancellationToken) : Task<int> =
    task {
        let file =
            match args.Positional with
            | [ f ] -> f
            | _ -> raise (BadArgs "export takes exactly one file: a session saved by the web app (.json) or a design report (.csv).")
        if not (File.Exists file) then raise (BadArgs(sprintf "Input file '%s' not found." file))
        let format =
            match (defaultArg (opt args "format") "genbank").ToLowerInvariant() with
            | "genbank" | "gb" -> "genbank"
            | "sbol" | "sbol3" -> "sbol"
            | "sbol2" -> "sbol2"
            | f -> raise (BadArgs(sprintf "--format must be genbank, sbol (SBOL 3) or sbol2, got '%s'." f))
        let top = intOpt args "top" 1
        if top < 1 then raise (BadArgs "--top must be 1 or more.")
        let date =
            match opt args "date" with
            | None -> DateTime.UtcNow
            | Some d ->
                match DateTime.TryParseExact(d, "yyyy-MM-dd", Globalization.CultureInfo.InvariantCulture, Globalization.DateTimeStyles.None) with
                | true, v -> v
                | _ -> raise (BadArgs(sprintf "--date must look like 2026-10-19, got '%s'." d))
        let constructFlags =
            [ "dr", args.Options.ContainsKey "dr"
              "frida", args.Flags.Contains "frida"
              "biobricks", args.Flags.Contains "biobricks"
              "custom5", args.Options.ContainsKey "custom5"
              "custom3", args.Options.ContainsKey "custom3" ]
            |> List.filter snd
            |> List.map (fun (n, _) -> "--" + n)

        let text = File.ReadAllText file
        let guides, sessionConstruct =
            if text.TrimStart().StartsWith "{" then
                if args.Options.ContainsKey "top" then raise (BadArgs "--top applies to a design report; a session already holds the chosen guides.")
                if not constructFlags.IsEmpty then
                    raise (BadArgs(sprintf "%s cannot be combined with a session file: the session holds the construct you built in the web app." (String.concat ", " constructFlags)))
                let g, c = guidesFromSession text file
                if g.IsEmpty then raise (BadArgs(sprintf "The shortlist of '%s' is empty; add guides to the shortlist in the web app and save the session again." file))
                g, c
            else
                let g = guidesFromDesignReport file top
                if g.IsEmpty then raise (BadArgs(sprintf "No Mutated guides found in '%s'." file))
                g, None
        let construct =
            match sessionConstruct with
            | Some c -> c
            | None ->
                let dr = (defaultArg (opt args "dr") Construct.LwaId).ToLowerInvariant()
                if not (Construct.drPresets |> List.exists (fun p -> p.Id = dr)) then
                    raise (BadArgs(sprintf "--dr must be %s, got '%s'." (Construct.drPresets |> List.map _.Id |> String.concat " or ") dr))
                let custom name =
                    let v = defaultArg (opt args name) ""
                    if not (Construct.isValidCustom v) then raise (BadArgs(sprintf "--%s may only contain A, C, G, T or U." name))
                    v
                mkConstruct dr (args.Flags.Contains "frida") (args.Flags.Contains "biobricks") (custom "custom5") (custom "custom3")
        let options : Export.ExportOptions =
            { Construct = construct
              IncludeT7 = args.Flags.Contains "t7"
              Namespace = defaultArg (opt args "namespace") Export.DefaultNamespace
              Date = date }
        let body = match format with | "sbol" -> Export.toSbol3 options guides | "sbol2" -> Export.toSbol2 options guides | _ -> Export.toGenBank options guides
        writeOut (opt args "out") body
        eprintfn "exported %d guide%s as %s (%s%s)." guides.Length (if guides.Length = 1 then "" else "s")
            (match format with | "sbol" -> "SBOL 3" | "sbol2" -> "SBOL 2" | _ -> "GenBank") (Construct.describe construct) (if options.IncludeT7 then ", T7 promoter" else "")
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
                | "export" -> return! exportCommand args ct
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

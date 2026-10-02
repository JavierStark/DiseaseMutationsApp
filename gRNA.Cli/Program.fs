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
  grna resolve <rsID> [<rsID> ...]
  grna design --hgvs <HGVS> [--spacer 28] [--seed 10-17] [--complement] [--format csv|json|tsv] [--out file]
  grna design --input <list.txt|session.json> --out <report.csv> [--spacer 28] [--seed 10-17] [--complement] [--concurrency 2]
  grna fold <RNA sequence>
  grna pool --guides <report.csv> [--capacity 5] [--plate 96|384] [--model auto|2df|2dm|3d] [--out plan.csv]
  grna pool --count <V>          [--capacity 5] [--plate 96|384] [--model auto|2df|2dm|3d] [--out plan.csv]

Environment: GRNA_BOWTIE_BINARY, GRNA_BOWTIE_INDEX, GRNA_PYTHON, GRNA_NCBI_API_KEY, GRNA_NCBI_CONTACT
Exit codes: 0 ok, 1 failure, 2 bad input, 3 missing native dependency, 4 upstream failure
A design report in csv format is accepted unchanged by `grna pool --guides` and by the web Pooling tab."""

exception BadArgs of string

/// Minimal option parsing: `--name value` pairs, bare `--flag`s and positional arguments.
type Args =
    { Positional: string list
      Options: Map<string, string>
      Flags: Set<string> }

let private flagNames = set [ "complement"; "network"; "help" ]

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

let resolve (args: Args) (ct: CancellationToken) : Task<int> =
    task {
        if args.Positional.IsEmpty then raise (BadArgs "resolve needs at least one rsID.")
        for rs in args.Positional do
            let id = if rs.StartsWith("rs", StringComparison.OrdinalIgnoreCase) then rs.Substring 2 else rs
            let! hgvs = SNP.getHgvsNotationsAsync id ct
            if hgvs.IsEmpty then eprintfn "%s: no NG_ notations found" rs
            for h in hgvs do printfn "%s\t%s" rs h
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
                       structure = g.RnaFoldResult.Structure |})
            {| hgvs = r.Hgvs
               rsId = r.RsId
               strand = if r.Complement then "Complement" else "Normal"
               mutated = cands r.Result.gRNA
               original = cands r.Result.originalGRNA |})
    JsonSerializer.Serialize(items, opts)

let private csvOf (rows: DesignRow list) (spacer, seedS, seedE) (includeHeader: bool) =
    let sb = StringBuilder()
    if includeHeader then
        sb.AppendLine(ReportSchema.provenance spacer seedS seedE) |> ignore
        sb.AppendLine ReportSchema.header |> ignore
    for r in rows do
        for g in r.Result.gRNA do
            sb.AppendLine(ReportSchema.row r.RsId r.Hgvs ReportSchema.mutatedType r.Complement g) |> ignore
        for g in r.Result.originalGRNA do
            sb.AppendLine(ReportSchema.row r.RsId r.Hgvs ReportSchema.originalType r.Complement g) |> ignore
    sb.ToString()

let private tsvOf (rows: DesignRow list) =
    (csvOf rows (0, 0, 0) false).Split([| '\n' |], StringSplitOptions.RemoveEmptyEntries)
    |> Array.map (fun l -> l.TrimEnd('\r').Replace(',', '\t'))
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
        if spacer <= 0 || seedS < 0 || seedE >= spacer || seedS > seedE then
            raise (BadArgs "Seed must satisfy 0 <= start <= end < spacer.")
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

        // Resumable batch: with an existing csv report, variants already present are skipped and rows appended.
        let isBatchCsv = opt args "input" |> Option.isSome && format = "csv" && out.IsSome
        let alreadyDone =
            if isBatchCsv && File.Exists out.Value then
                File.ReadAllLines out.Value
                |> Array.filter (fun l -> not (l.StartsWith "#"))
                |> Array.skip 1
                |> Array.choose (fun l -> let f = l.Split ',' in if f.Length > 1 then Some(f[0] + "|" + f[1]) else None)
                |> Set.ofArray
            else
                Set.empty

        let bowtie = Services.BowtieService()
        let results = System.Collections.Concurrent.ConcurrentBag<int * DesignRow>()
        let mutable failures = 0
        let mutable lastError: exn option = None
        let gate = obj ()

        // Expand rsIDs to HGVS notations (sequentially: pure HTTP, rate limited by NCBI anyway).
        let jobs = ResizeArray<int * string option * string>()
        let mutable idx = 0
        for input in inputs do
            if input.StartsWith("rs", StringComparison.OrdinalIgnoreCase) && Char.IsDigit(input[2]) then
                let! hgvs = SNP.getHgvsNotationsAsync (input.Substring 2) ct
                if hgvs.IsEmpty then eprintfn "%s: no NG_ notations found" input
                for h in hgvs do
                    jobs.Add((idx, Some(input.Substring 2), h))
                    idx <- idx + 1
            else
                jobs.Add((idx, None, input))
                idx <- idx + 1

        let work =
            jobs
            |> Seq.filter (fun (_, rs, h) -> not (alreadyDone.Contains((defaultArg rs "") + "|" + h)))
            |> List.ofSeq

        let sem = new SemaphoreSlim(concurrency)
        let tasks =
            work
            |> List.map (fun (i, rs, h) ->
                task {
                    do! sem.WaitAsync ct
                    try
                        try
                            let! r = Main.getBestgRNAFromHGVS h spacer seedS seedE bowtie ct complement
                            results.Add((i, { Hgvs = h; RsId = rs; Complement = complement; Result = r }))
                            eprintfn "ok   %s" h
                        with
                        | :? OperationCanceledException as oce -> raise oce
                        | ex ->
                            lock gate (fun () ->
                                failures <- failures + 1
                                lastError <- Some ex)
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
            | "tsv" -> tsvOf rows
            | _ -> csvOf rows (spacer, seedS, seedE) (not isBatchCsv || not (File.Exists out.Value) || alreadyDone.IsEmpty)

        if isBatchCsv && File.Exists out.Value && not alreadyDone.IsEmpty then
            File.AppendAllText(out.Value, body, UTF8Encoding(false))
        else
            writeOut out body

        if failures > 0 && rows.IsEmpty then
            match lastError with
            | Some (:? GrnaDependencyException) -> return ExitDependency
            | Some (:? GrnaInputException) -> return ExitBadInput
            | Some (:? GrnaUpstreamException) -> return ExitUpstream
            | _ -> return ExitFailure
        else
            return (if failures > 0 then ExitFailure else ExitOk)
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
            let strandCol = ReportSchema.columns |> List.findIndex ((=) "Strand")
            let best = Collections.Generic.Dictionary<string, int * string>()
            let strand = Collections.Generic.Dictionary<string, string>()
            let order = ResizeArray<string>()
            for l in lines |> Array.skip 1 do
                let f = l.Split delim
                if f.Length >= 5 && f[2].Trim() = ReportSchema.mutatedType then
                    let h = f[1].Trim()
                    let s = if f.Length > strandCol then f[strandCol].Trim() else ""
                    let first = if strand.ContainsKey h then strand[h] = s else (strand[h] <- s; true)
                    if first then
                        let rank = match Int32.TryParse(f[3].Trim()) with | true, n -> n | _ -> Int32.MaxValue
                        match best.TryGetValue h with
                        | true, (r, _) when r <= rank -> ()
                        | true, _ -> best[h] <- (rank, f[4].Trim())
                        | _ ->
                            best[h] <- (rank, f[4].Trim())
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
                | true, n when n > 0 -> [ for i in 1..n -> sprintf "Guide %d" i ]
                | _ -> raise (BadArgs "--count must be a positive integer.")
            | _ -> raise (BadArgs "pool needs exactly one of --guides or --count.")

        let v = labels.Length
        if v = 0 then raise (BadArgs "No guides found.")

        let model =
            match (defaultArg (opt args "model") "auto").ToLowerInvariant() with
            | "auto" -> Pooling.bestModel v capacity
            | "2df" -> Pooling.TwoDFragmented
            | "2dm" -> Pooling.TwoDMatrix
            | "3d" -> Pooling.ThreeD
            | m -> raise (BadArgs(sprintf "--model must be auto, 2df, 2dm or 3d, got '%s'." m))

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
                let args = parseArgs rest
                match cmd with
                | "doctor" -> return! doctor args ct
                | "resolve" -> return! resolve args ct
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

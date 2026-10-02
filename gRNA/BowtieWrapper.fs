module gRNA.BowtieWrapper

open System
open System.Diagnostics
open System.IO
open System.Threading
open System.Threading.Tasks
open gRNA.Parsing

// Bowtie 2 indexes use .bt2/.bt2l, Bowtie 1 indexes use .ebwt/.ebwtl.
let private indexSuffixes =
    [| ".rev.2.bt2l"; ".rev.1.bt2l"; ".4.bt2l"; ".3.bt2l"; ".2.bt2l"; ".1.bt2l"
       ".rev.2.bt2"; ".rev.1.bt2"; ".4.bt2"; ".3.bt2"; ".2.bt2"; ".1.bt2"
       ".rev.2.ebwtl"; ".rev.1.ebwtl"; ".4.ebwtl"; ".3.ebwtl"; ".2.ebwtl"; ".1.ebwtl"
       ".rev.2.ebwt"; ".rev.1.ebwt"; ".4.ebwt"; ".3.ebwt"; ".2.ebwt"; ".1.ebwt" |]

let tryStripIndexSuffix (fileName: string) =
    indexSuffixes
    |> Array.tryFind (fun suffix -> fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
    |> Option.map (fun suffix -> fileName.Substring(0, fileName.Length - suffix.Length))

/// Deterministically resolves the index base: explicit setting wins, otherwise the first
/// (sorted) index file found under the index directory.
let resolveBowtieIndexBase (env: GrnaEnvironment) : string =
    match env.BowtieIndexBase with
    | Some b -> b
    | None ->
        let dir = GrnaEnvironment.resolvePath env.BowtieIndexDirectory
        if not (Directory.Exists dir) then
            raise (GrnaDependencyException(sprintf "Bowtie index directory '%s' not found. Expected .bt2/.bt2l/.ebwt index files." dir))
        let bases =
            Directory.GetFiles(dir, "*", SearchOption.AllDirectories)
            |> Array.sort
            |> Array.choose tryStripIndexSuffix
        match Array.tryHead bases with
        | Some b -> b
        | None -> raise (GrnaDependencyException(sprintf "No Bowtie index files found in '%s'." dir))

// Retryable memo: failures are not cached.
let private memoLock = obj ()
let mutable private memo: (string * string) option = None

let private indexBaseFor (env: GrnaEnvironment) =
    lock memoLock (fun () ->
        let key = defaultArg env.BowtieIndexBase env.BowtieIndexDirectory
        match memo with
        | Some (k, v) when k = key -> v
        | _ ->
            let v = resolveBowtieIndexBase env
            memo <- Some (key, v)
            v)

/// Runs Bowtie for the given reads and returns the parsed alignments.
let runBowtieWith (env: GrnaEnvironment) (mismatches: int) (threads: int) (sequences: string list) (cancellationToken: CancellationToken) : Task<BowtieAlignment list> = task {
    let binary = GrnaEnvironment.resolvePath env.BowtieBinary
    let indexBase = indexBaseFor env
    let startInfo = ProcessStartInfo()
    startInfo.FileName <- binary
    for a in [ "-x"; indexBase; "-c"; String.concat "," sequences; "-v"; string mismatches; "-k"; "6"; "--threads"; string threads; "--mm" ] do
        startInfo.ArgumentList.Add a
    startInfo.RedirectStandardOutput <- true
    startInfo.RedirectStandardError <- true
    startInfo.UseShellExecute <- false
    startInfo.CreateNoWindow <- true

    GrnaLog.log (sprintf "Running Bowtie: %s (%d reads)" binary sequences.Length)

    use proc = new Process()
    proc.StartInfo <- startInfo
    use cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
    cts.CancelAfter env.ProcessTimeout
    // Registered before Start so a token cancelled in that window still kills the process.
    use _ = cts.Token.Register(fun () ->
        try if not proc.HasExited then proc.Kill(entireProcessTree = true) with _ -> ())

    try
        try
            proc.Start() |> ignore
        with ex ->
            raise (GrnaDependencyException(sprintf "Cannot start Bowtie at '%s': %s" binary ex.Message, ex))
        if cts.Token.IsCancellationRequested then
            try proc.Kill(entireProcessTree = true) with _ -> ()

        let stdoutTask = proc.StandardOutput.ReadToEndAsync()
        let stderrTask = proc.StandardError.ReadToEndAsync()
        try
            do! proc.WaitForExitAsync(cts.Token)
        with :? OperationCanceledException when not cancellationToken.IsCancellationRequested ->
            raise (GrnaUpstreamException(sprintf "Bowtie timed out after %O." env.ProcessTimeout))
        let! stdout = stdoutTask
        let! stderr = stderrTask

        if proc.ExitCode = 137 then
            raise (GrnaDependencyException "Bowtie was killed (likely out of memory). Process fewer sequences at once or increase memory.")
        elif proc.ExitCode <> 0 then
            raise (GrnaDependencyException(sprintf "Bowtie exited with code %d. Error: %s" proc.ExitCode stderr))

        return parseBowtieAlignments stdout
    finally
        try if not proc.HasExited then proc.Kill(entireProcessTree = true) with _ -> ()
}

let runBowtieForMultipleSequencesWith (env: GrnaEnvironment) (sequences: string list) (mismatches: int) (threads: int) (cancellationToken: CancellationToken) : Task<int list> = task {
    let! alignments = runBowtieWith env mismatches threads sequences cancellationToken
    return countAlignments sequences.Length alignments
}

let runBowtieForMultipleSequences (sequences: string list) (mismatches: int) (threads: int) (cancellationToken: CancellationToken) : Task<int list> =
    runBowtieForMultipleSequencesWith (GrnaEnvironment.getCurrent ()) sequences mismatches threads cancellationToken

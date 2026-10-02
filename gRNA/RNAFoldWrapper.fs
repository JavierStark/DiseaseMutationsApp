module gRNA.RNAFoldWrapper

open System
open System.Diagnostics
open System.Threading
open System.Threading.Tasks

type RNAFoldResult = {
    Structure: string
    Energy: float
}

/// Bounds concurrent Python interpreters across the whole process.
let private foldSemaphore = new SemaphoreSlim(2, 2)

// The script prints `structure<TAB>energy` itself, so the parser owns the format end to end
// instead of scraping the repr of whatever the installed ViennaRNA binding returns.
let private script =
    "import sys, RNA\nfor s in sys.argv[1:]:\n    st, e = RNA.fold(s)\n    print(st + chr(9) + repr(float(e)))"

let private runPython (env: GrnaEnvironment) (sequences: string list) (cancellationToken: CancellationToken) : Task<string> = task {
    do! foldSemaphore.WaitAsync(cancellationToken)
    try
        let startInfo = ProcessStartInfo()
        startInfo.FileName <- env.PythonExecutable
        startInfo.ArgumentList.Add "-c"
        startInfo.ArgumentList.Add script
        for s in sequences do startInfo.ArgumentList.Add s
        startInfo.RedirectStandardOutput <- true
        startInfo.RedirectStandardError <- true
        startInfo.UseShellExecute <- false
        startInfo.CreateNoWindow <- true

        use proc = new Process()
        proc.StartInfo <- startInfo
        use cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
        cts.CancelAfter env.ProcessTimeout

        try
            proc.Start() |> ignore
        with ex ->
            raise (GrnaDependencyException(sprintf "Cannot start '%s' (is Python installed?): %s" env.PythonExecutable ex.Message, ex))

        use _ = cts.Token.Register(fun () -> try if not proc.HasExited then proc.Kill(entireProcessTree = true) with _ -> ())
        let stdoutTask = proc.StandardOutput.ReadToEndAsync()
        let stderrTask = proc.StandardError.ReadToEndAsync()
        try
            do! proc.WaitForExitAsync(cts.Token)
        with :? OperationCanceledException when not cancellationToken.IsCancellationRequested ->
            raise (GrnaUpstreamException(sprintf "ViennaRNA fold timed out after %O." env.ProcessTimeout))
        let! stdout = stdoutTask
        let! stderr = stderrTask
        if proc.ExitCode <> 0 then
            raise (GrnaDependencyException(sprintf "Python/ViennaRNA failed with exit code %d: %s" proc.ExitCode stderr))
        return stdout
    finally
        foldSemaphore.Release() |> ignore
}

/// Folds many RNA sequences in a single Python call. Results are in input order.
let foldManyWith (env: GrnaEnvironment) (sequences: string list) (cancellationToken: CancellationToken) : Task<RNAFoldResult list> = task {
    if sequences.IsEmpty then
        return []
    else
        // Chunk to stay under OS argument-length limits.
        let chunks = sequences |> List.chunkBySize 200
        let results = ResizeArray<RNAFoldResult>()
        for chunk in chunks do
            let! stdout = runPython env chunk cancellationToken
            let parsed = Parsing.parseFoldBatch stdout
            if parsed.Length <> chunk.Length then
                raise (GrnaUpstreamException(sprintf "ViennaRNA returned %d results for %d sequences." parsed.Length chunk.Length))
            for (s, e) in parsed do results.Add { Structure = s; Energy = e }
        return List.ofSeq results
}

let foldMany (sequences: string list) (cancellationToken: CancellationToken) : Task<RNAFoldResult list> =
    foldManyWith (GrnaEnvironment.getCurrent ()) sequences cancellationToken

/// Folds one RNA sequence: dot-bracket structure and minimum free energy.
let fold (sequence: string) (cancellationToken: CancellationToken) : Task<RNAFoldResult> = task {
    let! r = foldMany [ sequence ] cancellationToken
    return List.head r
}

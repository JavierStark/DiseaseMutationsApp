/// Preflight checks shared by `grna doctor`, the /healthz endpoint and the in-app diagnostics panel.
module gRNA.Diagnostics

open System
open System.Diagnostics
open System.IO
open System.Net.Http
open System.Threading
open System.Threading.Tasks

type CheckStatus =
    | Pass
    | Warn
    | Fail

type CheckResult =
    { Name: string
      Status: CheckStatus
      Detail: string
      /// What to do when the check does not pass; empty when it passes.
      Remedy: string }

let private mk name status detail remedy =
    { Name = name; Status = status; Detail = detail; Remedy = remedy }

/// Runs a short child process and returns (exit code, stdout, stderr), or None if it could not start.
let private tryRun (fileName: string) (args: string list) (timeout: TimeSpan) : Task<(int * string * string) option> =
    task {
        try
            let psi = ProcessStartInfo()
            psi.FileName <- fileName
            for a in args do psi.ArgumentList.Add a
            psi.RedirectStandardOutput <- true
            psi.RedirectStandardError <- true
            psi.UseShellExecute <- false
            psi.CreateNoWindow <- true
            use proc = new Process()
            proc.StartInfo <- psi
            proc.Start() |> ignore
            use cts = new CancellationTokenSource(timeout)
            let outTask = proc.StandardOutput.ReadToEndAsync()
            let errTask = proc.StandardError.ReadToEndAsync()
            let! _ =
                task {
                    try
                        do! proc.WaitForExitAsync(cts.Token)
                    with :? OperationCanceledException ->
                        proc.Kill(entireProcessTree = true)
                }
            let! o = outTask
            let! e = errTask
            return Some(proc.ExitCode, o, e)
        with _ ->
            return None
    }

let checkBowtieBinary (env: GrnaEnvironment) : Task<CheckResult> =
    task {
        let path = GrnaEnvironment.resolvePath env.BowtieBinary
        if not (File.Exists path) then
            return mk "Bowtie binary" Fail (sprintf "Not found at %s" path) "Set GRNA_BOWTIE_BINARY or place bowtie-align-s in ./bowtie/."
        else
            let! r = tryRun path [ "--version" ] (TimeSpan.FromSeconds 10.0)
            match r with
            | Some (_, out, _) ->
                let firstLine = (out.Split('\n') |> Array.tryHead |> Option.defaultValue "").Trim()
                return mk "Bowtie binary" Pass (sprintf "%s (%s)" path firstLine) ""
            | None ->
                return mk "Bowtie binary" Fail (sprintf "%s exists but cannot be executed" path) "Make it executable (chmod +x) and check it matches this OS/architecture."
    }

let checkBowtieIndex (env: GrnaEnvironment) : CheckResult =
    try
        let b = BowtieWrapper.resolveBowtieIndexBase env
        let dir = Path.GetDirectoryName b
        let kind =
            if Directory.Exists dir && Directory.GetFiles(dir, "*.ebwt*").Length > 0 then "Bowtie 1 (.ebwt)"
            else "Bowtie 2 (.bt2)"
        mk "Bowtie index" Pass (sprintf "%s [%s]" b kind) ""
    with ex ->
        mk "Bowtie index" Fail ex.Message "Rebuild the image (./start.sh --rebuild-bowtie) or set GRNA_BOWTIE_INDEX to an index base path."

let checkViennaRna (env: GrnaEnvironment) : Task<CheckResult> =
    task {
        let! r = tryRun env.PythonExecutable [ "-c"; "import RNA; print(RNA.__version__)" ] (TimeSpan.FromSeconds 20.0)
        match r with
        | None ->
            return mk "Python" Fail (sprintf "Cannot start '%s'" env.PythonExecutable) "Install Python 3 or set GRNA_PYTHON."
        | Some (0, out, _) ->
            return mk "ViennaRNA" Pass (sprintf "RNA %s via %s" (out.Trim()) env.PythonExecutable) ""
        | Some (_, _, err) ->
            return mk "ViennaRNA" Fail (sprintf "import RNA failed: %s" (err.Trim())) "pip install viennarna (pinned version in the Dockerfile)."
    }

let checkNcbi () : Task<CheckResult> =
    task {
        try
            use http = new HttpClient(Timeout = TimeSpan.FromSeconds 8.0)
            use req = new HttpRequestMessage(HttpMethod.Head, "https://eutils.ncbi.nlm.nih.gov/entrez/eutils/einfo.fcgi")
            let! resp = http.SendAsync req
            return
                if int resp.StatusCode < 500 then mk "NCBI reachability" Pass (sprintf "HTTP %d" (int resp.StatusCode)) ""
                else mk "NCBI reachability" Warn (sprintf "HTTP %d" (int resp.StatusCode)) "NCBI may be down; retry later."
        with ex ->
            return mk "NCBI reachability" Warn ex.Message "Check outbound HTTPS access to eutils.ncbi.nlm.nih.gov."
    }

let checkMemory (env: GrnaEnvironment) : CheckResult =
    let info = GC.GetGCMemoryInfo()
    let totalGb = float info.TotalAvailableMemoryBytes / 1073741824.0
    if totalGb < 4.0 then
        mk "Memory" Warn (sprintf "%.1f GB available to the process" totalGb) "The GRCh38 index is memory-mapped (~4 GB); give the container at least 4 GB for good performance."
    else
        mk "Memory" Pass (sprintf "%.1f GB available to the process" totalGb) ""

let checkWorkingDirectory () : CheckResult =
    mk "Working directory" Pass (sprintf "cwd=%s base=%s" Environment.CurrentDirectory AppContext.BaseDirectory) ""

/// Runs every check. `includeNetwork=false` skips NCBI (used by the container HEALTHCHECK).
let runAll (env: GrnaEnvironment) (includeNetwork: bool) : Task<CheckResult list> =
    task {
        let! bowtie = checkBowtieBinary env
        let index = checkBowtieIndex env
        let! rna = checkViennaRna env
        let! ncbi =
            if includeNetwork then
                task {
                    let! c = checkNcbi ()
                    return [ c ]
                }
            else
                Task.FromResult []
        return [ checkWorkingDirectory (); bowtie; index; rna ] @ ncbi @ [ checkMemory env ]
    }

/// True when no check failed (warnings are tolerated).
let isHealthy (results: CheckResult list) =
    results |> List.forall (fun r -> r.Status <> Fail)

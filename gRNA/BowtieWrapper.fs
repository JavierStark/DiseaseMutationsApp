module gRNA.BowtieWrapper

open System
open System.Diagnostics
open System.IO
open System.Threading
open System.Threading.Tasks

//example output:
// 0	+	chr7	147119362	ACTGACTGACTG	IIIIIIIIIIII	478	
// 0	+	chr9	37515365	ACTGACTGACTG	IIIIIIIIIIII	478	

let private indexSuffixes =
    [| ".rev.2.bt2"; ".rev.1.bt2"; ".4.bt2"; ".3.bt2"; ".2.bt2"; ".1.bt2";
       ".rev.2.bt2l"; ".rev.1.bt2l"; ".4.bt2l"; ".3.bt2l"; ".2.bt2l"; ".1.bt2l" |]

let private tryStripIndexSuffix (fileName: string) =
    indexSuffixes
    |> Array.tryFind (fun suffix -> fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
    |> Option.map (fun suffix -> fileName.Substring(0, fileName.Length - suffix.Length))

let private resolveBowtieIndexBase () =
    let indexDirectory = "bowtie/indexes"

    if not (Directory.Exists(indexDirectory)) then
        failwith "No Bowtie index base found in bowtie/indexes. Expected .bt2 or .bt2l index files."

    let firstIndexFile =
        Directory.GetFiles(indexDirectory, "*.bt2*", SearchOption.AllDirectories)
        |> Array.tryHead

    match firstIndexFile with
    | Some filePath ->
        match tryStripIndexSuffix filePath with
        | Some indexBase -> indexBase
        | None -> failwith "Invalid Bowtie index file name in bowtie/indexes. Expected .bt2 or .bt2l index files."
    | None ->
        failwith "No Bowtie index base found in bowtie/indexes. Expected .bt2 or .bt2l index files."

let private cachedBowtieIndexBase = lazy (resolveBowtieIndexBase())

// ./bowtie-align-s -x GCA_000001405.15_GRCh38_no_alt_analysis_set -c SEQUENCE -v MISMATCHES -k 2
let runBowtie(mismatches: int) (threads: int) (sequence: string) (cancellationToken: CancellationToken) : Task<string array> = task {
    let startInfo = ProcessStartInfo()
    let maxAllignment = 6
    let indexBase = cachedBowtieIndexBase.Value
    startInfo.FileName <- "bowtie/bowtie-align-s"
    startInfo.Arguments <- sprintf "-x \"%s\" -c %s -v %d -k %d --threads %d --mm" indexBase sequence mismatches maxAllignment threads
    startInfo.RedirectStandardOutput <- true
    startInfo.RedirectStandardError <- true
    startInfo.UseShellExecute <- false
    startInfo.CreateNoWindow <- true
    
    printf "Working Directory: %s\n" Environment.CurrentDirectory
    printf "Running Bowtie with command: %s %s" startInfo.FileName startInfo.Arguments

    use proc = new Process()
    proc.StartInfo <- startInfo
    
    try
        proc.Start() |> ignore

        use _ = cancellationToken.Register(fun () ->
            try if not proc.HasExited then proc.Kill(entireProcessTree = true) with | _ -> ()
        )

        let stdoutTask = proc.StandardOutput.ReadToEndAsync(cancellationToken)
        let stderrTask = proc.StandardError.ReadToEndAsync(cancellationToken)

        do! proc.WaitForExitAsync(cancellationToken)

        let! stdout = stdoutTask
        let! stderr = stderrTask

        let combinedOutput =
            if String.IsNullOrWhiteSpace(stdout) && String.IsNullOrWhiteSpace(stderr) then
                sprintf "Bowtie exited with code %d but produced no output." proc.ExitCode
            else
                stdout + "\n" + stderr
                
        //0	+	chr7	147119362	ACTGACTGACTG	IIIIIIIIIIII	478
        // 0	+	chr9	37515365	ACTGACTGACTG	IIIIIIIIIIII	478//
        //
        // # reads processed: 1
        // # reads with at least one alignment: 1 (100.00%)
        // # reads that failed to align: 0 (0.00%)
        // Reported 2 alignments

        let allignments =
            combinedOutput.Split([|'\n'|])
            |> Array.takeWhile (fun line -> not (String.IsNullOrEmpty(line)))
            |> Array.map (_.Trim())
            
        printfn "Bowtie finished with exit code %d" proc.ExitCode
        
        // Check for memory-related exit codes
        if proc.ExitCode = 137 then
            failwith "Bowtie process was killed due to out of memory (OOM). Try processing fewer sequences at once or increase system memory."
        elif proc.ExitCode <> 0 then
            printfn "Bowtie stderr: %s" stderr
            failwith (sprintf "Bowtie exited with code %d. Error: %s" proc.ExitCode stderr)

        return allignments
    finally
        try
            proc.Kill(entireProcessTree = true)
        with
        | :? System.InvalidOperationException -> ()  // Process already exited - normal on Linux
        | ex -> printfn "Cleanup error: %s" ex.Message
        // Force garbage collection to clean up resources
        GC.Collect()
        GC.WaitForPendingFinalizers()
           
}

let runBowtieForMultipleSequences (sequences: string list) (mismatches: int) (threads: int) (cancellationToken: CancellationToken) : Task<int list> = task {
    let! results =
        sequences
        |> String.concat ","
        |> fun seq -> runBowtie mismatches threads seq cancellationToken
        
    
    let nOfAllignments =
        results
        |> Array.map(fun r -> r.Split '\t' |> Array.head |> int)
        |> Array.groupBy id
        |> Array.map(fun (key, group) -> (key, group.Length))
        |> dict
        |> fun dict ->
            sequences
            |> List.mapi(fun i _ ->
                match dict.TryGetValue(i) with
                | true, count -> count
                | _ -> 0)
            
    return nOfAllignments
}
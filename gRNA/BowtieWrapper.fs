module gRNA.BowtieWrapper

open System
open System.Diagnostics
open System.Threading.Tasks

//example output:
// 0	+	chr7	147119362	ACTGACTGACTG	IIIIIIIIIIII	478	
// 0	+	chr9	37515365	ACTGACTGACTG	IIIIIIIIIIII	478	

// ./bowtie-align-s -x GCA_000001405.15_GRCh38_no_alt_analysis_set -c SEQUENCE -v MISMATCHES -k 2
let runBowtie(mismatches: int) (threads: int)  (sequence: string) : Task<string array> = task {
    let startInfo = ProcessStartInfo()
    let maxAllignment = 6
    startInfo.FileName <- "bowtie/bowtie-align-s"
    startInfo.Arguments <- sprintf "-x \"GCA_000001405.15_GRCh38_no_alt_analysis_set\" -c %s -v %d -k %d --threads %d" sequence mismatches maxAllignment threads
    startInfo.RedirectStandardOutput <- true
    startInfo.RedirectStandardError <- true
    startInfo.UseShellExecute <- false
    startInfo.CreateNoWindow <- true
    
    printf "Working Directory: %s\n" Environment.CurrentDirectory
    printf "Running Bowtie with command: %s %s" startInfo.FileName startInfo.Arguments

    use proc = new Process()
    proc.StartInfo <- startInfo
    proc.Start() |> ignore

    let! stdout = proc.StandardOutput.ReadToEndAsync()
    let! stderr = proc.StandardError.ReadToEndAsync()

    do! proc.WaitForExitAsync()

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

    return allignments
           
}

let runBowtieForMultipleSequences (sequences: string list) (mismatches: int) (threads: int) : Async<int list> = async {
    let! results =
        sequences
        |> String.concat ","
        |> runBowtie mismatches threads
        |> Async.AwaitTask
        
    
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
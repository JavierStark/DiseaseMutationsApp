module gRNA.BowtieWrapper

open System
open System.IO
open System.Diagnostics
open System.Runtime.InteropServices
open System.Threading
open System.Threading.Tasks

//example output:
// 0	+	chr7	147119362	ACTGACTGACTG	IIIIIIIIIIII	478	
// 0	+	chr9	37515365	ACTGACTGACTG	IIIIIIIIIIII	478	

// ./bowtie-align-s -x grch38_1kgmaj -c SEQUENCE -v MISMATCHES -k 2
let runBowtie (sequence: string) (mismatches: int) (threads: int): Task<string array> = task {
    printfn "Running bowtie for sequence: %s with up to %d mismatches on %d threads" sequence mismatches threads
    let startInfo = ProcessStartInfo()
    startInfo.FileName <- "./bowtie/bowtie-align-s.exe"
    startInfo.Arguments <- sprintf "-x grch38_1kgmaj -c %s -v %d -k 2 --threads %d" sequence mismatches threads
    startInfo.RedirectStandardOutput <- true
    startInfo.RedirectStandardError <- true
    startInfo.UseShellExecute <- false
    startInfo.CreateNoWindow <- true

    use proc = new Process()
    proc.StartInfo <- startInfo
    proc.Start() |> ignore

      // Read both stdout (alignments) and stderr (stats/messages) concurrently
    let! stdout = proc.StandardOutput.ReadToEndAsync()
    let! stderr = proc.StandardError.ReadToEndAsync()

    do! proc.WaitForExitAsync()

    // Combine outputs so you always get useful info
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
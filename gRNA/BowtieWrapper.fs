module gRNA.BowtieWrapper

open System
open System.IO
open System.Diagnostics
open System.Runtime.InteropServices
open System.Threading
open System.Threading.Tasks

// ./bowtie-align-s -x grch38_1kgmaj -c SEQUENCE -v MISMATCHES -k 2
let runBowtie (sequence: string) (mismatches: int) (threads: int): Task<string> = task {
    let startInfo = ProcessStartInfo()
    startInfo.FileName <- "./bowtie-align-s.exe"
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

    return combinedOutput
}
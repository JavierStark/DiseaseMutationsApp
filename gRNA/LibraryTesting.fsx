#r "bin/Debug/net8.0/gRNA.dll"
open System.IO
open gRNA.BowtieWrapper

Directory.SetCurrentDirectory(__SOURCE_DIRECTORY__ + "/bowtieFiles")

runBowtie "ACTGACTGACTGACTGATCGTAGCTAGTCAGC" 2 1
|> Async.AwaitTask
|> Async.RunSynchronously
|> Array.iter (printfn "%s")
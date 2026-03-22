namespace gRNA.Services

open System.Threading
open System.Threading.Tasks
open gRNA.BowtieWrapper

type BowtieService() =
    static let semaphore = new SemaphoreSlim(1, 1)

    member this.ProcessMultipleSequencesAsync(sequences: string list, mismatches: int, threads: int, cancellationToken: CancellationToken) =
        task {
            do! semaphore.WaitAsync(cancellationToken)
            try
                return! runBowtieForMultipleSequences sequences mismatches threads cancellationToken
            finally
                semaphore.Release() |> ignore
        }

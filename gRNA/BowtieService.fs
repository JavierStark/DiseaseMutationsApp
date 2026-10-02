namespace gRNA.Services

open System.Threading
open System.Threading.Tasks
open gRNA.BowtieWrapper

/// Abstraction so the pipeline can be tested or pointed at another aligner.
type IBowtieRunner =
    abstract ProcessMultipleSequencesAsync: sequences: string list * mismatches: int * threads: int * cancellationToken: CancellationToken -> Task<int list>

/// Single-slot (per instance) Bowtie runner; the instance is a DI singleton.
type BowtieService() =
    let semaphore = new SemaphoreSlim(1, 1)

    member this.ProcessMultipleSequencesAsync(sequences: string list, mismatches: int, threads: int, cancellationToken: CancellationToken) =
        task {
            do! semaphore.WaitAsync(cancellationToken)
            try
                return! runBowtieForMultipleSequences sequences mismatches threads cancellationToken
            finally
                semaphore.Release() |> ignore
        }

    /// Alignments (with loci) of one DNA window, up to `maxHits`. Same single-slot semaphore as the batch path.
    member this.FindOffTargetsAsync(window: string, mismatches: int, maxHits: int, cancellationToken: CancellationToken) =
        task {
            do! semaphore.WaitAsync(cancellationToken)
            try
                return! runBowtieK (gRNA.GrnaEnvironment.getCurrent ()) maxHits mismatches 2 [ window ] cancellationToken
            finally
                semaphore.Release() |> ignore
        }

    interface IBowtieRunner with
        member this.ProcessMultipleSequencesAsync(s, m, t, c) = this.ProcessMultipleSequencesAsync(s, m, t, c)

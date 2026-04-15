module Main

open gRNA
open System.Threading.Tasks

type ResultFromHGVS = {
    gRNA: SpacerFinder.gRNAResult list
    mutatedSequence: string
    originalSequence: string
    extraNucleotids: int
}

let private calculateMutationSpanInMutatedLocal (original: string) (mutated: string) : int * int =
    let minLength = min original.Length mutated.Length

    let mutable prefix = 0
    while prefix < minLength && original.[prefix] = mutated.[prefix] do
        prefix <- prefix + 1

    let mutable suffix = 0
    let remainingOriginal = original.Length - prefix
    let remainingMutated = mutated.Length - prefix
    let maxSuffix = min remainingOriginal remainingMutated

    while suffix < maxSuffix &&
          original.[original.Length - 1 - suffix] = mutated.[mutated.Length - 1 - suffix] do
        suffix <- suffix + 1

    let mutationLengthInMutated = mutated.Length - prefix - suffix
    (prefix, max 0 mutationLengthInMutated)

let getBestgRNAFromHGVS (hgvsString: string) (grnaSize: int) (bowtieService: gRNA.Services.BowtieService) (cancellationToken: System.Threading.CancellationToken) = task {
    let hgvsObj = HGVS.HGVS(hgvsString)
    let! sequence = SequenceRepository.SequenceRepository.GetSequence(hgvsObj.Accession)
    let extraNucleotids = grnaSize - hgvsObj.GetMutationLength()

    let mutated, original = sequence.GetMutatedSubsequence(hgvsObj, extraNucleotids, extraNucleotids)
    let mutationStartInMutated, mutationLengthInMutated = calculateMutationSpanInMutatedLocal original mutated

    let! bestgRna =
        SpacerFinder.getOrderedgRna
            grnaSize
            mutated
            mutationStartInMutated
            mutationLengthInMutated
            bowtieService
            cancellationToken

    return  {
        gRNA = bestgRna
        mutatedSequence = mutated
        originalSequence = original
        extraNucleotids = extraNucleotids
    }
}


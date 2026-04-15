module Main

open gRNA
open System.Threading.Tasks

type ResultFromHGVS = {
    gRNA: SpacerFinder.gRNAResult list
    mutatedSequence: string
    originalSequence: string
    extraNucleotids: int
}

let private calculateMutationSpanInMutated (extraNucleotids: int) (sequenceLength: int) (hgvs: HGVS.HGVS) (mutatedLength: int) : int * int =
    let leftContext = max 0 (min extraNucleotids ((fst hgvs.Position) - 1))
    let rightContext = max 0 (min extraNucleotids (sequenceLength - (snd hgvs.Position)))
    let mutationLengthInMutated = max 0 (mutatedLength - leftContext - rightContext)
    (leftContext, mutationLengthInMutated)

let getBestgRNAFromHGVS (hgvsString: string) (grnaSize: int) (bowtieService: gRNA.Services.BowtieService) (cancellationToken: System.Threading.CancellationToken) = task {
    let hgvsObj = HGVS.HGVS(hgvsString)
    let! sequence = SequenceRepository.SequenceRepository.GetSequence(hgvsObj.Accession)
    let extraNucleotids = grnaSize - hgvsObj.GetMutationLength()

    let mutated, original = sequence.GetMutatedSubsequence(hgvsObj, extraNucleotids, extraNucleotids)
    
    let mutationStartInMutated, mutationLengthInMutated =
        calculateMutationSpanInMutated extraNucleotids sequence.Data.Length hgvsObj mutated.Length

    let! gRNAs =
        SpacerFinder.getOrderedgRna
            grnaSize
            mutated
            mutationStartInMutated
            mutationLengthInMutated
            bowtieService
            cancellationToken

    return  {
        gRNA = gRNAs
        mutatedSequence = mutated
        originalSequence = original
        extraNucleotids = extraNucleotids
    }
}

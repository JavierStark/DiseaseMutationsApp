module Main

open gRNA
open System.Threading.Tasks

type ResultFromHGVS = {
    gRNA: SpacerFinder.gRNAResult list
    originalGRNA: SpacerFinder.gRNAResult list
    mutatedSequence: string
    originalSequence: string
    extraNucleotids: int
}

let private calculateMutationSpanInMutated (extraNucleotids: int) (sequenceLength: int) (hgvs: HGVS.HGVS) (mutatedLength: int) : int * int =
    let leftContext = max 0 (min extraNucleotids ((fst hgvs.Position) - 1))
    let rightContext = max 0 (min extraNucleotids (sequenceLength - (snd hgvs.Position)))
    let mutationLengthInMutated = max 0 (mutatedLength - leftContext - rightContext)
    (leftContext, mutationLengthInMutated)

let getBestgRNAFromHGVS (hgvsString: string) (grnaSize: int) (bowtieService: gRNA.Services.BowtieService) (cancellationToken: System.Threading.CancellationToken) (complement: bool) = task {
    let hgvsObj = HGVS.HGVS(hgvsString)
    let! sequence = SequenceRepository.SequenceRepository.GetSequence(hgvsObj.Accession)
    let extraNucleotids = grnaSize - hgvsObj.GetMutationLength()

    let mutated, original = sequence.GetMutatedSubsequence(hgvsObj, extraNucleotids, extraNucleotids)

    let finalMutated, finalOriginal =
        if complement then
            Sequence.complementary mutated, Sequence.complementary original
        else
            mutated, original

    let mutationStartInMutated, mutationLengthInMutated =
        calculateMutationSpanInMutated extraNucleotids sequence.Data.Length hgvsObj finalMutated.Length

    let mutationStartInOriginal, mutationLengthInOriginal =
        calculateMutationSpanInMutated extraNucleotids sequence.Data.Length hgvsObj finalOriginal.Length

    let gRNAsTask =
        SpacerFinder.getOrderedgRna
            grnaSize
            finalMutated
            mutationStartInMutated
            mutationLengthInMutated
            bowtieService
            cancellationToken

    let originalGRNAsTask =
        SpacerFinder.getOrderedgRna
            grnaSize
            finalOriginal
            mutationStartInOriginal
            mutationLengthInOriginal
            bowtieService
            cancellationToken

    let! gRNAs = gRNAsTask
    let! originalGRNAs = originalGRNAsTask

    let gRNAs, originalGRNAs =
        if hgvsObj.Mutation = HGVS.MutationType.Substitution then
            SpacerFinder.applySubstitutionSpecialRule grnaSize gRNAs,
            SpacerFinder.applySubstitutionSpecialRule grnaSize originalGRNAs
        else
            gRNAs, originalGRNAs

    return  {
        gRNA = gRNAs
        originalGRNA = originalGRNAs
        mutatedSequence = finalMutated
        originalSequence = finalOriginal
        extraNucleotids = extraNucleotids
    }
}

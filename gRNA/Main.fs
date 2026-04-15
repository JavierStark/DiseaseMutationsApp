module Main

open gRNA
open System.Threading.Tasks

type ResultFromHGVS = {
    gRNA: SpacerFinder.gRNAResult list
    mutatedSequence: string
    originalSequence: string
    extraNucleotids: int
}
    

let getBestgRNAFromHGVS (hgvsString: string) (grnaSize: int) (bowtieService: gRNA.Services.BowtieService) (cancellationToken: System.Threading.CancellationToken) = task {
    let hgvsObj = HGVS.HGVS(hgvsString)
    let! sequence = SequenceRepository.SequenceRepository.GetSequence(hgvsObj.Accession)
    let extraNucleotids = grnaSize - hgvsObj.GetMutationLength()

    let mutated, original = sequence.GetMutatedSubsequence(hgvsObj, extraNucleotids, extraNucleotids)
    
    let! gRNAs = SpacerFinder.getOrderedgRna grnaSize mutated bowtieService cancellationToken

    return  {
        gRNA = gRNAs
        mutatedSequence = mutated
        originalSequence = original
        extraNucleotids = extraNucleotids
    }
}



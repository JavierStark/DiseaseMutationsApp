module Main

open gRNA

type ResultFromHGVS = {
    gRNA: SpacerFinder.gRNAResult list
    mutatedSequence: string
    originalSequence: string
    extraNucleotids: int
}
    

let getBestgRNAFromHGVS (hgvsString: string) (grnaSize: int) = async {
    let hgvsObj = HGVS.HGVS(hgvsString)
    let! sequence = SequenceRepository.SequenceRepository.GetSequence(hgvsObj.Accession)
    let extraNucleotids = grnaSize - hgvsObj.GetMutationLength()
    
    let mutated, original = sequence.GetMutatedSubsequence(hgvsObj, extraNucleotids, extraNucleotids)
    
    let! bestgRna = SpacerFinder.getBestgRNA grnaSize mutated
    
    return  {
        gRNA = bestgRna
        mutatedSequence = mutated
        originalSequence = original
        extraNucleotids = extraNucleotids
    }
}



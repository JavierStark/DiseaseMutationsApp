module Main

open gRNA
open System.Threading.Tasks

type ResultFromHGVS = {
    gRNA: SpacerFinder.gRNAResult list
    originalGRNA: SpacerFinder.gRNAResult list
    mutatedSequence: string
    originalSequence: string
    extraNucleotids: int
    /// 1-based inclusive window of the accession that original/mutated were cut from (same for both strands).
    windowStart: int
    windowEnd: int
}

/// Spacer sizes outside this range are rejected: shorter spacers cannot be specific, longer ones are not Cas13 spacers
/// and make the number of Bowtie reads grow without a use.
[<Literal>]
let MinSpacerSize = 18
[<Literal>]
let MaxSpacerSize = 60

/// Throws GrnaInputException unless 18 <= spacer <= 60 and 0 <= seedStart <= seedEnd < spacer.
let validateSpacerParameters (grnaSize: int) (seedStart: int) (seedEnd: int) : unit =
    if grnaSize < MinSpacerSize || grnaSize > MaxSpacerSize then
        raise (GrnaInputException(sprintf "Spacer size must be between %d and %d, got %d." MinSpacerSize MaxSpacerSize grnaSize))
    if seedStart < 0 || seedEnd < seedStart || seedEnd >= grnaSize then
        raise (GrnaInputException(sprintf "The seed region must satisfy 0 <= start <= end < spacer size (%d), got %d-%d." grnaSize seedStart seedEnd))

/// Checks a parsed variant against the fetched accession sequence: supported coordinate system and mutation type,
/// positions inside the sequence, and the stated reference allele equal to the real one. Throws GrnaInputException.
let validateAgainstSequence (hgvs: HGVS.HGVS) (data: string) : unit =
    match hgvs.Type with
    | "g" | "m" | "n" -> ()
    | "c" ->
        raise (GrnaInputException(
            "Coding-sequence (c.) positions are counted from the CDS start, which Diana does not convert yet. " +
            "Use genomic (g.) coordinates, for example the g. notations an rsID resolves to."))
    | t -> raise (GrnaInputException(sprintf "Only g., m. and n. coordinates are supported, got '%s.'." t))

    match hgvs.Mutation with
    | HGVS.MutationType.Repeat | HGVS.MutationType.Conversion | HGVS.MutationType.Translocation | HGVS.MutationType.LargeUnknown ->
        raise (GrnaInputException(sprintf "The %A mutation type is not supported for design." hgvs.Mutation))
    | _ -> ()

    let startPos, endPos = hgvs.Position
    if endPos > data.Length then
        raise (GrnaInputException(sprintf "Position %d is outside %s, which has %d bases." endPos hgvs.Accession data.Length))
    if hgvs.Mutation = HGVS.MutationType.Insertion && endPos <> startPos + 1 then
        raise (GrnaInputException "An insertion needs two adjacent positions, for example 215_216insA.")

    let actual = data.Substring(startPos - 1, endPos - startPos + 1).ToUpperInvariant()
    let stated =
        match hgvs.Mutation with
        | HGVS.MutationType.Substitution | HGVS.MutationType.Deletion | HGVS.MutationType.Duplication | HGVS.MutationType.Inversion -> hgvs.Reference
        | _ -> ""
    if stated <> "" && stated <> actual then
        raise (GrnaInputException(sprintf "Reference mismatch at %s:%d_%d: the variant states %s but the sequence has %s." hgvs.Accession startPos endPos stated actual))

/// Maps a (start, length) span from the forward orientation of a string of the given length to its reverse complement.
/// A zero-length span (a deletion junction) keeps pointing at the same boundary.
let mirrorSpan (totalLength: int) (span: int * int) : int * int =
    let spanStart, spanLength = span
    (totalLength - (spanStart + spanLength), spanLength)

/// The complement tab designs against the OTHER strand: its sequence is the reverse complement, read 5'->3' like the
/// normal tab, so the spacer, the Bowtie query and the folding all describe a real molecule.
let orientForStrand (complement: bool) (sequence: string) (span: int * int) : string * (int * int) =
    if complement then Sequence.reverseComplement sequence, mirrorSpan sequence.Length span
    else sequence, span

/// The 1-based inclusive accession window that Sequence.GetMutatedSubsequence slices for these paddings.
let calculateWindow (extraNucleotids: int) (sequenceLength: int) (hgvs: HGVS.HGVS) : int * int =
    let windowStart = max 0 ((fst hgvs.Position) - 1 - extraNucleotids) + 1
    let windowEnd = min sequenceLength ((snd hgvs.Position) + extraNucleotids)
    (windowStart, windowEnd)

let calculateMutationSpanInMutated (extraNucleotids: int) (sequenceLength: int) (hgvs: HGVS.HGVS) (mutatedLength: int) : int * int =
    let leftContext = max 0 (min extraNucleotids ((fst hgvs.Position) - 1))
    let rightContext = max 0 (min extraNucleotids (sequenceLength - (snd hgvs.Position)))
    let mutationLengthInMutated = max 0 (mutatedLength - leftContext - rightContext)
    (leftContext, mutationLengthInMutated)

let getBestgRNAFromHGVS (hgvsString: string) (grnaSize: int) (seedStart: int) (seedEnd: int) (bowtieService: gRNA.Services.BowtieService) (cancellationToken: System.Threading.CancellationToken) (complement: bool) = task {
    validateSpacerParameters grnaSize seedStart seedEnd
    let hgvsObj = HGVS.HGVS(hgvsString)
    let! sequence = SequenceRepository.SequenceRepository.GetSequence(hgvsObj.Accession)
    validateAgainstSequence hgvsObj sequence.Data

    // Never negative: a variant longer than the spacer needs no extra flank, and a negative value would shrink the slice inwards.
    let extraNucleotids = max 0 (grnaSize - hgvsObj.GetMutationLength())

    let mutated, original = sequence.GetMutatedSubsequence(hgvsObj, extraNucleotids, extraNucleotids)

    let mutatedSpan = calculateMutationSpanInMutated extraNucleotids sequence.Data.Length hgvsObj mutated.Length
    let originalSpan = calculateMutationSpanInMutated extraNucleotids sequence.Data.Length hgvsObj original.Length

    let finalMutated, (mutationStartInMutated, mutationLengthInMutated) = orientForStrand complement mutated mutatedSpan
    let finalOriginal, (mutationStartInOriginal, mutationLengthInOriginal) = orientForStrand complement original originalSpan

    if finalMutated.Length < grnaSize then
        raise (GrnaInputException(
            sprintf "The mutated region is %d nt, shorter than the spacer size (%d nt), so no spacer window fits. Use a smaller spacer size or a shorter variant."
                finalMutated.Length grnaSize))

    let gRNAsTask =
        SpacerFinder.getOrderedgRna
            seedStart
            seedEnd
            grnaSize
            finalMutated
            mutationStartInMutated
            mutationLengthInMutated
            bowtieService
            cancellationToken

    let originalGRNAsTask =
        SpacerFinder.getOrderedgRna
            seedStart
            seedEnd
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
            SpacerFinder.applySubstitutionSpecialRule seedStart seedEnd grnaSize gRNAs,
            SpacerFinder.applySubstitutionSpecialRule seedStart seedEnd grnaSize originalGRNAs
        else
            gRNAs, originalGRNAs

    let windowStart, windowEnd = calculateWindow extraNucleotids sequence.Data.Length hgvsObj

    return  {
        gRNA = gRNAs
        originalGRNA = originalGRNAs
        mutatedSequence = finalMutated
        originalSequence = finalOriginal
        extraNucleotids = extraNucleotids
        windowStart = windowStart
        windowEnd = windowEnd
    }
}

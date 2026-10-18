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
    /// Things the user should know about this result (for example that the substitution rule could not be applied).
    notices: string list
}

/// Spacer sizes outside this range are rejected: shorter spacers cannot be specific, longer ones are not Cas13 spacers
/// and make the number of Bowtie reads grow without a use.
[<Literal>]
let MinSpacerSize = 18
[<Literal>]
let MaxSpacerSize = 60

/// Where the substitution rule puts the variant and its deliberate extra mismatch in the spacer. Both positions are counted
/// from the spacer's 3' end, 1 being the last letter (the default is 3 and 4).
type SpecialRuleOptions = { SnpPos: int; MismatchPos: int }

let defaultSpecialRule : SpecialRuleOptions =
    { SnpPos = SpacerFinder.DefaultSnpPosition; MismatchPos = SpacerFinder.DefaultMismatchPosition }

/// Throws GrnaInputException unless both positions lie inside the spacer (1 = its last letter) and they differ: changing the
/// variant's own letter would defeat the purpose of the rule.
let validateSpecialRule (grnaSize: int) (rule: SpecialRuleOptions) : unit =
    if rule.SnpPos < 1 || rule.SnpPos > grnaSize then
        raise (GrnaInputException(sprintf "The variant position must be between 1 and the spacer size (%d), counted from the spacer's 3' end, got %d." grnaSize rule.SnpPos))
    if rule.MismatchPos < 1 || rule.MismatchPos > grnaSize then
        raise (GrnaInputException(sprintf "The mismatch position must be between 1 and the spacer size (%d), counted from the spacer's 3' end, got %d." grnaSize rule.MismatchPos))
    if rule.SnpPos = rule.MismatchPos then
        raise (GrnaInputException "The engineered mismatch cannot be at the variant's own position: choose two different positions.")

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
        // getBestgRNAFromHGVS converts c. positions of a transcript record before this point. A c. position that gets here
        // was not converted (a genomic record, or a caller that skipped the conversion) and must never be used as an index.
        HGVS.requireCodingTranscript hgvs.Accession
        raise (GrnaInputException("Coding-sequence (c.) positions are counted from the CDS start and must be converted to transcript positions before the design; the CDS start of this record is not known here."))
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

/// The warning shown when the substitution special rule could not be applied to one or both candidate lists, or None.
let specialRuleNoticeAt (snpPos: int) (hgvs: HGVS.HGVS) (sequenceLength: int) (windowSize: int) (appliesToMutated: bool) (appliesToOriginal: bool) : string option =
    if appliesToMutated && appliesToOriginal then None
    else
        let which =
            if not appliesToMutated && not appliesToOriginal then "mutated and original sequences"
            elif not appliesToMutated then "mutated sequence"
            else "original sequence"
        Some(sprintf "The substitution rule (an engineered mismatch near the mutation, with the mutation at position %d from the spacer's 3' end) could not be applied to the %s: the mutation is at position %d of %s (%d bases), too close to an end of the sequence for a window that puts it there, which needs at least %d and %d bases on its two sides. The ordinary ranking is shown instead."
                snpPos which (fst hgvs.Position) hgvs.Accession sequenceLength (snpPos - 1) (windowSize - snpPos))

/// The warning with the default variant position (3rd from the 3' end).
let specialRuleNotice (hgvs: HGVS.HGVS) (sequenceLength: int) (windowSize: int) (appliesToMutated: bool) (appliesToOriginal: bool) : string option =
    specialRuleNoticeAt SpacerFinder.DefaultSnpPosition hgvs sequenceLength windowSize appliesToMutated appliesToOriginal

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

/// The whole design for one variant. `rule` places the engineered mismatch (see `SpecialRuleOptions`) and `repeat` is the
/// 5' context the spacers are folded with while screening: "" for the spacer alone, otherwise a direct repeat (see
/// `SpacerFinder.normaliseRepeat`, applied here, so callers may pass raw text).
let getBestgRNAFromHGVSWithRuleAndRepeat (rule: SpecialRuleOptions) (repeat: string) (hgvsString: string) (grnaSize: int) (seedStart: int) (seedEnd: int) (bowtieService: gRNA.Services.BowtieService) (cancellationToken: System.Threading.CancellationToken) (complement: bool) = task {
    validateSpacerParameters grnaSize seedStart seedEnd
    validateSpecialRule grnaSize rule
    let repeat = SpacerFinder.normaliseRepeat repeat
    // c. positions on a transcript record (NM_...:c.20A>T) are converted to positions of the mRNA (n.70A>T) first.
    let! hgvsString, conversionNotice =
        task {
            match HGVS.tryParseCoding hgvsString with
            | Some(accession, _) ->
                HGVS.requireCodingTranscript accession
                let! cdsStart, cdsEnd = SequenceRepository.SequenceRepository.GetCodingRange(accession, cancellationToken)
                let converted = HGVS.convertCodingToTranscript cdsStart cdsEnd hgvsString
                return converted, Some(sprintf "%s was converted to %s: the coding sequence of %s starts at position %d of the transcript, so c.1 is n.%d. The guides are designed on the mRNA itself." hgvsString converted accession cdsStart cdsStart)
            | None -> return hgvsString, None
        }
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
        SpacerFinder.getOrderedgRnaWith
            repeat
            seedStart
            seedEnd
            grnaSize
            finalMutated
            mutationStartInMutated
            mutationLengthInMutated
            bowtieService
            cancellationToken

    let originalGRNAsTask =
        SpacerFinder.getOrderedgRnaWith
            repeat
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

    let isSubstitution = hgvsObj.Mutation = HGVS.MutationType.Substitution
    let appliesToMutated = SpacerFinder.specialRuleAppliesAt rule.SnpPos grnaSize gRNAs
    let appliesToOriginal = SpacerFinder.specialRuleAppliesAt rule.SnpPos grnaSize originalGRNAs

    let gRNAs, originalGRNAs =
        if isSubstitution then
            SpacerFinder.applySubstitutionSpecialRuleAt rule.SnpPos rule.MismatchPos seedStart seedEnd grnaSize gRNAs,
            SpacerFinder.applySubstitutionSpecialRuleAt rule.SnpPos rule.MismatchPos seedStart seedEnd grnaSize originalGRNAs
        else
            gRNAs, originalGRNAs

    // How well each guide tells the two alleles apart: its DNA target against the closest window of the other allele.
    // Done after the substitution rule so the engineered spacer is measured as it will be ordered.
    let gRNAs = SpacerFinder.annotateDiscrimination finalOriginal gRNAs
    let originalGRNAs = SpacerFinder.annotateDiscrimination finalMutated originalGRNAs

    let notices =
        (conversionNotice |> Option.toList)
        @ (if isSubstitution then
               specialRuleNoticeAt rule.SnpPos hgvsObj sequence.Data.Length grnaSize appliesToMutated appliesToOriginal |> Option.toList
           else [])

    let windowStart, windowEnd = calculateWindow extraNucleotids sequence.Data.Length hgvsObj

    return  {
        gRNA = gRNAs
        originalGRNA = originalGRNAs
        mutatedSequence = finalMutated
        originalSequence = finalOriginal
        extraNucleotids = extraNucleotids
        windowStart = windowStart
        windowEnd = windowEnd
        notices = notices
    }
}

/// `getBestgRNAFromHGVSWithRuleAndRepeat` with the default direct repeat (the 36 nt Cas13 scaffold).
let getBestgRNAFromHGVSWithRule (rule: SpecialRuleOptions) (hgvsString: string) (grnaSize: int) (seedStart: int) (seedEnd: int) (bowtieService: gRNA.Services.BowtieService) (cancellationToken: System.Threading.CancellationToken) (complement: bool) =
    getBestgRNAFromHGVSWithRuleAndRepeat rule SpacerFinder.scaffold hgvsString grnaSize seedStart seedEnd bowtieService cancellationToken complement

/// `getBestgRNAFromHGVSWithRuleAndRepeat` with the default substitution rule (variant 3rd and mismatch 4th from the spacer's 3' end).
let getBestgRNAFromHGVSWith (repeat: string) (hgvsString: string) (grnaSize: int) (seedStart: int) (seedEnd: int) (bowtieService: gRNA.Services.BowtieService) (cancellationToken: System.Threading.CancellationToken) (complement: bool) =
    getBestgRNAFromHGVSWithRuleAndRepeat defaultSpecialRule repeat hgvsString grnaSize seedStart seedEnd bowtieService cancellationToken complement

/// Designs guides with the default substitution rule and the default direct repeat.
let getBestgRNAFromHGVS (hgvsString: string) (grnaSize: int) (seedStart: int) (seedEnd: int) (bowtieService: gRNA.Services.BowtieService) (cancellationToken: System.Threading.CancellationToken) (complement: bool) =
    getBestgRNAFromHGVSWithRule defaultSpecialRule hgvsString grnaSize seedStart seedEnd bowtieService cancellationToken complement

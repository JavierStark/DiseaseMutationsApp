module gRNA.SpacerFinder

open System.Threading.Tasks
open gRNA.RNAFoldWrapper

/// Constant Cas13 scaffold (36 nt) that precedes the spacer in the complete gRNA. It is the default direct repeat.
let scaffold = "GAUUUAGACUACCCCAAAAACGAAGGGGACUAAAAC"

/// Longest custom direct repeat accepted; real repeats are 30-40 nt, so this only stops pasted sequences that are not one.
[<Literal>]
let MaxRepeatLength = 200

/// The 5' context a spacer is folded with is a "direct repeat" string: "" means the spacer alone, anything else is
/// prepended to every spacer. This turns what a user typed into that string (RNA, no blanks); "" stays "".
/// Throws GrnaInputException for letters that are not A, C, G, U/T or for a sequence that is too long.
let normaliseRepeat (raw: string) : string =
    let cleaned =
        (if isNull raw then "" else raw)
        |> Seq.filter (fun c -> not (System.Char.IsWhiteSpace c))
        |> Seq.map (fun c -> match System.Char.ToUpperInvariant c with 'T' -> 'U' | u -> u)
        |> Seq.toArray
        |> System.String
    if cleaned |> Seq.exists (fun c -> not ("ACGU".Contains c)) then
        raise (GrnaInputException "A direct repeat may contain only the letters A, C, G and U (or T).")
    if cleaned.Length > MaxRepeatLength then
        raise (GrnaInputException(sprintf "A direct repeat is at most %d letters long, got %d." MaxRepeatLength cleaned.Length))
    cleaned

let slidingWindow (input: string) (windowSize: int) =
    if windowSize <= 0 then
        invalidArg "windowSize" "Window size must be greater than 0."
    elif input.Length < windowSize then
        []
    else
        [ for i in 0 .. input.Length - windowSize -> input.Substring(i, windowSize) ]


let calculateGCContent (sequence: string) =
    let gcCount = sequence |> Seq.filter (fun c -> c = 'G' || c = 'C') |> Seq.length
    let totalCount = sequence.Length

    if totalCount = 0 then
        0.0
    else
        System.Math.Round((float gcCount / float totalCount) * 100.0, 2)

let calculateGCScore (gcContent: float) (lowerThreshold: float, upperThreshold: float) =
    if gcContent <= upperThreshold && gcContent >= lowerThreshold then
        1.0
    else if gcContent < lowerThreshold then
        gcContent / lowerThreshold
    else
        (100.0 - gcContent) / (100.0 - upperThreshold)

let countHomopolymers (sequence: string) =
    let pattern = "(A{4,}|C{4,}|G{4,}|U{4,})"
    System.Text.RegularExpressions.Regex.Matches(sequence, pattern).Count


type gRNAResult =
    { Sequence: string
      GCScore: float
      GCContent: float
      HomopolymerCount: int
      SeedRegion: string
      Offtargets: int
      RnaFoldResult: RNAFoldResult
      Rank: int
      Score: float
      MutationHighlightStart: int
      MutationHighlightLength: int
      /// Fewest letters in which this spacer's DNA target differs from the closest window of the OTHER allele
      /// (a mutated-list spacer is compared with the original sequence, an original-list spacer with the mutated one).
      /// A guide with 0-1 mismatches cannot tell the alleles apart. -1 means "not computed".
      WtMismatches: int
      /// True for the spacer built by the substitution rule (one extra, deliberate mismatch next to the variant).
      Engineered: bool
      /// True when the scaffold's own hairpin (the one Cas13 recognises) is still formed in the fold of the complete gRNA.
      ScaffoldIntact: bool
      /// Share (0-1) of the spacer's letters left unpaired in the fold of the complete gRNA; -1 = not computed.
      SpacerUnpaired: float }

/// Extracts the seed region substring for a given inclusive [seedStart, seedEnd] range,
/// clamping into the sequence bounds. Returns "" for an empty sequence or an inverted range.
let getSeedRegion (seedStart: int) (seedEnd: int) (sequence: string) =
    if System.String.IsNullOrEmpty(sequence) then
        ""
    else
        let lastIndex = sequence.Length - 1
        let clampedStart = seedStart |> max 0 |> min lastIndex
        let clampedEnd = seedEnd |> max 0 |> min lastIndex

        if clampedStart > clampedEnd then
            ""
        else
            sequence[clampedStart..clampedEnd]

let getgRNAResult (seedStart: int) (seedEnd: int) (sequence: string) : gRNAResult =
    let gcContent = calculateGCContent sequence
    let gcScore = calculateGCScore gcContent (40.0, 60.0)
    let homopolymerCount = countHomopolymers sequence
    let seedRegion = getSeedRegion seedStart seedEnd sequence

    {
        Sequence = sequence
        GCScore = gcScore
        GCContent = gcContent
        HomopolymerCount = homopolymerCount
        SeedRegion = seedRegion
        Offtargets = 0
        RnaFoldResult = { Structure = ""; Energy = 0.0 }
        Rank = 0
        Score = 0.0
        MutationHighlightStart = -1
        MutationHighlightLength = 0
        WtMismatches = -1
        Engineered = false
        ScaffoldIntact = true
        SpacerUnpaired = -1.0
    }

/// Flips the letter `positionFromEnd` places from the 3' end (1 = the last letter) to A, or to U when it already is A.
/// A sequence shorter than that is returned unchanged.
let adjustPositionFromEndToAorU (positionFromEnd: int) (sequence: string) =
    if System.String.IsNullOrEmpty(sequence) || positionFromEnd < 1 || sequence.Length < positionFromEnd then
        sequence
    else
        let targetIndex = sequence.Length - positionFromEnd
        let replacement =
            if sequence.[targetIndex] = 'A' then 'U'
            else 'A'

        sequence.Remove(targetIndex, 1).Insert(targetIndex, string replacement)

/// The default rule changes the 4th letter from the 3' end.
let adjustFourthFromEndToAorU (sequence: string) = adjustPositionFromEndToAorU 4 sequence

/// Default positions of the substitution rule, both counted from the spacer's 3' end (1 = last letter):
/// the variant is the 3rd letter, the engineered mismatch the 4th.
[<Literal>]
let DefaultSnpPosition = 3
[<Literal>]
let DefaultMismatchPosition = 4

/// True when some candidate carries the mutation at index `windowSize - 3`, i.e. when the rule below can build its
/// engineered spacer. It cannot when the mutation is too close to an end of the sequence for such a window to exist.
let specialRuleAppliesAt (snpPos: int) (windowSize: int) (results: gRNAResult list) =
    results
    |> List.exists (fun result ->
        result.MutationHighlightStart = windowSize - snpPos
        && result.MutationHighlightLength > 0)

let specialRuleApplies (windowSize: int) (results: gRNAResult list) =
    specialRuleAppliesAt DefaultSnpPosition windowSize results

/// Puts the mismatch-engineered candidate FIRST (marked `Engineered`, Rank 1, Score 1.0) and keeps the whole ordinary
/// ranked list after it, unchanged. When no candidate carries the mutation at `windowSize - 3` (see `specialRuleApplies`)
/// the ranked list is returned UNCHANGED: the caller reports that the rule did not apply.
let applySubstitutionSpecialRuleAt (snpPos: int) (mismatchPos: int) (seedStart: int) (seedEnd: int) (windowSize: int) (results: gRNAResult list) =
    let targetMutationIndex = windowSize - snpPos

    results
    |> List.tryFind (fun result ->
        result.MutationHighlightStart = targetMutationIndex
        && result.MutationHighlightLength > 0)
    |> Option.map (fun selected ->
        let adjustedSequence = adjustPositionFromEndToAorU mismatchPos selected.Sequence
        let adjustedBaseResult = getgRNAResult seedStart seedEnd adjustedSequence

        { adjustedBaseResult with
            Offtargets = selected.Offtargets
            RnaFoldResult = selected.RnaFoldResult
            MutationHighlightStart = selected.MutationHighlightStart
            MutationHighlightLength = selected.MutationHighlightLength
            Engineered = true
            ScaffoldIntact = selected.ScaffoldIntact
            SpacerUnpaired = selected.SpacerUnpaired
            Rank = 1
            Score = 1.0 })
    |> function
        | Some adjusted -> adjusted :: results
        | None -> results

/// The default rule: variant 3rd from the 3' end, engineered mismatch 4th from the 3' end.
let applySubstitutionSpecialRule (seedStart: int) (seedEnd: int) (windowSize: int) (results: gRNAResult list) =
    applySubstitutionSpecialRuleAt DefaultSnpPosition DefaultMismatchPosition seedStart seedEnd windowSize results

/// The DNA window a spacer was derived from: spacer = reverse(complement(window)) with T to U.
let windowOfSpacer (spacer: string) =
    spacer.Replace('U', 'T') |> Sequence.reverseComplement

/// Fewest differing letters between `window` and any same-length window of `other`; the window length itself when
/// `other` is shorter than the window (nothing to compare with).
let minMismatchesAgainst (window: string) (other: string) =
    let n = window.Length
    if n = 0 || other.Length < n then n
    else
        let mutable best = n
        let mutable i = 0
        while best > 0 && i <= other.Length - n do
            let mutable d = 0
            let mutable j = 0
            while j < n && d < best do
                if window.[j] <> other.[i + j] then d <- d + 1
                j <- j + 1
            if d < best then best <- d
            i <- i + 1
        best

/// Fills `WtMismatches` for every candidate against the DNA of the other allele (same strand orientation).
let annotateDiscrimination (otherSequence: string) (results: gRNAResult list) =
    results
    |> List.map (fun r -> { r with WtMismatches = minMismatchesAgainst (windowOfSpacer r.Sequence) otherSequence })

let getMutationHighlightSpan (windowStart: int) (windowSize: int) (mutationStart: int) (mutationLength: int) : int * int =
    let windowEndExclusive = windowStart + windowSize
    let mutationEndExclusive = mutationStart + mutationLength

    if mutationLength > 0 then
        let overlapStart = max windowStart mutationStart
        let overlapEnd = min windowEndExclusive mutationEndExclusive
        let overlapLength = overlapEnd - overlapStart

        if overlapLength > 0 then
            (overlapStart - windowStart, overlapLength)
        else
            (-1, 0)
    else
        // Anchor visualization for zero-length mutations (e.g. deletion in local mutated sequence).
        // The upper bound is inclusive of windowEndExclusive itself so a deletion sitting right at
        // the window's exclusive end still anchors to the window's last base instead of being dropped.
        if mutationStart >= windowStart && mutationStart <= windowEndExclusive then
            let anchor =
                if mutationStart <= windowStart then 0
                elif mutationStart >= windowEndExclusive then windowSize - 1
                else mutationStart - windowStart
            (anchor, 1)
        else
            (-1, 0)

/// Base pairs (i, j) of a dot-bracket structure. Brackets other than ( ) are ignored.
let basePairs (structure: string) : Set<int * int> =
    let stack = System.Collections.Generic.Stack<int>()
    let mutable pairs = Set.empty
    structure |> Seq.iteri (fun i c ->
        if c = '(' then stack.Push i
        elif c = ')' && stack.Count > 0 then pairs <- pairs.Add((stack.Pop(), i)))
    pairs

/// True when every base pair of the scaffold's own fold (`reference`, the fold of the scaffold alone) is still present in the
/// fold of the complete gRNA (`full`), i.e. the spacer did not pull the hairpin apart.
let scaffoldIntact (reference: string) (full: string) : bool =
    if System.String.IsNullOrEmpty reference || System.String.IsNullOrEmpty full then true
    else Set.isSubset (basePairs reference) (basePairs full)

/// Share of the spacer's letters (after the scaffold) that are unpaired in the fold of the complete gRNA; -1 when unknown.
let spacerUnpaired (scaffoldLength: int) (full: string) : float =
    if System.String.IsNullOrEmpty full || full.Length <= scaffoldLength then -1.0
    else
        let spacerPart = full.Substring scaffoldLength
        float (spacerPart |> Seq.filter (fun c -> c = '.') |> Seq.length) / float spacerPart.Length

/// Folds of direct repeats alone, each computed once per process. Custom repeats are user input, so the table is bounded.
let private repeatReferences = System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<Task<string>>>()
let private MaxCachedRepeats = 64

/// The fold of the repeat alone (what Cas13 needs intact); "" for no repeat.
let private repeatReference (repeat: string) : Task<string> =
    if repeat = "" then Task.FromResult ""
    else
        if repeatReferences.Count >= MaxCachedRepeats then repeatReferences.Clear()
        repeatReferences.GetOrAdd(repeat, fun r ->
            lazy (task {
                let! fold = RNAFoldWrapper.fold r System.Threading.CancellationToken.None
                return fold.Structure
            })).Value

/// One construct (direct repeat + spacer) folded, with the two checks derived from that fold.
type ConstructFold =
    { Fold: RNAFoldResult
      /// False when the spacer pulls the repeat's own hairpin apart; always true without a repeat.
      Intact: bool
      /// Share (0-1) of the spacer left unpaired; -1 = not computed.
      Unpaired: float }

/// Folds `repeat + spacer` for every spacer. The screening and the Builder's complete-gRNA fold both go through here,
/// so the numbers mean the same thing wherever the context is chosen. `repeat` = "" folds each spacer alone.
let foldConstructs (repeat: string) (spacers: string list) (cancellationToken: System.Threading.CancellationToken) : Task<ConstructFold list> =
    task {
        let! folds = foldMany (spacers |> List.map (fun s -> repeat + s)) cancellationToken
        let! reference = repeatReference repeat
        return
            folds
            |> List.map (fun fold ->
                { Fold = fold
                  Intact = scaffoldIntact reference fold.Structure
                  Unpaired = spacerUnpaired repeat.Length fold.Structure })
    }

/// The two checks for a fold of a LARGER construct (adapters, flanks): the repeat sits at `repeatStart`, the spacer at
/// `spacerStart`. Returns (repeat hairpin still formed, share of the spacer left unpaired or -1 when it does not fit).
let checkWithin (repeat: string) (repeatStart: int) (spacerStart: int) (spacerLength: int) (structure: string) : Task<bool * float> =
    task {
        if System.String.IsNullOrEmpty structure then return true, -1.0
        else
            let! reference = repeatReference repeat
            let shifted = basePairs reference |> Set.map (fun (i, j) -> (i + repeatStart, j + repeatStart))
            let unpaired =
                if spacerLength <= 0 || structure.Length < spacerStart + spacerLength then -1.0
                else float (structure.Substring(spacerStart, spacerLength) |> Seq.filter (fun c -> c = '.') |> Seq.length) / float spacerLength
            return Set.isSubset shifted (basePairs structure), unpaired
    }

let sortByResult (result: gRNAResult) =
    (result.Offtargets, -result.RnaFoldResult.Energy, -result.GCScore, result.HomopolymerCount)

let reverse (sequence: string) =
    sequence
    |> Seq.rev
    |> Seq.toArray
    |> System.String

/// Ranks every window of `sequence` as a spacer. `repeat` is the 5' context each spacer is folded with while screening
/// ("" = the spacer alone); it must already be normalised (see `normaliseRepeat`).
let getOrderedgRnaWith (repeat: string) (seedStart: int) (seedEnd: int) (window: int) (sequence: string) (mutationStart: int) (mutationLength: int) (bowtieService: gRNA.Services.BowtieService) (cancellationToken: System.Threading.CancellationToken) : Task<gRNAResult list> =
    task {
        let subsequences = slidingWindow sequence window
        // A sequence shorter than the spacer has no window: nothing to align or fold (Bowtie rejects an empty read list).
        if List.isEmpty subsequences then return [] else
        let results =
            subsequences
            |> List.mapi (fun i subsequence ->
                let rawHighlightStart, highlightLength = getMutationHighlightSpan i window mutationStart mutationLength
                let highlightStart =
                    if rawHighlightStart >= 0 && highlightLength > 0 then
                        window - (rawHighlightStart + highlightLength)
                    else
                        rawHighlightStart
                let sequenceResult =
                    subsequence
                    |> Sequence.complementary
                    |> reverse
                    |> _.Replace('T', 'U')
                    |> getgRNAResult seedStart seedEnd
                { sequenceResult with
                    MutationHighlightStart = highlightStart
                    MutationHighlightLength = highlightLength })
        let! offtargets = bowtieService.ProcessMultipleSequencesAsync(subsequences, 2, 2, cancellationToken)
        // Fold the actual RNA (direct repeat + spacer), not the raw DNA window.
        let! constructs = foldConstructs repeat (results |> List.map (fun r -> r.Sequence)) cancellationToken

        let results =
            List.zip3 results constructs offtargets
            |> List.map (fun (res, construct, align) ->
                { res with
                    RnaFoldResult = construct.Fold
                    ScaffoldIntact = construct.Intact
                    SpacerUnpaired = construct.Unpaired
                    Offtargets = align})

        let filteredResults = results |> List.sortBy sortByResult
        
        let rankedResults =
            filteredResults
            |> List.fold (fun (acc: _ list, currentGroupRank: int) res ->
                let (newRank, nextGroupRank) =
                    match acc with
                    | [] -> (1, 2)  // First element is group 1, next will be group 2
                    | prevRes :: _ ->
                        if sortByResult res = sortByResult prevRes then
                            (prevRes.Rank, currentGroupRank)  // Same group, don't increment
                        else
                            (currentGroupRank, currentGroupRank + 1)  // New group, increment for next
                
                let newRes = { res with Rank = newRank }
                (newRes :: acc, nextGroupRank)
                ) ([], 1)
            |> fst
            |> List.rev

        let totalGroups = rankedResults |> List.map _.Rank |> List.distinct |> List.length
        let finalResults =
            rankedResults
            |> List.map (fun res ->
                let score = (float (totalGroups - res.Rank + 1)) / (float totalGroups)
                { res with Score = System.Math.Round(score, 2) })

        return finalResults
    }

/// `getOrderedgRnaWith` with the default direct repeat (the 36 nt Cas13 scaffold).
let getOrderedgRna (seedStart: int) (seedEnd: int) (window: int) (sequence: string) (mutationStart: int) (mutationLength: int) (bowtieService: gRNA.Services.BowtieService) (cancellationToken: System.Threading.CancellationToken) : Task<gRNAResult list> =
    getOrderedgRnaWith scaffold seedStart seedEnd window sequence mutationStart mutationLength bowtieService cancellationToken

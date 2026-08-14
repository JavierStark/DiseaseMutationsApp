module gRNA.SpacerFinder

open System.Threading.Tasks
open gRNA.RNAFoldWrapper

/// Constant Cas13 scaffold (36 nt) that precedes the spacer in the complete gRNA.
let scaffold = "GAUUUAGACUACCCCAAAAACGAAGGGGACUAAAAC"

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
    if gcContent < upperThreshold && gcContent > lowerThreshold then
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
      Allignments: int
      RnaFoldResult: RNAFoldResult
      Rank: int
      Score: float
      MutationHighlightStart: int
      MutationHighlightLength: int }

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
        Allignments = 0
        RnaFoldResult = { Structure = ""; Energy = 0.0 }
        Rank = 0
        Score = 0.0
        MutationHighlightStart = -1
        MutationHighlightLength = 0
    }

let adjustFourthFromEndToAorU (sequence: string) =
    if System.String.IsNullOrEmpty(sequence) || sequence.Length < 4 then
        sequence
    else
        let targetIndex = sequence.Length - 4
        let replacement =
            if sequence.[targetIndex] = 'A' then 'U'
            else 'A'

        sequence.Remove(targetIndex, 1).Insert(targetIndex, string replacement)

let applySubstitutionSpecialRule (seedStart: int) (seedEnd: int) (windowSize: int) (results: gRNAResult list) =
    let targetMutationIndex = windowSize - 3

    results
    |> List.tryFind (fun result ->
        result.MutationHighlightStart = targetMutationIndex
        && result.MutationHighlightLength > 0)
    |> Option.map (fun selected ->
        let adjustedSequence = adjustFourthFromEndToAorU selected.Sequence
        let adjustedBaseResult = getgRNAResult seedStart seedEnd adjustedSequence

        { adjustedBaseResult with
            Allignments = selected.Allignments
            RnaFoldResult = selected.RnaFoldResult
            MutationHighlightStart = selected.MutationHighlightStart
            MutationHighlightLength = selected.MutationHighlightLength
            Rank = 1
            Score = 1.0 })
    |> Option.toList

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

let sortByResult (result: gRNAResult) =
    (result.Allignments, -result.RnaFoldResult.Energy, -result.GCScore, result.HomopolymerCount)

let reverse (sequence: string) =
    sequence
    |> Seq.rev
    |> Seq.toArray
    |> System.String

let getOrderedgRna (seedStart: int) (seedEnd: int) (window: int) (sequence: string) (mutationStart: int) (mutationLength: int) (bowtieService: gRNA.Services.BowtieService) (cancellationToken: System.Threading.CancellationToken) : Task<gRNAResult list> =
    task {
        let subsequences = slidingWindow sequence window
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
        // Fold the actual RNA gRNA (scaffold + spacer), not the raw DNA window.
        let gRNASequences = results |> List.map (fun r -> scaffold + r.Sequence)

        let! allignments = bowtieService.ProcessMultipleSequencesAsync(subsequences, 2, 2, cancellationToken)
        let! folds = foldMany gRNASequences
        
        let results =
            List.zip3 results folds allignments
            |> List.map (fun (res, fold, align) ->
                { res with
                    RnaFoldResult = fold
                    Allignments = align})

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

module gRNA.SpacerFinder

open System.Threading.Tasks
open gRNA.RNAFoldWrapper

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
    let pattern = "(A{4,}|C{4,}|G{4,}|T{4,})"
    System.Text.RegularExpressions.Regex.Matches(sequence, pattern).Count


type gRNAResult =
    { Sequence: string
      GCScore: float
      HomopolymerCount: int
      SeedRegion: string
      Allignments: int
      RnaFoldResult: RNAFoldResult
      Rank: int
      Score: float
      MutationHighlightStart: int
      MutationHighlightLength: int }

let getgRNAResult sequence : gRNAResult =
    let gcContent = calculateGCContent sequence
    let gcScore = calculateGCScore gcContent (40.0, 60.0)
    let homopolymerCount = countHomopolymers sequence
    let seedRegion = sequence[10..17]

    { Sequence = sequence
      GCScore = gcScore
      HomopolymerCount = homopolymerCount
      SeedRegion = seedRegion
      Allignments = 0
      RnaFoldResult = { Structure = ""; Energy = 0.0 }
      Rank = 0
      Score = 0.0
      MutationHighlightStart = -1
      MutationHighlightLength = 0}

let adjustFourthFromEndToAorU (sequence: string) =
    if System.String.IsNullOrEmpty(sequence) || sequence.Length < 4 then
        sequence
    else
        let targetIndex = sequence.Length - 4
        let replacement =
            if sequence.[targetIndex] = 'A' then 'U'
            else 'A'

        sequence.Remove(targetIndex, 1).Insert(targetIndex, string replacement)

let applySubstitutionSpecialRule (windowSize: int) (results: gRNAResult list) =
    let targetMutationIndex = windowSize - 3

    results
    |> List.tryFind (fun result ->
        result.MutationHighlightStart = targetMutationIndex
        && result.MutationHighlightLength > 0)
    |> Option.map (fun selected ->
        let adjustedSequence = adjustFourthFromEndToAorU selected.Sequence
        let adjustedBaseResult = getgRNAResult adjustedSequence

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
        // Anchor visualization for zero-length mutations (e.g. deletion in local mutated sequence)
        if mutationStart >= windowStart && mutationStart < windowEndExclusive then
            let anchor =
                if mutationStart <= windowStart then 0
                elif mutationStart >= windowEndExclusive then windowSize - 1
                else mutationStart - windowStart
            (anchor, 1)
        else
            (-1, 0)

let sortByResult (result: gRNAResult) =
    (result.Allignments, -result.RnaFoldResult.Energy, -result.GCScore, result.HomopolymerCount)

let complementary (sequence: string) =
    sequence
    |> Seq.map (function
        | 'A' -> 'T'
        | 'T' -> 'A'
        | 'C' -> 'G'
        | 'G' -> 'C'
        | c -> c)
    |> Seq.toArray
    |> System.String

let getOrderedgRna (window: int) (sequence: string) (mutationStart: int) (mutationLength: int) (bowtieService: gRNA.Services.BowtieService) (cancellationToken: System.Threading.CancellationToken) : Task<gRNAResult list> =
    task {
        let subsequences = slidingWindow sequence window
        let results =
            subsequences
            |> List.mapi (fun i subsequence ->
                let highlightStart, highlightLength = getMutationHighlightSpan i window mutationStart mutationLength
                let sequenceResult =
                    subsequence
                    |> complementary
                    |> _.Replace('T', 'U')
                    |> getgRNAResult
                { sequenceResult with
                    MutationHighlightStart = highlightStart
                    MutationHighlightLength = highlightLength })
        let gRNASequences = subsequences |> List.map (fun s -> s + "GAUUUAGACUACCCCAAAAACGAAGGGGACUAAAAC")

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

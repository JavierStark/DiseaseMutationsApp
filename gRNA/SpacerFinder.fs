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
      Score: float }

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
      Score = 0.0}

let sortByResult (result: gRNAResult) =
    (result.Allignments, -result.RnaFoldResult.Energy, -result.GCScore, result.HomopolymerCount)

let getOrderedgRna (window: int) (sequence: string) : Task<gRNAResult list> =
    task {
        let subsequences = slidingWindow sequence window
        let results = subsequences |> List.map getgRNAResult
        let gRNASequences = subsequences |> List.map (fun s -> s + "GATTTAGACTACCCCAAAAACGAAGGGGACTAAAAC")

        let! allignments = BowtieWrapper.runBowtieForMultipleSequences subsequences 2 2
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

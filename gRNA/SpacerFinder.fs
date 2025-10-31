namespace gRNA

module SpacerFinder =
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
        if totalCount = 0 then 0.0 else System.Math.Round((float gcCount / float totalCount) * 100.0, 2)
        
    let calculateGCScore (gcContent: float) (lowerThreshold: float, upperThreshold: float) =
        if gcContent < upperThreshold && gcContent > lowerThreshold then
            1.0
        else
            if gcContent < lowerThreshold then
                gcContent/lowerThreshold
            else
                (100.0 - gcContent)/(100.0 - upperThreshold)

    let countHomopolymers (sequence: string) =
        let pattern = "(A{4,}|C{4,}|G{4,}|T{4,})"
        System.Text.RegularExpressions.Regex.Matches(sequence, pattern).Count
        
        
    let getBestgRNA (window: int) (sequence: string) =
        let subsequences = slidingWindow sequence window
        let filteredResults =
            subsequences
            |> List.map (fun subseq ->
                let gcContent = calculateGCContent subseq
                let gcScore = calculateGCScore gcContent (40.0, 60.0)
                let homopolymerCount = countHomopolymers subseq
                let seedRegion = subseq.[10..17]
                (subseq, gcScore, homopolymerCount, seedRegion))
            |> List.sortBy (fun (_, gcScore, homopolymerCount, _) -> (homopolymerCount, -gcScore))
            |> List.take 5
            
        filteredResults
#r "./bin/Debug/net7.0/DiseaseMutationsApp.dll"
#r "nuget: Deedle"

open DiseaseMutationsApp
open Deedle

// Example usage of SequenceRepository
let sequence = SequenceRepository.GetSequence("NG_016465.4") |> Async.AwaitTask |> Async.RunSynchronously

let window = 28

let hgvs = HGVS("NG_016465.4:g.12345A>T")
let mutationLength = hgvs.GetMutationLength()
let struct (mutated, original) = sequence.GetMutatedSubsequence(hgvs, window-mutationLength, window-mutationLength)
printf $"Original Subsequence: %s{original}\n"
printf $"Mutated Subsequence: %s{mutated}\n"


let slidingWindow (input: string) (windowSize: int) =
    if windowSize <= 0 then
        invalidArg "windowSize" "Window size must be greater than 0."
    elif input.Length < windowSize then
        []
    else
        [ for i in 0 .. input.Length - windowSize -> input.Substring(i, windowSize) ]

let list = slidingWindow mutated window
for subseq in list do
    printfn $"%s{subseq}"


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

let results =
    list
    |> List.map (fun subseq ->
        let gcContent = calculateGCContent subseq
        let gcScore = calculateGCScore gcContent (40.0, 60.0)
        let homopolymerCount = countHomopolymers subseq
        let seedRegion = if subseq.Length >= 18 then subseq.[10..17] else "N/A"
        (subseq, gcContent, homopolymerCount, seedRegion, gcScore))
    |> Frame.ofRecords
    
//rename item1 to Subsequence, item2 to GC Content, item3 to Homopolymer Count, item4 to Seed Region
results.RenameColumns(fun colName ->
    match colName with
    | "Item1" -> "Subsequence"
    | "Item2" -> "GC Content"
    | "Item3" -> "Homopolymer Count"
    | "Item4" -> "Seed Region"
    | "Item5" -> "GC Score"
    | _ -> colName)
    
results.Print()


// filter list
let filteredResults =
    list
    |> List.map (fun subseq ->
        let gcContent = calculateGCContent subseq
        let gcScore = calculateGCScore gcContent (40.0, 60.0)
        let homopolymerCount = countHomopolymers subseq
        let seedRegion = subseq.[10..17]
        (subseq, gcScore, homopolymerCount, seedRegion))
    //order  by biggest gcScore and less homopolymerCount
    |> List.sortBy (fun (_, gcScore, homopolymerCount, _) -> (-gcScore, homopolymerCount))
    |> List.take 5
module gRNA.LevenshteinDistance

let rec levenshteinDistance (s1: string) (s2: string) =
        let lenS1 = s1.Length
        let lenS2 = s2.Length
        let d = Array2D.init (lenS1 + 1) (lenS2 + 1) (fun i j -> 0)

        for i in 0 .. lenS1 do
            d.[i, 0] <- i
        for j in 0 .. lenS2 do
            d.[0, j] <- j

        for i in 1 .. lenS1 do
            for j in 1 .. lenS2 do
                let cost = if s1.[i - 1] = s2.[j - 1] then 0 else 1
                d.[i, j] <-
                    List.min [
                        d.[i - 1, j] + 1      // deletion
                        d.[i, j - 1] + 1      // insertion
                        d.[i - 1, j - 1] + cost // substitution
                    ]

        d.[lenS1, lenS2]

let levenshteinSimilarityPercentage (a: string) (b:string) =
    
    let a = a.ToLower()
    let b = b.ToLower()
    
    let distance = levenshteinDistance a b
    let maxLength = max a.Length b.Length
    if maxLength = 0 then 100.0 else (float (maxLength - distance) / float maxLength) * 100.0
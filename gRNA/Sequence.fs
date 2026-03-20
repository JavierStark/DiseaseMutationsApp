module gRNA.Sequence

open gRNA.HGVS

type Sequence(id: string, data: string) =
    member _.Id = id
    member _.Data = data
    
    member this.GetMutatedSubsequence(hgvs: HGVS, ?leftPadding: int, ?rightPadding: int) =
        let leftPadding = defaultArg leftPadding 0
        let rightPadding = defaultArg rightPadding 0
        
        let start = fst hgvs.Position - 1
        let mutable limitLeft = start - leftPadding
        
        let mutable actualStart = start
        let mutable actualLeftPadding = leftPadding
        
        if actualStart < 0 then actualStart <- 0
        if limitLeft < 0 then 
            actualLeftPadding <- 0
            limitLeft <- 0
        
        let end' = snd hgvs.Position
        let mutable limitRight = end' + rightPadding
        
        let mutable actualEnd = end'
        let mutable actualRightPadding = rightPadding
        
        if actualEnd > data.Length then actualEnd <- data.Length
        if limitRight > data.Length then 
            actualRightPadding <- data.Length
            limitRight <- data.Length
        
        let original = data.[limitLeft..limitRight - 1]
        
        let mutated =
            match hgvs.Mutation with
            | MutationType.Substitution ->
                data.[limitLeft..actualStart - 1] + hgvs.Alternate + data.[actualEnd..limitRight - 1]
            | MutationType.Deletion ->
                data.[limitLeft..actualStart - 1] + data.[actualEnd..limitRight - 1]
            | MutationType.Insertion ->
                data.[limitLeft..actualStart] + hgvs.Alternate + data.[actualStart + 1..limitRight - 1]
            | MutationType.Duplication ->
                data.[limitLeft..actualStart - 1] + 
                data.[actualStart..actualEnd - 1] + 
                data.[actualStart..actualEnd - 1] + 
                data.[actualEnd..limitRight - 1]
            | MutationType.NoChange ->
                original
            | MutationType.DeletionInsertion ->
                data.[limitLeft..actualStart - 1] + hgvs.Alternate + data.[actualEnd..limitRight - 1]
            | MutationType.Inversion ->
                let reversed = 
                    data.[actualStart..actualEnd - 1] 
                    |> Seq.rev 
                    |> Seq.toArray 
                    |> System.String
                data.[limitLeft..actualStart - 1] + reversed + data.[actualEnd..limitRight - 1]
            | _ ->
                raise (System.NotImplementedException($"Mutation type {hgvs.Mutation} not implemented"))
        
        
        (mutated, original)
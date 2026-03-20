module gRNA.HGVS

open System
open System.Text.RegularExpressions

type MutationType =
    | Substitution
    | Deletion
    | Insertion
    | Duplication
    | Inversion
    | DeletionInsertion
    | Repeat
    | NoChange
    | Conversion
    | Translocation
    | LargeUnknown

type HGVS(code: string) =
    let positionRangeRegex = Regex(@"^(\d+)(_(\d+))?")
    
    let parseMethod (method: string) =
        if String.IsNullOrEmpty(method) || method = "=" then
            (MutationType.NoChange, "", "", 0)
        elif method.Contains('>') then
            let refs = method.Split('>')
            (MutationType.Substitution, refs.[0], refs.[1], 0)
        elif method.StartsWith("delins") then
            (MutationType.DeletionInsertion, "", method.[6..], 0)
        elif method.StartsWith("del") then
            (MutationType.Deletion, "", "", 0)
        elif method.StartsWith("ins") then
            (MutationType.Insertion, "", method.[3..], 0)
        elif method.StartsWith("dup") then
            (MutationType.Duplication, "", "", 0)
        elif method.StartsWith("inv") then
            (MutationType.Inversion, "", "", 0)
        elif method.Contains('[') && method.Contains(']') then
            let sequence = method.[..method.IndexOf('[') - 1]
            let repeatCountStr = method.[method.IndexOf('[') + 1..method.IndexOf(']') - 1]
            let repeatCount = int repeatCountStr
            (MutationType.Repeat, "", sequence, repeatCount)
        else
            raise (Exception("Invalid HGVS format: Unknown mutation method"))
    
    let parse () =
        let parts = code.Split(':')
        let accession = parts.[0]
        
        let typeAndRest = parts.[1]
        let typeValue = string typeAndRest.[0]
        let positionAndMethod = typeAndRest.[2..]
        
        let positionMatch = positionRangeRegex.Match(positionAndMethod)
        if not positionMatch.Success then
            raise (Exception("Invalid HGVS format: Position not found"))
        
        let positions = positionMatch.Value.Split('_')
        let position =
            if positions.Length = 2 then
                (int positions.[0], int positions.[1])
            else
                (int positions.[0], int positions.[0])
        
        let method = positionAndMethod.[positionMatch.Length..]
        let (mutation, reference, alternate, count) = parseMethod method
        
        (accession, typeValue, position, mutation, reference, alternate, count)
    
    let accession, typeValue, position, mutation, reference, alternate, count = parse()
    
    member _.Code = code
    member _.Accession = accession
    member _.Type = typeValue
    member _.Position = position
    member _.Mutation = mutation
    member _.Reference = reference
    member _.Alternate = alternate
    member _.Count = count
    
    member _.GetMutationLength() =
        let start, end' = position
        end' - start + 1
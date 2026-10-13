module gRNA.HGVS

open System
open System.Text.RegularExpressions
open gRNA

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
        if String.IsNullOrEmpty(method) then
            raise (GrnaInputException("Invalid HGVS format: a position without a change; write the change after it, or '=' for no change"))
        elif method = "=" then
            (MutationType.NoChange, "", "", 0)
        elif method.Contains('>') then
            let refs = method.Split('>')
            (MutationType.Substitution, refs.[0], refs.[1], 0)
        elif method.StartsWith("delins") then
            (MutationType.DeletionInsertion, "", method.[6..], 0)
        // For del/dup/inv an explicit sequence after the keyword (c.215_217delACG) is kept in Reference so it can be
        // checked against the fetched sequence.
        elif method.StartsWith("del") then
            (MutationType.Deletion, method.[3..], "", 0)
        elif method.StartsWith("ins") then
            (MutationType.Insertion, "", method.[3..], 0)
        elif method.StartsWith("dup") then
            (MutationType.Duplication, method.[3..], "", 0)
        elif method.StartsWith("inv") then
            (MutationType.Inversion, method.[3..], "", 0)
        elif method.Contains('[') && method.Contains(']') && method.IndexOf('[') < method.IndexOf(']') then
            let sequence = method.[..method.IndexOf('[') - 1]
            let repeatCountStr = method.[method.IndexOf('[') + 1..method.IndexOf(']') - 1]
            match Int32.TryParse(repeatCountStr, Globalization.NumberStyles.None, Globalization.CultureInfo.InvariantCulture) with
            | true, repeatCount -> (MutationType.Repeat, "", sequence, repeatCount)
            | _ -> raise (GrnaInputException(sprintf "Invalid HGVS format: repeat count '%s' is not a number" repeatCountStr))
        else
            raise (GrnaInputException("Invalid HGVS format: Unknown mutation method"))
    
    let formatHint = "expected ACCESSION:TYPE.CHANGE, for example NC_000017.11:g.7674220C>T"

    let parsePosition (text: string) =
        match Int32.TryParse(text, Globalization.NumberStyles.None, Globalization.CultureInfo.InvariantCulture) with
        | true, n when n >= 1 -> n
        | _ -> raise (GrnaInputException(sprintf "Invalid HGVS format: position '%s' is not a number between 1 and %d" text Int32.MaxValue))

    let isBases (s: string) = s.Length > 0 && s |> Seq.forall (fun c -> c = 'A' || c = 'C' || c = 'G' || c = 'T')

    /// Alleles must be non-empty upper-case A/C/G/T (no IUPAC codes, no lower case).
    let requireBases (what: string) (s: string) =
        if not (isBases s) then
            raise (GrnaInputException(sprintf "Invalid HGVS format: %s '%s' must be upper-case A, C, G or T" what s))

    let parse () =
        if String.IsNullOrWhiteSpace code then
            raise (GrnaInputException("Invalid HGVS format: the variant is empty; " + formatHint))
        let parts = code.Split(':')
        if parts.Length <> 2 || parts.[0].Length = 0 || parts.[1].Length < 3 || parts.[1].[1] <> '.' then
            raise (GrnaInputException(sprintf "Invalid HGVS format '%s': %s" code formatHint))
        let accession = parts.[0]

        let typeAndRest = parts.[1]
        let typeValue = string typeAndRest.[0]
        let positionAndMethod = typeAndRest.[2..]

        let positionMatch = positionRangeRegex.Match(positionAndMethod)
        if not positionMatch.Success then
            raise (GrnaInputException("Invalid HGVS format: Position not found"))

        let positions = positionMatch.Value.Split('_')
        let position =
            if positions.Length = 2 then
                (parsePosition positions.[0], parsePosition positions.[1])
            else
                (parsePosition positions.[0], parsePosition positions.[0])
        if fst position > snd position then
            raise (GrnaInputException(sprintf "Invalid HGVS format: the range %d_%d is reversed" (fst position) (snd position)))

        let method = positionAndMethod.[positionMatch.Length..]
        let (mutation, reference, alternate, count) = parseMethod method

        match mutation with
        | MutationType.Substitution ->
            requireBases "reference base" reference
            requireBases "alternate base" alternate
            if reference.Length <> 1 || alternate.Length <> 1 then
                raise (GrnaInputException(sprintf "Invalid HGVS format: a substitution changes one base ('%s>%s')" reference alternate))
            if reference = alternate then
                raise (GrnaInputException(sprintf "Invalid HGVS format: '%s>%s' does not change the sequence" reference alternate))
        | MutationType.Insertion | MutationType.DeletionInsertion ->
            requireBases "inserted sequence" alternate
        | MutationType.Deletion | MutationType.Duplication | MutationType.Inversion when reference <> "" ->
            requireBases "stated sequence" reference
        | _ -> ()

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
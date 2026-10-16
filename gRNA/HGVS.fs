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

// ---------------------------------------------------------------------------------------------------------------
// Coding-sequence (c.) positions on a transcript record
// ---------------------------------------------------------------------------------------------------------------

/// `Some (accession, text after "c.")` when the code is a c. notation, else None.
let tryParseCoding (code: string) : (string * string) option =
    let m = Regex.Match(code.Trim(), @"^([^:]+):c\.(.+)$")
    if m.Success then Some(m.Groups.[1].Value, m.Groups.[2].Value) else None

/// c. positions can be converted only on a transcript record (NM_ / XM_, a protein-coding mRNA), whose sequence has no introns,
/// so a c. position is just an offset from the start of the coding sequence. On a genomic record (NG_, NC_) the exons are
/// separated by introns and the conversion would need the exon map, which Diana does not read yet.
let requireCodingTranscript (accession: string) : unit =
    if not (accession.StartsWith "NM_" || accession.StartsWith "XM_") then
        raise (GrnaInputException(sprintf "c. positions can be converted only on a transcript record such as NM_000518.5 (a protein-coding mRNA), not on '%s'. Use the NM_ accession of the transcript, or genomic (g.) coordinates, for example the g. notations an rsID resolves to." accession))

let private codingPosition = Regex(@"^([-*]?)(\d+)([+-]\d+)?")

/// Rewrites `NM_x:c.20A>T` as `NM_x:n.70A>T`, given the 1-based first and last position of the CDS in the transcript.
/// c.N is cdsStart + N - 1; c.-N is cdsStart - N (5' UTR); c.*N is cdsEnd + N (3' UTR). Intronic offsets (c.123+4) do not
/// exist in an mRNA and are refused. A code that is not a c. notation is returned unchanged.
let convertCodingToTranscript (cdsStart: int) (cdsEnd: int) (code: string) : string =
    match tryParseCoding code with
    | None -> code
    | Some(accession, body) ->
        requireCodingTranscript accession
        let convert (text: string) : int * string =
            let m = codingPosition.Match text
            if not m.Success then
                raise (GrnaInputException(sprintf "Invalid HGVS format: c. position not found in '%s'" text))
            if m.Groups.[3].Success then
                raise (GrnaInputException(sprintf "'%s' has an intronic offset (c.%s). A transcript record has no introns, so such a position cannot be placed on it; use genomic (g.) coordinates." code m.Value))
            let n = Int32.Parse(m.Groups.[2].Value, Globalization.CultureInfo.InvariantCulture)
            if n < 1 then raise (GrnaInputException(sprintf "Invalid HGVS format: c.%s (c. positions start at 1; c.-1 is the base before the start codon)" m.Value))
            let position =
                match m.Groups.[1].Value with
                | "-" -> cdsStart - n
                | "*" -> cdsEnd + n
                | _ -> cdsStart + n - 1
            if position < 1 then raise (GrnaInputException(sprintf "c.%s is before the start of %s." m.Value accession))
            position, text.Substring m.Length
        let first, afterFirst = convert body
        if afterFirst.StartsWith "_" then
            let second, rest = convert (afterFirst.Substring 1)
            sprintf "%s:n.%d_%d%s" accession first second rest
        else
            sprintf "%s:n.%d%s" accession first afterFirst

/// Pure parsing of the text produced by the child processes. No I/O, fully unit-testable.
module gRNA.Parsing

open System

/// One Bowtie alignment line: read index, strand, reference, 0-based offset, matched sequence.
type BowtieAlignment =
    { ReadIndex: int
      Strand: char
      Reference: string
      Offset: int64
      Sequence: string
      /// Number of mismatches against the reference (0 when Bowtie prints no mismatch column).
      MismatchCount: int
      /// Bowtie's mismatch descriptor, e.g. "21:A>G,25:A>G", or "".
      MismatchDetail: string }

let private unquote (s: string) =
    let t = s.Trim()
    if t.Length >= 2 && ((t.StartsWith "'" && t.EndsWith "'") || (t.StartsWith "\"" && t.EndsWith "\"")) then
        t.Substring(1, t.Length - 2)
    else
        t

/// Parses one fold result line. Accepts the library's own `structure<TAB>energy` format as well as
/// the legacy Python reprs `['structure', energy]` and `('structure', energy)`.
let parseFoldLine (line: string) : string * float =
    let fail reason =
        raise (GrnaUpstreamException(sprintf "Failed to parse ViennaRNA output '%s': %s" line reason))

    let trimmed = line.Trim()

    if trimmed.Contains '\t' then
        let idx = trimmed.IndexOf '\t'
        let structure = trimmed.Substring(0, idx).Trim()
        let energyText = trimmed.Substring(idx + 1).Trim()
        match Double.TryParse(energyText, Globalization.NumberStyles.Float, Globalization.CultureInfo.InvariantCulture) with
        | true, e -> structure, e
        | _ -> fail "energy is not a number"
    else
        let inner =
            if (trimmed.StartsWith "[" && trimmed.EndsWith "]") || (trimmed.StartsWith "(" && trimmed.EndsWith ")") then
                trimmed.Substring(1, trimmed.Length - 2)
            else
                trimmed

        let comma = inner.LastIndexOf ','
        if comma = -1 then
            fail "no separator found"
        else
            let structure = unquote (inner.Substring(0, comma))
            let energyText = inner.Substring(comma + 1).Trim()
            match Double.TryParse(energyText, Globalization.NumberStyles.Float, Globalization.CultureInfo.InvariantCulture) with
            | true, e -> structure, e
            | _ -> fail "energy is not a number"

/// Parses a batch: one result per non-empty line.
let parseFoldBatch (output: string) : (string * float) list =
    output.Split([| '\n'; '\r' |], StringSplitOptions.RemoveEmptyEntries)
    |> Array.map parseFoldLine
    |> List.ofArray

/// Parses a single Bowtie stdout line; None for blank/summary (`#`) lines.
let tryParseBowtieLine (line: string) : BowtieAlignment option =
    let line = line.Trim()
    if String.IsNullOrWhiteSpace line || line.StartsWith "#" then
        None
    else
        let f = line.Split '\t'
        if f.Length < 5 then
            None
        else
            match Int32.TryParse f[0], Int64.TryParse f[3] with
            | (true, idx), (true, off) when f[1].Length = 1 ->
                let detail = if f.Length > 7 then f[7].Trim() else ""
                let mismatches = if detail = "" then 0 else detail.Split(',').Length
                Some { ReadIndex = idx; Strand = f[1].[0]; Reference = f[2]; Offset = off; Sequence = f[4]
                       MismatchCount = mismatches; MismatchDetail = detail }
            | _ -> None

/// Parses Bowtie output (stdout, optionally followed by stderr summary) into alignments.
let parseBowtieAlignments (output: string) : BowtieAlignment list =
    output.Split('\n')
    |> Array.choose tryParseBowtieLine
    |> List.ofArray

/// Counts alignments per input read, returning a list of `readCount` integers.
let countAlignments (readCount: int) (alignments: BowtieAlignment list) : int list =
    let counts = alignments |> List.countBy _.ReadIndex |> dict
    [ for i in 0 .. readCount - 1 ->
        match counts.TryGetValue i with
        | true, c -> c
        | _ -> 0 ]

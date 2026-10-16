/// The CSV report written by the web app's Builder export and the `grna design --format csv` command,
/// and read by the Pooling page. One definition so the producers and the parser cannot drift apart.
module gRNA.ReportSchema

open System
open System.Globalization

let columns =
    [ "RS ID"; "HGVS"; "Sequence Type"; "Rank"; "Sequence"; "Score"; "GC Content"
      "Alignments"; "Seed Region"; "Homopolymers"; "Fold Energy"; "Strand"
      "WT Mismatches"; "Engineered"; "Scaffold Intact"; "Spacer Unpaired" ]

let header = String.Join(",", columns)

let mutatedType = "Mutated"
let originalType = "Original"
let normalStrand = "Normal"
let complementStrand = "Complement"

/// Spreadsheets run a field that starts with = + - @ (or a tab / carriage return) as a formula. Such a field is
/// prefixed with an apostrophe, the convention spreadsheets treat as "text" and hide on display (OWASP CSV injection).
let neutralise (value: string) =
    if value.Length > 0 && "=+-@\t\r".IndexOf(value.[0]) >= 0 then "'" + value else value

/// Quotes a field only when it contains a comma, quote or newline, after neutralising formula characters.
let csvField (value: string) =
    let value = neutralise value
    if value.IndexOfAny [| ','; '"'; '\n'; '\r' |] < 0 then value
    else "\"" + value.Replace("\"", "\"\"") + "\""

/// Splits one CSV line into fields, honouring quoted fields ("a,b") and doubled quotes inside them.
let splitCsvLine (line: string) : string list =
    let fields = ResizeArray<string>()
    let current = System.Text.StringBuilder()
    let mutable inQuotes = false
    let mutable i = 0
    while i < line.Length do
        let c = line.[i]
        if inQuotes then
            if c = '"' && i + 1 < line.Length && line.[i + 1] = '"' then
                current.Append '"' |> ignore
                i <- i + 1
            elif c = '"' then
                inQuotes <- false
            else
                current.Append c |> ignore
        elif c = '"' then
            inQuotes <- true
        elif c = ',' then
            fields.Add(current.ToString())
            current.Clear() |> ignore
        else
            current.Append c |> ignore
        i <- i + 1
    fields.Add(current.ToString())
    List.ofSeq fields

let private inv (x: float) = x.ToString(CultureInfo.InvariantCulture)

/// One report row for a ranked candidate. Numbers use the invariant culture.
let row (rsId: string option) (hgvs: string) (sequenceType: string) (isComplement: bool) (g: SpacerFinder.gRNAResult) =
    String.Join(
        ",",
        [ csvField (defaultArg rsId "")
          csvField hgvs
          sequenceType
          string g.Rank
          g.Sequence
          inv g.Score
          inv g.GCContent
          string g.Allignments
          g.SeedRegion
          string g.HomopolymerCount
          inv g.RnaFoldResult.Energy
          (if isComplement then complementStrand else normalStrand)
          (if g.WtMismatches < 0 then "" else string g.WtMismatches)
          (if g.Engineered then "Yes" else "No")
          (if g.ScaffoldIntact then "Yes" else "No")
          (if g.SpacerUnpaired < 0.0 then "" else inv (Math.Round(g.SpacerUnpaired, 2))) ]
    )

/// A leading comment line recording the parameters so a report can be reproduced.
let provenance (spacer: int) (seedStart: int) (seedEnd: int) =
    // After the three parameters the line records the fixed choices behind the numbers, so a report can be reproduced and read later.
    sprintf "# Diana report; spacer=%d; seed=%d-%d; generated=%s; gc-score-range=40-60; special-rule=variant at N-3, mismatch at N-4; scaffold=%d nt; off-target index=GRCh38 genome (not a transcriptome)"
        spacer seedStart seedEnd (DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")) SpacerFinder.scaffold.Length

/// Reads spacer and seed back from a provenance line written by `provenance`; None when the line is not one.
let tryParseProvenance (line: string) : (int * int * int) option =
    let m = System.Text.RegularExpressions.Regex.Match(line, @"^#\s*Diana report;\s*spacer=(\d+);\s*seed=(\d+)-(\d+)")
    if m.Success then Some(int m.Groups.[1].Value, int m.Groups.[2].Value, int m.Groups.[3].Value) else None

/// The CSV report written by the web app's Builder export and the `grna design --format csv` command,
/// and read by the Pooling page. One definition so the producers and the parser cannot drift apart.
module gRNA.ReportSchema

open System
open System.Globalization

let columns =
    [ "RS ID"; "HGVS"; "Sequence Type"; "Rank"; "Sequence"; "Score"; "GC Content"
      "Alignments"; "Seed Region"; "Homopolymers"; "Fold Energy"; "Strand" ]

let header = String.Join(",", columns)

let mutatedType = "Mutated"
let originalType = "Original"
let normalStrand = "Normal"
let complementStrand = "Complement"

/// Quotes a field only when it contains a comma, quote or newline.
let csvField (value: string) =
    if value.IndexOfAny [| ','; '"'; '\n'; '\r' |] < 0 then value
    else "\"" + value.Replace("\"", "\"\"") + "\""

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
          (if isComplement then complementStrand else normalStrand) ]
    )

/// A leading comment line recording the parameters so a report can be reproduced.
let provenance (spacer: int) (seedStart: int) (seedEnd: int) =
    sprintf "# Diana report; spacer=%d; seed=%d-%d; generated=%s" spacer seedStart seedEnd (DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"))

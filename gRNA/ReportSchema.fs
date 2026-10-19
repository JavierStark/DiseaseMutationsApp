/// The CSV report written by the web app's Builder export and the `grna design --format csv` command,
/// and read by the Pooling page. One definition so the producers and the parser cannot drift apart.
module gRNA.ReportSchema

open System
open System.Globalization

let columns =
    [ "RS ID"; "HGVS"; "Sequence Type"; "Rank"; "Sequence"; "Score"; "GC Content"
      "Offtargets"; "Seed Region"; "Homopolymers"; "Fold Energy"; "Strand"
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
          string g.Offtargets
          g.SeedRegion
          string g.HomopolymerCount
          inv g.RnaFoldResult.Energy
          (if isComplement then complementStrand else normalStrand)
          (if g.WtMismatches < 0 then "" else string g.WtMismatches)
          (if g.Engineered then "Yes" else "No")
          (if g.ScaffoldIntact then "Yes" else "No")
          (if g.SpacerUnpaired < 0.0 then "" else inv (Math.Round(g.SpacerUnpaired, 2))) ]
    )

/// How the direct repeat is written in a provenance line: "none" for the spacer alone, otherwise its RNA sequence.
let repeatToken (repeat: string) = if repeat = "" then "none" else repeat

/// How a rule is written in a provenance line, e.g. "variant at N-9, mismatch at N-10 and N-11, bases A>A G>A C>C U>C".
let ruleToken (rule: SpacerFinder.SpecialRuleOptions) =
  if not rule.Enabled then "off" else
    let positions = SpacerFinder.mismatchPositions rule |> List.map (sprintf "N-%d") |> String.concat " and "
    let bases =
        try SpacerFinder.parseEngineeredBases rule.EngineeredBases |> Map.toList |> List.map (fun (t, g) -> sprintf "%c>%c" t g) |> String.concat " "
        with _ -> "(older report: the letter was flipped to A, or U)"
    sprintf "variant at N-%d, mismatch at %s, bases %s" rule.SnpPos positions bases

/// A leading comment line recording the parameters so a report can be reproduced. `repeat` is the 5' context the
/// spacers were folded with while screening ("" = the spacer alone).
let provenanceWithRuleAndRepeat (rule: SpacerFinder.SpecialRuleOptions) (repeat: string) (spacer: int) (seedStart: int) (seedEnd: int) =
    // After the three parameters the line records the fixed choices behind the numbers, so a report can be reproduced and read later.
    // "variant at N-9" means the variant is the 9th letter from the spacer's 3' end (N = spacer length).
    sprintf "# Diana report; spacer=%d; seed=%d-%d; generated=%s; gc-score-range=40-60; special-rule=%s; repeat=%s; off-target index=GRCh38 genome (not a transcriptome)"
        spacer seedStart seedEnd (DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")) (ruleToken rule) (repeatToken repeat)

/// The provenance line with the default direct repeat (the 36 nt Cas13 scaffold).
let provenanceWithRule (rule: SpacerFinder.SpecialRuleOptions) (spacer: int) (seedStart: int) (seedEnd: int) =
    provenanceWithRuleAndRepeat rule SpacerFinder.scaffold spacer seedStart seedEnd

/// The provenance line with the default rule (variant 9th, mismatches 10th and 11th from the 3' end).
let provenanceWithRepeat (repeat: string) (spacer: int) (seedStart: int) (seedEnd: int) =
    provenanceWithRuleAndRepeat SpacerFinder.defaultSpecialRule repeat spacer seedStart seedEnd

/// The provenance line with the default rule and the default direct repeat.
let provenance (spacer: int) (seedStart: int) (seedEnd: int) =
    provenanceWithRepeat SpacerFinder.scaffold spacer seedStart seedEnd

/// Reads the rule back from a provenance line. Reports made before the rule was recorded used a single mismatch at the 4th
/// letter with the variant 3rd, and flipped the letter to A (U when already A); those come back as variant 3, mismatch 4,
/// one mismatch and an empty base table, which differs from every current rule.
let tryParseRule (line: string) : SpacerFinder.SpecialRuleOptions option =
    let legacy = { SpacerFinder.defaultSpecialRule with SnpPos = 3; MismatchPos = 4; MismatchCount = 1; EngineeredBases = "" }
    let off = System.Text.RegularExpressions.Regex.IsMatch(line, @"^#\s*Diana report;.*special-rule=off")
    let m = System.Text.RegularExpressions.Regex.Match(line, @"^#\s*Diana report;.*special-rule=variant at N-(\d+), mismatch at N-(\d+)(?: and N-(\d+))?(?:, bases ((?:[ACGU]>[ACGU] ?)+))?")
    if off then Some { SpacerFinder.defaultSpecialRule with Enabled = false }
    elif m.Success then
        Some { SpacerFinder.defaultSpecialRule with
                 SnpPos = int m.Groups.[1].Value
                 MismatchPos = int m.Groups.[2].Value
                 MismatchCount = (if m.Groups.[3].Success then 2 else 1)
                 EngineeredBases = (if m.Groups.[4].Success then m.Groups.[4].Value.Trim() else "") }
    elif line.StartsWith "# Diana report;" then Some legacy
    else None

/// True when two rules put the same letters in the same places (the base tables are compared as tables, not as text).
let sameRule (a: SpacerFinder.SpecialRuleOptions) (b: SpacerFinder.SpecialRuleOptions) =
    let table (r: SpacerFinder.SpecialRuleOptions) =
        try SpacerFinder.parseEngineeredBases r.EngineeredBases |> Map.toList with _ -> []
    a.Enabled = b.Enabled && (not a.Enabled || a.SnpPos = b.SnpPos) && (not a.Enabled || a.MismatchPos = b.MismatchPos && a.MismatchCount = b.MismatchCount && table a = table b)

/// Reads spacer and seed back from a provenance line written by `provenance`; None when the line is not one.
let tryParseProvenance (line: string) : (int * int * int) option =
    let m = System.Text.RegularExpressions.Regex.Match(line, @"^#\s*Diana report;\s*spacer=(\d+);\s*seed=(\d+)-(\d+)")
    if m.Success then Some(int m.Groups.[1].Value, int m.Groups.[2].Value, int m.Groups.[3].Value) else None

/// Reads the direct repeat back from a provenance line: Some "" for "none", Some sequence otherwise. Reports written before
/// the repeat was recorded (they say "scaffold=36 nt") were all made with the default one.
let tryParseRepeat (line: string) : string option =
    if not (System.Text.RegularExpressions.Regex.IsMatch(line, @"^#\s*Diana report;")) then None
    else
        let m = System.Text.RegularExpressions.Regex.Match(line, @"repeat=(none|[ACGU]+)")
        if m.Success then Some(if m.Groups.[1].Value = "none" then "" else m.Groups.[1].Value)
        else Some SpacerFinder.scaffold

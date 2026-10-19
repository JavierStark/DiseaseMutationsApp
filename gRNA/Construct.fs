/// The "complete guide": what is wrapped around a spacer before it is ordered or exported.
/// <c>[BioBricks 5'][Frida 5'][custom 5'][DR] SPACER [custom 3'][Frida 3'][BioBricks 3']</c>, optionally behind a T7 promoter.
/// Everything is held as RNA letters (U); the adapters are DNA by origin and converted on use. The web app's
/// <c>GuideConstruct</c> delegates here, so the web app, the `grna` CLI and the exports share one definition.
module gRNA.Construct

/// A direct repeat (the constant handle before the spacer) the user can pick.
type DrPreset = { Id: string; Name: string; Sequence: string }

/// What wraps the spacer. `Custom5` sits 5' of the DR/spacer and `Custom3` 3' of the spacer, both inside the adapters.
type ConstructOptions =
    { DrId: string
      Frida: bool
      BioBricks: bool
      Custom5: string
      Custom3: string }

type PartKind =
    | BioBricks
    | Frida
    | Custom
    | DirectRepeat
    | Spacer

/// One piece of the complete guide (`Rna` uses U). The labels are relied on by the restriction-site accounting.
type Part = { Kind: PartKind; Label: string; Rna: string }

[<Literal>]
let LwaId = "lwa"

[<Literal>]
let NoneId = "none"

[<Literal>]
let Frida5 = "AGAGCGTCTCTAAAC"

[<Literal>]
let Frida3 = "GCTTAGAGACGTACT"

[<Literal>]
let BioBricks5 = "GGCTAGCGAATTCGCGGCCGCTTCT"

[<Literal>]
let BioBricks3 = "AGTAGCGGCCGCTGCAGCGATCGT"

/// T7 class III promoter up to (not including) the +1 base. The Cas13 scaffold begins with G, which becomes the +1
/// transcription start, so the promoter is followed directly by the guide's DNA.
[<Literal>]
let T7Promoter = "TAATACGACTCACTATA"

/// Add further Cas13 direct repeats here (data only) once their sequence is verified.
let drPresets : DrPreset list =
    [ { Id = LwaId; Name = "LwaCas13a"; Sequence = SpacerFinder.scaffold }
      { Id = NoneId; Name = "None (no direct repeat)"; Sequence = "" } ]

let defaultOptions : ConstructOptions =
    { DrId = LwaId; Frida = false; BioBricks = false; Custom5 = ""; Custom3 = "" }

/// Uppercase, whitespace removed.
let clean (s: string) =
    if isNull s then "" else System.String(s.ToCharArray() |> Array.filter (fun c -> not (System.Char.IsWhiteSpace c))).ToUpperInvariant()

let toRna (s: string) = (clean s).Replace('T', 'U')

/// True when the text is empty or only A, C, G, T, U (case and spaces ignored).
let isValidCustom (s: string) = clean s |> Seq.forall (fun c -> "ACGTU".Contains c)

let dr (o: ConstructOptions) =
    drPresets |> List.tryFind (fun p -> p.Id = o.DrId) |> Option.defaultValue drPresets.Head

/// The ordered pieces of the complete guide; empty pieces and invalid custom text are left out.
let parts (spacerRna: string) (o: ConstructOptions) : Part list =
    let d = dr o
    let c5 = if isValidCustom o.Custom5 then toRna o.Custom5 else ""
    let c3 = if isValidCustom o.Custom3 then toRna o.Custom3 else ""
    let optional on kind label rna = if on && rna <> "" then [ { Kind = kind; Label = label; Rna = rna } ] else []
    List.concat
        [ optional o.BioBricks BioBricks "BioBricks adapter 5'" (toRna BioBricks5)
          optional o.Frida Frida "Frida adapter 5'" (toRna Frida5)
          optional true Custom "Custom 5'" c5
          optional true DirectRepeat (d.Name + " DR") (toRna d.Sequence)
          [ { Kind = Spacer; Label = "Spacer"; Rna = toRna spacerRna } ]
          optional true Custom "Custom 3'" c3
          optional o.Frida Frida "Frida adapter 3'" (toRna Frida3)
          optional o.BioBricks BioBricks "BioBricks adapter 3'" (toRna BioBricks3) ]

/// The complete guide as RNA letters.
let build (spacerRna: string) (o: ConstructOptions) = parts spacerRna o |> List.map _.Rna |> String.concat ""

/// Short description for file headers, e.g. "LwaCas13a DR + Frida + BioBricks".
let describe (o: ConstructOptions) =
    let d = dr o
    [ yield (if d.Sequence.Length > 0 then d.Name + " DR" else "no DR")
      if o.Frida then yield "Frida"
      if o.BioBricks then yield "BioBricks"
      if (clean o.Custom5).Length > 0 && isValidCustom o.Custom5 then yield "custom 5'"
      if (clean o.Custom3).Length > 0 && isValidCustom o.Custom3 then yield "custom 3'" ]
    |> String.concat " + "

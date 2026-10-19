/// Exports of the complete guide (what would be ordered) as GenBank flat files and SBOL 3 documents. Pure functions: no
/// I/O, no native tools, no network, so the web app, the `grna export` command and any program using the library get
/// byte-identical files. Both writers read one `layout`, so their coordinates cannot disagree.
module gRNA.Export

open System
open System.Globalization
open System.IO
open System.Text
open System.Xml

/// One guide to export. `Spacer` uses RNA or DNA letters. Rank, score and offtargets are written when known.
type ExportGuide =
    { Hgvs: string
      RsId: string option
      Complement: bool
      Spacer: string
      Rank: int option
      Score: float option
      Offtargets: int option }

type ExportOptions =
    { Construct: Construct.ConstructOptions
      /// Put the T7 promoter in front, as the oligo order does.
      IncludeT7: bool
      /// SBOL namespace (an absolute http/https URL) that every identity starts with.
      Namespace: string
      /// Date written in the GenBank LOCUS line; passed in so output is deterministic.
      Date: DateTime }

/// The namespace used when none is given: the team's own wiki address. Set your own with `--namespace`.
[<Literal>]
let DefaultNamespace = "https://2026.igem.wiki/uma-malaga/diana"

let defaultOptions =
    { Construct = Construct.defaultOptions
      IncludeT7 = false
      Namespace = DefaultNamespace
      Date = DateTime.UtcNow }

type SegmentRole =
    | T7Promoter
    | Adapter
    | CustomFlank
    | DirectRepeat
    | SpacerRegion

/// A stretch of the exported DNA, 1-based and inclusive at both ends.
type Segment =
    { Role: SegmentRole
      Label: string
      Start: int
      End: int
      Dna: string }

/// Letters of the spacer as DNA; refuses empty text or anything but A, C, G, T, U.
let private spacerDna (g: ExportGuide) =
    let s = Construct.clean g.Spacer
    if s = "" then raise (GrnaInputException "A guide has no spacer sequence to export.")
    if s |> Seq.exists (fun c -> not ("ACGTU".Contains c)) then
        raise (GrnaInputException(sprintf "The spacer of %s has letters other than A, C, G, T, U." g.Hgvs))
    s.Replace('U', 'T')

let private toDna (rna: string) = rna.Replace('U', 'T')

/// The exported DNA as consecutive segments: the T7 promoter (when on), then the pieces of the complete guide.
let layout (o: ExportOptions) (g: ExportGuide) : Segment list =
    let spacer = spacerDna g
    let pieces =
        [ if o.IncludeT7 then yield T7Promoter, "T7 promoter", Construct.T7Promoter
          for p in Construct.parts spacer o.Construct do
              let role =
                  match p.Kind with
                  | Construct.BioBricks | Construct.Frida -> Adapter
                  | Construct.Custom -> CustomFlank
                  | Construct.DirectRepeat -> DirectRepeat
                  | Construct.Spacer -> SpacerRegion
              yield role, p.Label, toDna p.Rna ]
    let mutable at = 1
    [ for role, label, dna in pieces ->
          let seg = { Role = role; Label = label; Start = at; End = at + dna.Length - 1; Dna = dna }
          at <- at + dna.Length
          seg ]

/// The whole exported DNA (lower-level helper for tests and callers that want the plain sequence).
let sequence (o: ExportOptions) (g: ExportGuide) = layout o g |> List.map _.Dna |> String.concat ""

// ---- shared text helpers -----------------------------------------------------------------------------

let private inv (x: float) = x.ToString("0.##", CultureInfo.InvariantCulture)

/// One line of printable ASCII: control characters become spaces, other characters '?'.
let private oneLine (s: string) =
    let chars =
        (if isNull s then "" else s)
        |> Seq.map (fun c -> if c < ' ' || c = '\127' then ' ' elif c > '~' then '?' else c)
        |> Seq.toArray
    String(chars).Trim()

let private strandName (g: ExportGuide) = if g.Complement then "complement strand" else "normal strand"

/// What the file says about where the guide came from, one fact per entry.
let private facts (g: ExportGuide) =
    [ yield "Variant", oneLine g.Hgvs
      match g.RsId with
      | Some r when r.Trim() <> "" -> yield "dbSNP", oneLine (if r.StartsWith("rs", StringComparison.OrdinalIgnoreCase) then r else "rs" + r)
      | _ -> ()
      yield "Strand", strandName g
      yield "Spacer (5' to 3', RNA)", (Construct.toRna g.Spacer)
      match g.Rank with
      | Some r -> yield "Rank", string r
      | None -> ()
      match g.Score with
      | Some s -> yield "Score", inv s + " (relative rank, not a prediction of activity)"
      | None -> ()
      match g.Offtargets with
      | Some o -> yield "Offtargets (GRCh38, up to 2 mismatches, 6 = 6 or more)", string o
      | None -> () ]

let private notValidated = "Computational design made by Diana; its activity has not been validated experimentally."

let private roleNote (s: Segment) =
    match s.Role with
    | T7Promoter -> "T7 RNA polymerase promoter; transcription starts at the next base (+1)"
    | Adapter -> "Engineered adapter added by Diana"
    | CustomFlank -> "Custom sequence typed by the user"
    | DirectRepeat -> "Cas13 direct repeat (the constant handle before the spacer)"
    | SpacerRegion -> "Targeting sequence: the reverse complement of the target window"

let private describeGuide (o: ExportOptions) (g: ExportGuide) =
    sprintf "Diana Cas13 guide construct for %s (%s); %s%s" (oneLine g.Hgvs) (strandName g) (Construct.describe o.Construct)
        (if o.IncludeT7 then " behind a T7 promoter" else "")

// ---- GenBank -----------------------------------------------------------------------------------------

/// Wraps text at spaces to `width` columns; a single word longer than the width is split.
let private wrap (width: int) (text: string) : string list =
    let words = text.Split([| ' ' |], StringSplitOptions.RemoveEmptyEntries) |> List.ofArray
    let lines = ResizeArray<string>()
    let current = StringBuilder()
    let flush () =
        if current.Length > 0 then
            lines.Add(current.ToString())
            current.Clear() |> ignore
    for w in words do
        let mutable word = w
        while word.Length > width do
            flush ()
            lines.Add(word.Substring(0, width))
            word <- word.Substring width
        if current.Length > 0 && current.Length + 1 + word.Length > width then flush ()
        if current.Length > 0 then current.Append ' ' |> ignore
        current.Append word |> ignore
    flush ()
    if lines.Count = 0 then [ "" ] else List.ofSeq lines

let private qualifier (name: string) (value: string) : string list =
    // Quotes are doubled inside a quoted value; qualifiers start at column 22 and no line passes column 79.
    let quoted = (oneLine value).Replace("\"", "\"\"")
    let lines = wrap (79 - 21 - name.Length - 3) quoted
    let indent = String(' ', 21)
    lines
    |> List.mapi (fun i l ->
        let start = if i = 0 then indent + "/" + name + "=\"" else indent
        start + l + (if i = lines.Length - 1 then "\"" else ""))

let private featureLines (key: string) (location: string) (quals: string list) =
    [ yield "     " + key.PadRight 16 + location
      yield! quals ]

let private upperMonth (d: DateTime) = d.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture).ToUpperInvariant()

let private genBankRecord (o: ExportOptions) (index: int) (g: ExportGuide) : string =
    let segments = layout o g
    let dna = segments |> List.map _.Dna |> String.concat ""
    let sb = StringBuilder()
    let line (s: string) = sb.Append(s).Append('\n') |> ignore
    let labelled (label: string) (text: string) =
        match wrap 67 text with
        | first :: rest ->
            line ((label.PadRight 12) + first)
            for r in rest do line (String(' ', 12) + r)
        | [] -> line label

    line (sprintf "LOCUS       %-16s %11d bp    DNA     linear   SYN %s" (sprintf "Diana_%03d" index) dna.Length (upperMonth o.Date))
    labelled "DEFINITION" (describeGuide o g + ".")
    line "KEYWORDS    ."
    line "SOURCE      synthetic construct"
    line "  ORGANISM  synthetic construct"
    line "            other sequences; artificial sequences."
    let comment = [ yield notValidated; for k, v in facts g -> sprintf "%s: %s." k v ]
    labelled "COMMENT" (comment.Head)
    for c in comment.Tail do
        for l in wrap 67 c do line (String(' ', 12) + l)
    line "FEATURES             Location/Qualifiers"
    for l in featureLines "source" (sprintf "1..%d" dna.Length)
                 [ yield! qualifier "organism" "synthetic construct"
                   yield! qualifier "mol_type" "other DNA"
                   yield! qualifier "db_xref" "taxon:32630" ] do
        line l
    for s in segments do
        let loc = sprintf "%d..%d" s.Start s.End
        let note = roleNote s
        let lines =
            match s.Role with
            | T7Promoter ->
                featureLines "regulatory" loc
                    [ yield "                     /regulatory_class=promoter"
                      yield! qualifier "label" s.Label
                      yield! qualifier "note" note ]
            | DirectRepeat ->
                featureLines "repeat_region" loc
                    [ yield "                     /rpt_type=direct"
                      yield! qualifier "label" s.Label
                      yield! qualifier "note" note ]
            | _ ->
                featureLines "misc_feature" loc
                    [ yield! qualifier "label" s.Label
                      yield! qualifier "note" note ]
        for l in lines do line l
    line "ORIGIN"
    let lower = dna.ToLowerInvariant()
    for start in 0 .. 60 .. lower.Length - 1 do
        let chunk = lower.Substring(start, min 60 (lower.Length - start))
        let groups = [ for i in 0 .. 10 .. chunk.Length - 1 -> chunk.Substring(i, min 10 (chunk.Length - i)) ]
        line (sprintf "%9d %s" (start + 1) (String.Join(" ", groups)))
    line "//"
    sb.ToString()

/// GenBank flat file, one record per guide (records are separated by the `//` terminator, as in any multi-record file).
let toGenBank (o: ExportOptions) (guides: ExportGuide list) : string =
    if guides.IsEmpty then raise (GrnaInputException "There is no guide to export.")
    guides |> List.mapi (fun i g -> genBankRecord o (i + 1) g) |> String.concat ""

// ---- SBOL 3 ------------------------------------------------------------------------------------------

let private rdfNs = "http://www.w3.org/1999/02/22-rdf-syntax-ns#"
let private sbolNs = "http://sbols.org/v3#"
let private xsdInteger = "http://www.w3.org/2001/XMLSchema#integer"
let private identifiers (curie: string) = "https://identifiers.org/" + curie

[<Literal>]
let SboDna = "SBO:0000251"

[<Literal>]
let SoLinear = "SO:0000987"

[<Literal>]
let SoEngineeredRegion = "SO:0000804"

[<Literal>]
let SoDirectRepeat = "SO:0000314"

[<Literal>]
let SoT7Promoter = "SO:0001207"

/// The EDAM term SBOL 3 uses for IUPAC nucleic-acid sequences.
[<Literal>]
let IupacEncoding = "edam:format_1207"

let private soFor (r: SegmentRole) =
    match r with
    | T7Promoter -> SoT7Promoter
    | DirectRepeat -> SoDirectRepeat
    | Adapter | CustomFlank | SpacerRegion -> SoEngineeredRegion

/// Checks and normalises a namespace: an absolute http or https URL without a trailing slash.
let normaliseNamespace (ns: string) : string =
    let t = (if isNull ns then "" else ns.Trim()).TrimEnd('/')
    match Uri.TryCreate(t, UriKind.Absolute) with
    | true, u when (u.Scheme = "http" || u.Scheme = "https") && t.IndexOfAny [| '#'; '?'; ' ' |] < 0 -> t
    | _ -> raise (GrnaInputException(sprintf "'%s' is not a usable namespace: give an absolute http or https URL without spaces, '#' or '?'." ns))

/// A valid SBOL displayId (letters, digits, underscore; not starting with a digit) made from free text.
let displayIdOf (text: string) : string =
    let chars = (oneLine text) |> Seq.map (fun c -> if Char.IsAsciiLetterOrDigit c || c = '_' then c else '_') |> Seq.toArray
    let s = String(chars).Trim('_')
    let s = if s = "" then "guide" else s
    if Char.IsDigit s.[0] then "g_" + s else s

/// One unique displayId per guide, in order; the "_seq" companion of each is reserved too.
let private uniqueIds (guides: ExportGuide list) : string list =
    let used = System.Collections.Generic.HashSet<string>(StringComparer.Ordinal)
    [ for g in guides ->
          let baseId = displayIdOf (g.Hgvs + (if g.Complement then "_C" else ""))
          let mutable id = baseId
          let mutable n = 2
          while used.Contains id || used.Contains(id + "_seq") do
              id <- sprintf "%s_%d" baseId n
              n <- n + 1
          used.Add id |> ignore
          used.Add(id + "_seq") |> ignore
          id ]

/// Characters XML 1.0 can carry; anything else (control characters, lone surrogates) is replaced by a space.
let private xmlSafe (s: string) =
    let t = oneLine s
    String(t.ToCharArray() |> Array.map (fun c -> if XmlConvert.IsXmlChar c then c else ' '))

type private Utf8StringWriter(sb: StringBuilder) =
    inherit StringWriter(sb, CultureInfo.InvariantCulture)
    override _.Encoding = UTF8Encoding(false) :> Encoding

let toSbol3 (o: ExportOptions) (guides: ExportGuide list) : string =
    if guides.IsEmpty then raise (GrnaInputException "There is no guide to export.")
    let ns = normaliseNamespace o.Namespace
    let ids = uniqueIds guides
    let sb = StringBuilder()
    let settings = XmlWriterSettings(Indent = true, IndentChars = "  ", Encoding = UTF8Encoding(false), OmitXmlDeclaration = false, NewLineChars = "\n")
    use sw = new Utf8StringWriter(sb)
    use w = XmlWriter.Create(sw, settings)
    let resource (name: string) (uri: string) =
        w.WriteStartElement("sbol", name, sbolNs)
        w.WriteAttributeString("rdf", "resource", rdfNs, uri)
        w.WriteEndElement()
    let text (name: string) (value: string) = w.WriteElementString("sbol", name, sbolNs, xmlSafe value)
    let integer (name: string) (value: int) =
        w.WriteStartElement("sbol", name, sbolNs)
        w.WriteAttributeString("rdf", "datatype", rdfNs, xsdInteger)
        w.WriteString(string value)
        w.WriteEndElement()
    let about (kind: string) (uri: string) =
        w.WriteStartElement("sbol", kind, sbolNs)
        w.WriteAttributeString("rdf", "about", rdfNs, uri)

    w.WriteStartDocument()
    w.WriteStartElement("rdf", "RDF", rdfNs)
    w.WriteAttributeString("xmlns", "sbol", null, sbolNs)

    List.zip guides ids
    |> List.iter (fun (g, id) ->
        let segments = layout o g
        let dna = segments |> List.map _.Dna |> String.concat ""
        let comp = ns + "/" + id
        let seqUri = comp + "_seq"

        about "Component" comp
        text "displayId" id
        resource "hasNamespace" ns
        text "name" (sprintf "Diana guide for %s%s" g.Hgvs (if g.Complement then " (complement)" else ""))
        text "description" (String.Join(" ", describeGuide o g + ".", notValidated, String.Join(" ", facts g |> List.map (fun (k, v) -> sprintf "%s: %s." k v))))
        resource "type" (identifiers SboDna)
        resource "type" (identifiers SoLinear)
        resource "role" (identifiers SoEngineeredRegion)
        resource "hasSequence" seqUri
        segments
        |> List.iteri (fun i s ->
            let fid = sprintf "feature_%02d" (i + 1)
            w.WriteStartElement("sbol", "hasFeature", sbolNs)
            about "SequenceFeature" (comp + "/" + fid)
            text "displayId" fid
            text "name" s.Label
            text "description" (roleNote s)
            resource "role" (identifiers (soFor s.Role))
            w.WriteStartElement("sbol", "hasLocation", sbolNs)
            about "Range" (comp + "/" + fid + "/range")
            text "displayId" "range"
            integer "start" s.Start
            integer "end" s.End
            resource "orientation" (sbolNs + "inline")
            resource "hasSequence" seqUri
            w.WriteEndElement() // Range
            w.WriteEndElement() // hasLocation
            w.WriteEndElement() // SequenceFeature
            w.WriteEndElement()) // hasFeature
        w.WriteEndElement() // Component

        about "Sequence" seqUri
        text "displayId" (id + "_seq")
        resource "hasNamespace" ns
        text "elements" (dna.ToLowerInvariant())
        resource "encoding" (identifiers IupacEncoding)
        w.WriteEndElement()) // Sequence

    w.WriteEndElement() // RDF
    w.WriteEndDocument()
    w.Flush()
    sw.ToString()

/// File extension and media type for each format, so every front end names its downloads the same way.
let genBankExtension = ".gb"
let sbolExtension = ".xml"

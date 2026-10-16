module gRNA.SNP

open gRNA

open System.Net.Http
open System.Text.Json
open System.Threading.Tasks

let private httpClient = new HttpClient(Timeout = System.TimeSpan.FromSeconds 60.0)

let loadJsonFromUrlAsync (url: string) (cancellationToken: System.Threading.CancellationToken) : Task<string> =
    task {
        try
            return! httpClient.GetStringAsync(url, cancellationToken)
        with
        | :? HttpRequestException as ex ->
            return raise (GrnaUpstreamException(sprintf "NCBI variation lookup failed: %s" ex.Message, ex))
    }

/// One alternative allele of an rsID written on one RefSeqGene (NG_) record, with what dbSNP knows about it.
type SnpVariant =
    { /// Genomic HGVS, for example `NG_000007.3:g.70614A>T`.
      Hgvs: string
      Accession: string
      /// The gene dbSNP places the variant in (for example HBB), when known.
      Gene: string option
      /// True when the gene is read along the + strand of `Accession` (so the "Normal" design targets its mRNA);
      /// False when it is read along the - strand (the "Complement" design targets its mRNA); None when unknown.
      GeneOnPlusStrand: bool option
      /// ClinVar clinical significances dbSNP lists for this allele (for example "pathogenic").
      Significance: string list
      /// True for the default selection: the main RefSeqGene record, and the pathogenic allele(s) when dbSNP names any.
      Preferred: bool }

let private prop (name: string) (e: JsonElement) =
    if e.ValueKind = JsonValueKind.Object then
        match e.TryGetProperty name with
        | true, v -> Some v
        | _ -> None
    else None

let private items (e: JsonElement option) : JsonElement list =
    match e with
    | Some v when v.ValueKind = JsonValueKind.Array -> [ for x in v.EnumerateArray() -> x ]
    | _ -> []

let private text (e: JsonElement option) : string option =
    match e with
    | Some v when v.ValueKind = JsonValueKind.String -> Some(v.GetString())
    | _ -> None

let private flag (e: JsonElement option) : bool =
    match e with
    | Some v when v.ValueKind = JsonValueKind.True -> true
    | _ -> false

/// Pure parser of the dbSNP `refsnp` JSON: every non-reference allele on every NG_ (RefSeqGene) record, with the gene,
/// its orientation and the clinical significance. Allele number i of each placement is allele i of `allele_annotations`.
let parseVariants (json: string) : SnpVariant list =
    use doc = JsonDocument.Parse json
    let snap = prop "primary_snapshot_data" doc.RootElement
    let placements = items (snap |> Option.bind (prop "placements_with_allele"))
    let annotations = items (snap |> Option.bind (prop "allele_annotations"))

    // Per allele index: significances, and the gene with its orientation on the chromosome assembly.
    let significance i =
        annotations
        |> List.tryItem i
        |> Option.map (fun a ->
            items (prop "clinical" a)
            |> List.collect (fun c -> items (prop "clinical_significances" c) |> List.choose (fun s -> text (Some s)))
            |> List.distinct)
        |> Option.defaultValue []
    let geneOnChromosome =
        annotations
        |> List.tryPick (fun a ->
            items (prop "assembly_annotation" a)
            |> List.tryPick (fun asm ->
                items (prop "genes" asm)
                |> List.tryPick (fun g ->
                    match text (prop "locus" g), text (prop "orientation" g) with
                    | Some locus, Some o -> Some(locus, (o = "plus"))
                    | _ -> None)))

    let alleleHgvs (p: JsonElement) = items (prop "alleles" p) |> List.map (fun a -> text (prop "hgvs" a) |> Option.defaultValue "")
    let refBase (p: JsonElement) =
        items (prop "alleles" p)
        |> List.tryPick (fun a ->
            match text (prop "hgvs" a) with
            | Some h when h.EndsWith "=" -> a |> prop "allele" |> Option.bind (prop "spdi") |> Option.bind (prop "inserted_sequence") |> fun v -> text v
            | _ -> None)

    let genomic =
        placements
        |> List.filter (fun p -> (text (prop "seq_id" p) |> Option.defaultValue "").StartsWith "NG_")
    // The mRNA (NM_) placement tells which orientation is the transcript's: the main record is the NG_ one that
    // reads like the mRNA at this base; dbSNP lists the primary RefSeqGene first.
    let mrnaRef =
        placements
        |> List.tryFind (fun p -> (text (prop "seq_id" p) |> Option.defaultValue "").StartsWith "NM_")
        |> Option.bind refBase
    let mainAccession =
        genomic
        |> List.tryFind (fun p -> mrnaRef.IsNone || refBase p = mrnaRef)
        |> Option.orElse (List.tryHead genomic)
        |> Option.bind (fun p -> text (prop "seq_id" p))

    let allVariants =
        [ for p in genomic do
              let accession = text (prop "seq_id" p) |> Option.defaultValue ""
              let opposite = flag (prop "placement_annot" p |> Option.bind (prop "is_aln_opposite_orientation"))
              for (i, h) in alleleHgvs p |> List.indexed do
                  if h <> "" && not (h.EndsWith "=") then
                      let gene, onPlus =
                          match geneOnChromosome with
                          | Some(locus, plusOnChromosome) -> Some locus, Some(plusOnChromosome <> opposite)
                          | None -> None, None
                      yield i, { Hgvs = h; Accession = accession; Gene = gene; GeneOnPlusStrand = onPlus
                                 Significance = significance i; Preferred = false } ]

    let isPathogenic (v: SnpVariant) =
        v.Significance |> List.exists (fun s -> s.Contains "pathogenic" && not (s.Contains "benign"))
    let anyPathogenic = allVariants |> List.exists (snd >> isPathogenic)

    allVariants
    |> List.map (fun (_, v) ->
        { v with Preferred = (Some v.Accession = mainAccession) && (not anyPathogenic || isPathogenic v) })

/// Every NG_ notation of an rsID with its context (gene, strand, significance). Network call.
let getVariantsAsync (rsNumber: string) (cancellationToken: System.Threading.CancellationToken) : Task<SnpVariant list> =
    task {
        // The number goes into a URL path: digits only (callers strip the "rs").
        if System.String.IsNullOrEmpty rsNumber || not (rsNumber |> Seq.forall (fun c -> c >= '0' && c <= '9')) then
            raise (GrnaInputException(sprintf "'%s' is not an rsID number." rsNumber))
        let url = $"https://api.ncbi.nlm.nih.gov/variation/v0/refsnp/{rsNumber}"
        let! jsonString = loadJsonFromUrlAsync url cancellationToken

        try
            let variants = parseVariants jsonString |> List.distinctBy _.Hgvs
            GrnaLog.log $"HGVS Notations: %A{variants |> List.map _.Hgvs}"
            return variants
        with ex ->
            GrnaLog.log (sprintf "Error parsing SNP data: %s" ex.Message)
            return []
    }

/// The default notations of an rsID: the clinically relevant allele(s) on the main RefSeqGene record only.
/// Falls back to every notation when the default selection would be empty.
let getHgvsNotationsAsync (rsNumber: string) (cancellationToken: System.Threading.CancellationToken) : Task<string list> =
    task {
        let! variants = getVariantsAsync rsNumber cancellationToken
        let preferred = variants |> List.filter _.Preferred
        return (if preferred.IsEmpty then variants else preferred) |> List.map _.Hgvs
    }

/// Every NG_ notation of an rsID (all alleles, all RefSeqGene records): the "show all" behaviour.
let getAllHgvsNotationsAsync (rsNumber: string) (cancellationToken: System.Threading.CancellationToken) : Task<string list> =
    task {
        let! variants = getVariantsAsync rsNumber cancellationToken
        return variants |> List.map _.Hgvs
    }

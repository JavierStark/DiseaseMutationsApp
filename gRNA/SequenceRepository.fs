module gRNA.SequenceRepository

open System
open System.Collections.Concurrent
open System.Collections.Generic
open System.Net.Http
open System.Threading
open System.Threading.Tasks
open gRNA.Sequence

/// Turns an efetch FASTA response into the bare, upper-case sequence. Anything that is not exactly one FASTA record
/// (an HTML or JSON error page, an empty body, a second record) raises GrnaUpstreamException, so it can never be
/// designed against as if it were a genome.
let parseFasta (id: string) (content: string) : string =
    let lines = content.Replace("\r", "").Split('\n')
    if lines.Length < 2 || not (lines.[0].StartsWith ">") then
        raise (GrnaUpstreamException(sprintf "NCBI did not return a FASTA record for %s." id))
    let sequence = lines |> Array.skip 1 |> String.concat "" |> fun s -> s.Trim().ToUpperInvariant()
    if sequence.Length = 0 then
        raise (GrnaUpstreamException(sprintf "NCBI returned an empty sequence for %s." id))
    match sequence |> Seq.tryFind (fun c -> c < 'A' || c > 'Z') with
    | Some c -> raise (GrnaUpstreamException(sprintf "NCBI returned an unexpected character '%c' in the sequence of %s." c id))
    | None -> sequence

/// Reads the 1-based first and last position of the coding sequence (CDS) from a GenBank record. Only a single plain range
/// (`CDS  51..494`) is accepted; a split (`join(...)`) or reverse (`complement(...)`) location is refused.
let parseCdsRange (id: string) (genBank: string) : int * int =
    let m = System.Text.RegularExpressions.Regex.Match(genBank, @"(?m)^\s{5}CDS\s+<?(\d+)\.\.>?(\d+)\s*$")
    if m.Success then
        int m.Groups.[1].Value, int m.Groups.[2].Value
    elif System.Text.RegularExpressions.Regex.IsMatch(genBank, @"(?m)^\s{5}CDS\s") then
        raise (GrnaInputException(sprintf "The coding sequence of %s is not a single range (it is split or on the other strand), so c. positions cannot be converted. Use genomic (g.) coordinates." id))
    else
        raise (GrnaInputException(sprintf "%s has no coding sequence (CDS) annotation, so c. positions cannot be converted. Use n. or g. positions." id))

let private cdsCache = ConcurrentDictionary<string, Lazy<Task<int * int>>>()

type SequenceRepository() =
    static let BASE_URL = "https://eutils.ncbi.nlm.nih.gov/entrez/eutils/efetch.fcgi?db=nuccore&id="
    static let httpClient = new HttpClient(Timeout = TimeSpan.FromMinutes 5.0)
    static let cache = ConcurrentDictionary<string, Lazy<Task<Sequence>>>()
    static let order = ConcurrentQueue<string>()

    static member private Evict() =
        let limit = max 1 (GrnaEnvironment.getCurrent ()).SequenceCacheEntries
        let mutable go = true
        while go && cache.Count > limit do
            match order.TryDequeue() with
            | true, key -> cache.TryRemove key |> ignore
            | _ -> go <- false

    /// Fetches (and caches, bounded by insertion order) a sequence. Concurrent callers share one fetch.
    static member GetSequence(id: string, cancellationToken: CancellationToken) : Task<Sequence> =
        let lazyTask =
            cache.GetOrAdd(id, fun key ->
                order.Enqueue key
                Lazy<Task<Sequence>>(fun () ->
                    task {
                        // Shared fetch is deliberately not tied to one caller's token.
                        let! data = SequenceRepository.GetSequenceData(key, CancellationToken.None)
                        return Sequence(key, data)
                    }))
        SequenceRepository.Evict()
        task {
            try
                return! lazyTask.Value.WaitAsync(cancellationToken)
            with
            | :? OperationCanceledException -> return raise (OperationCanceledException(cancellationToken))
            | ex ->
                // Do not cache failures.
                cache.TryRemove(KeyValuePair(id, lazyTask)) |> ignore
                return raise ex
        }

    static member GetSequence(id: string) = SequenceRepository.GetSequence(id, CancellationToken.None)

    /// The CDS range of a transcript record (from its GenBank entry), cached; failures are not cached.
    static member GetCodingRange(id: string, cancellationToken: CancellationToken) : Task<int * int> =
        let lazyTask =
            cdsCache.GetOrAdd(id, fun key ->
                Lazy<Task<int * int>>(fun () ->
                    task {
                        let env = GrnaEnvironment.getCurrent ()
                        let url =
                            BASE_URL + Uri.EscapeDataString key + "&rettype=gb&retmode=text"
                            + (match env.NcbiApiKey with Some k -> "&api_key=" + Uri.EscapeDataString k | None -> "")
                            + (match env.NcbiContact with Some c -> "&tool=gRNA&email=" + Uri.EscapeDataString c | None -> "")
                        let! response = httpClient.GetAsync(url, CancellationToken.None)
                        if not response.IsSuccessStatusCode then
                            raise (GrnaUpstreamException(sprintf "NCBI returned HTTP %d for the record of %s." (int response.StatusCode) key))
                        let! content = response.Content.ReadAsStringAsync(CancellationToken.None)
                        return parseCdsRange key content
                    }))
        task {
            try
                return! lazyTask.Value.WaitAsync(cancellationToken)
            with
            | :? OperationCanceledException -> return raise (OperationCanceledException(cancellationToken))
            | ex ->
                cdsCache.TryRemove(KeyValuePair(id, lazyTask)) |> ignore
                return raise ex
        }

    static member private GetSequenceData(id: string, cancellationToken: CancellationToken) =
        task {
            let env = GrnaEnvironment.getCurrent ()
            let url =
                BASE_URL + Uri.EscapeDataString id + "&rettype=fasta"
                + (match env.NcbiApiKey with Some k -> "&api_key=" + Uri.EscapeDataString k | None -> "")
                + (match env.NcbiContact with Some c -> "&tool=gRNA&email=" + Uri.EscapeDataString c | None -> "")
            let! response = httpClient.GetAsync(url, cancellationToken)
            if not response.IsSuccessStatusCode then
                raise (GrnaUpstreamException(sprintf "NCBI returned HTTP %d for %s." (int response.StatusCode) id))
            let! content = response.Content.ReadAsStringAsync(cancellationToken)
            return parseFasta id content
        }

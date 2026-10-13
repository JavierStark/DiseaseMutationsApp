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

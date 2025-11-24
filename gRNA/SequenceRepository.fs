module gRNA.SequenceRepository

open System.Collections.Generic
open System.Net.Http
open gRNA.Sequence

type SequenceRepository() =
    static let BASE_URL = "https://eutils.ncbi.nlm.nih.gov/entrez/eutils/efetch.fcgi?db=nuccore&id="
    static let sequences = Dictionary<string, Sequence>()
    
    static member GetSequence(id: string) =
        async {
            match sequences.TryGetValue(id) with
            | true, sequence -> 
                return sequence
            | false, _ ->
                let! data = SequenceRepository.GetSequenceData(id)
                let sequence = Sequence(id, data)
                sequences.[id] <- sequence
                return sequence
        }
    
    static member private GetSequenceData(id: string) =
        async {
            use httpClient = new HttpClient()
            let! response = httpClient.GetAsync($"{BASE_URL}{id}&rettype=fasta") |> Async.AwaitTask
            response.EnsureSuccessStatusCode() |> ignore
            let! content = response.Content.ReadAsStringAsync() |> Async.AwaitTask
            let lines = content.Split('\n')
            let data = 
                lines 
                |> Array.skip 1 
                |> String.concat ""
                |> fun s -> s.Trim()
            return data
        }
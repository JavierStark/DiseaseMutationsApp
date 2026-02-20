module gRNA.SequenceRepository

open System.Collections.Generic
open System.Net.Http
open System.Threading.Tasks
open gRNA.Sequence

type SequenceRepository() =
    static let BASE_URL = "https://eutils.ncbi.nlm.nih.gov/entrez/eutils/efetch.fcgi?db=nuccore&id="
    static let sequences = Dictionary<string, Sequence>()
    
    static member GetSequence(id: string) =
        task {
            match sequences.TryGetValue(id) with
            | true, sequence -> 
                return sequence
            | false, _ ->
                let! data : string = SequenceRepository.GetSequenceData(id)
                let rna = data.Replace('T', 'U')
                let sequence = Sequence(id, rna)
                sequences.[id] <- sequence
                return sequence
        }
    
    static member private GetSequenceData(id: string) =
        task {
            use httpClient = new HttpClient()
            let! response = httpClient.GetAsync($"{BASE_URL}{id}&rettype=fasta")
            response.EnsureSuccessStatusCode() |> ignore
            let! content = response.Content.ReadAsStringAsync()
            let lines = content.Split('\n')
            let data =
                lines 
                |> Array.skip 1 
                |> String.concat ""
                |> _.Trim()
            return data
        }
module gRNA.SNP

open gRNA

open System.Net.Http
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

let getHgvsNotationsAsync (rsNumber: string) (cancellationToken: System.Threading.CancellationToken) : Task<string list> =
    task {
        let url = $"https://api.ncbi.nlm.nih.gov/variation/v0/refsnp/{rsNumber}"
        let! jsonString = loadJsonFromUrlAsync url cancellationToken
        
        try
            let pattern = "NG_\\d+(?:\\.\\d+)?:[a-z]\\.[a-zA-Z0-9_>+*=\\-]+"
            let matches = System.Text.RegularExpressions.Regex.Matches(jsonString, pattern)
            let hgvsNotations =
                matches
                |> Seq.cast<System.Text.RegularExpressions.Match>
                |> Seq.map _.Value
                |> Seq.filter (fun x -> not (x.EndsWith("=")))
                |> Seq.distinct
                
                |> Seq.toList
            
            GrnaLog.log $"HGVS Notations: %A{hgvsNotations}"
            
            return hgvsNotations
        with ex ->
            GrnaLog.log (sprintf "Error parsing SNP data: %s" ex.Message)
            return []
    }

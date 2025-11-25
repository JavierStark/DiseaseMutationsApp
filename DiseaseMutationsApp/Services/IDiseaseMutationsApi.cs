using Refit;

namespace DiseaseMutationsApp.Services;

public interface IDiseaseMutationsApi
{
    [Get("/getbestrna")]
    Task<List<GRNAResult>> GetBestgRNA([Query] int window, [Query] string sequence);
    
    //string -> int -> Async<gRNAResult list * string * string * int>
    [Get("/getbestgrnafromhgvs")]
    Task<ResultFromHGVS> GetBestgRNAFromHgvs([Query] string hgvs, [Query] int window);
    
}
/*
 *
 *         type gRNAResult = {
        Sequence: string
        GCScore: float
        HomopolymerCount: int
        SeedRegion: string
        Allignments: int
    }

 */

public record GRNAResult
{
    public string Sequence { get; init; }
    public float GCScore { get; init; }
    public int HomopolymerCount { get; init; }
    public string SeedRegion { get; init; }
    public int Allignments { get; init; }
}

// type ResultFromHGVS = {
//     gRNA: SpacerFinder.gRNAResult list
//     mutatedSequence: string
//     originalSequence: string
//     extraNucleotids: int
// }
//

public record ResultFromHGVS
{
    public List<GRNAResult> gRNA { get; init; }
    public string MutatedSequence { get; init; }
    public string OriginalSequence { get; init; }
    public int ExtraNucleotids { get; init; }
}
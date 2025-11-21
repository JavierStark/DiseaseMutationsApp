using Refit;

namespace DiseaseMutationsApp.Services;

public interface IDiseaseMutationsApi
{
    [Get("/getbestrna")]
    Task<List<GRNAResult>> GetBestgRNA([Query] int window, [Query] string sequence);
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
using Refit;

namespace DiseaseMutationsApp.Services;

public interface IDiseaseMutationsApi
{
    [Get("/getbestgrnafromhgvs")]
    Task<ResultFromHGVS> GetBestgRNAFromHgvs([Query] string hgvs, [Query] int window);

    [Get("/gethgvsfromsnp")]
    Task<List<string>> GetHgvsFromSnp([Query] string rsid);

    [Get("/getrnafold")]
    Task<RNAFoldResult> GetRnaFold([Query] string sequence);

    [Get("/getfornaurl")]
    Task<string> GetFornaUrl([Query] string sequence, [Query] string structure);
}

public record GRNAResult
{
    public string Sequence { get; init; }
    public float GCScore { get; init; }
    public int HomopolymerCount { get; init; }
    public string SeedRegion { get; init; }
    public int Allignments { get; init; }
}

public record ResultFromHGVS
{
    public List<GRNAResult> gRNA { get; init; }
    public string MutatedSequence { get; init; }
    public string OriginalSequence { get; init; }
    public int ExtraNucleotids { get; init; }
}

public record RNAFoldResult
{
    public string Structure { get; init; }
    public double Energy { get; init; }
}


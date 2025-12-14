using Refit;

namespace DiseaseMutationsApp.Services;

public interface IDiseaseMutationsApi
{
    [Get("/getbestgrnafromhgvs")]
    Task<ResultFromHGVS> GetBestgRNAFromHgvs([Query] string hgvs, [Query] int window);

    [Get("/gethgvsfromsnp")]
    Task<List<string>> GetHgvsFromSnp([Query] string rsid);

    [Get("/getrsfromomim")]
    Task<List<string>> GetRsFromOmim([Query] int omim);

    [Get("/getrnafold")]
    Task<RNAFoldResult> GetRnaFold([Query] string sequence);

    [Get("/getfornaurl")]
    Task<string> GetFornaUrl([Query] string sequence, [Query] string structure);
}

/*
 *     { Sequence = sequence
      GCScore = gcScore
      HomopolymerCount = homopolymerCount
      SeedRegion = seedRegion
      Allignments = 0
      RnaFoldResult = { Structure = ""; Energy = 0.0 }
      Rank = 0
      Score = 0.0}
 */
public record GRNAResult
{
    public string Sequence { get; init; }
    public float GCScore { get; init; }
    public int HomopolymerCount { get; init; }
    public string SeedRegion { get; init; }
    public int Allignments { get; init; }
    public RNAFoldResult RnaFoldResult { get; init; }
    public int Rank { get; init; }
    public double Score { get; init; }
    
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

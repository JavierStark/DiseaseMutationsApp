using gRNA;

namespace DiseaseMutationsApp.Services;

/// <summary>
/// Service that provides direct access to the F# gRNA library functionality.
/// Replaces the HTTP-based IDiseaseMutationsApi.
/// </summary>
public class GrnaService
{
    private readonly ILogger<GrnaService> _logger;
    private readonly gRNA.Services.BowtieService _bowtieService;

    public GrnaService(ILogger<GrnaService> logger, gRNA.Services.BowtieService bowtieService)
    {
        _logger = logger;
        _bowtieService = bowtieService;
    }

    public async Task<ResultFromHGVS> GetBestgRNAFromHgvs(string hgvs, int window, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Getting best gRNA from HGVS: {Hgvs}, Window: {Window}", hgvs, window);

            var fsharpResult = await Main.getBestgRNAFromHGVS(hgvs, window, _bowtieService, cancellationToken);

            // Convert F# result to C# record
            var grnaResults = fsharpResult.gRNA
                .Select(g => new GRNAResult
                {
                    Sequence = g.Sequence,
                    GCScore = (float)g.GCScore,
                    HomopolymerCount = g.HomopolymerCount,
                    SeedRegion = g.SeedRegion,
                    Allignments = g.Allignments,
                    RnaFoldResult = new RNAFoldResult
                    {
                        Structure = g.RnaFoldResult.Structure,
                        Energy = g.RnaFoldResult.Energy
                    },
                    Rank = g.Rank,
                    Score = g.Score,
                    MutationHighlightStart = g.MutationHighlightStart,
                    MutationHighlightLength = g.MutationHighlightLength
                })
                .ToList();

            return new ResultFromHGVS
            {
                gRNA = grnaResults,
                MutatedSequence = fsharpResult.mutatedSequence,
                OriginalSequence = fsharpResult.originalSequence,
                ExtraNucleotids = fsharpResult.extraNucleotids
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting best gRNA from HGVS: {Hgvs}", hgvs);
            throw;
        }
    }

    public async Task<List<string>> GetHgvsFromSnp(string rsid)
    {
        try
        {
            _logger.LogInformation("Getting HGVS notations from SNP: {RsId}", rsid);

            var fsharpList = await SNP.getHgvsNotationsAsync(rsid);
            return new List<string>(fsharpList);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting HGVS from SNP: {RsId}", rsid);
            throw;
        }
    }

    public async Task<List<string>> GetRsFromOmim(int omim)
    {
        try
        {
            _logger.LogInformation("Getting RS codes from OMIM: {Omim}", omim);

            var fsharpList = await Omim.rsFromOmim(omim);
            return new List<string>(fsharpList);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting RS from OMIM: {Omim}", omim);
            throw;
        }
    }

    public async Task<RNAFoldResult> GetRnaFold(string sequence)
    {
        try
        {
            _logger.LogInformation("Getting RNA fold for sequence of length: {Length}", sequence.Length);

            var fsharpResult = await RNAFoldWrapper.fold(sequence);

            return new RNAFoldResult
            {
                Structure = fsharpResult.Structure,
                Energy = fsharpResult.Energy
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting RNA fold for sequence");
            throw;
        }
    }

    public string GetFornaUrl(string sequence, string structure)
    {
        return $"http://nibiru.tbi.univie.ac.at/forna/forna.html?id=url/name&sequence={sequence}&structure={structure}";
    }
}

// Model classes previously defined in IDiseaseMutationsApi.cs
public record GRNAResult
{
    public required string Sequence { get; init; }
    public float GCScore { get; init; }
    public int HomopolymerCount { get; init; }
    public required string SeedRegion { get; init; }
    public int Allignments { get; init; }
    public required RNAFoldResult RnaFoldResult { get; init; }
    public int Rank { get; init; }
    public double Score { get; init; }
    public int MutationHighlightStart { get; init; }
    public int MutationHighlightLength { get; init; }
}

public record ResultFromHGVS
{
    public required List<GRNAResult> gRNA { get; init; }
    public required string MutatedSequence { get; init; }
    public required string OriginalSequence { get; init; }
    public int ExtraNucleotids { get; init; }
}

public record RNAFoldResult
{
    public required string Structure { get; init; }
    public double Energy { get; init; }
}

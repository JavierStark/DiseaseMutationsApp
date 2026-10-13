using DiseaseMutationsApp.Services;
using gRNA;

namespace DiseaseMutationsApp.E2E;

/// <summary>
/// Deterministic stand-in for the real pipeline. The variant text selects the behaviour:
/// containing "SLOW" it waits until cancelled (for cancel tests), containing "FAIL" it raises the same typed input error
/// the real pipeline raises for a reference mismatch, anything else returns five ranked candidates per sequence.
/// </summary>
public sealed class FakeAnalysis : IGrnaAnalysis
{
    public async Task<ResultFromHGVS> GetBestgRNAFromHgvs(string hgvs, int window, int seedStart, int seedEnd,
        bool complement = false, CancellationToken cancellationToken = default)
    {
        if (hgvs.Contains("SLOW", StringComparison.Ordinal))
            await Task.Delay(TimeSpan.FromMinutes(2), cancellationToken);
        else
            await Task.Delay(150, cancellationToken);

        if (hgvs.Contains("FAIL", StringComparison.Ordinal))
            throw new GrnaInputException("Reference mismatch at NC_1.1:5_5: the variant states A but the sequence has C.", Microsoft.FSharp.Core.FSharpOption<Exception>.None);

        var mutated = Candidates(window, seedStart, seedEnd, mutated: true, complement);
        var original = Candidates(window, seedStart, seedEnd, mutated: false, complement);
        return new ResultFromHGVS
        {
            gRNA = mutated,
            OriginalGRNA = original,
            MutatedSequence = Sequence(55, mutated: true),
            OriginalSequence = Sequence(55, mutated: false),
            ExtraNucleotids = 27,
            WindowStart = 70587,
            WindowEnd = 70641
        };
    }

    public Task<List<string>> GetHgvsFromSnp(string rsid, CancellationToken cancellationToken = default) =>
        Task.FromResult(new List<string> { "NG_000007.3:g.70614A>T", "NG_000007.3:g.70614A>G" });

    public string? GetNcbiNuccoreUrl(string hgvs) => null;

    private static string Sequence(int length, bool mutated)
    {
        const string unit = "CAGACACCATGGTGCATCTGACTCCTGAGGAGAAGTCTGCCGTTACTGCCCTGTG";
        var s = unit[..Math.Min(length, unit.Length)];
        return mutated ? s.Remove(27, 1).Insert(27, "T") : s;
    }

    private static List<GRNAResult> Candidates(int window, int seedStart, int seedEnd, bool mutated, bool complement)
    {
        var bases = new[] { 'A', 'C', 'G', 'U' };
        var list = new List<GRNAResult>();
        for (var i = 0; i < 5; i++)
        {
            var seq = new string(Enumerable.Range(0, window).Select(j => bases[(j * (i + 2) + i + (mutated ? 1 : 0)) % 4]).ToArray());
            list.Add(new GRNAResult
            {
                Sequence = seq,
                GCScore = 1f - i * 0.1f,
                GCContent = 50f + i,
                HomopolymerCount = i % 2,
                SeedRegion = seq.Substring(seedStart, seedEnd - seedStart + 1),
                Allignments = complement ? 1 : 1 + i / 2,
                RnaFoldResult = new RNAFoldResult { Structure = new string('.', 36 + window), Energy = -10 - i },
                Rank = i + 1,
                Score = Math.Round(1.0 - i * 0.2, 2),
                MutationHighlightStart = -1,
                MutationHighlightLength = 0
            });
        }
        return list;
    }
}

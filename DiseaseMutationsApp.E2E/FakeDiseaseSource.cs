using DiseaseMutationsApp.Services;
using gRNA;

namespace DiseaseMutationsApp.E2E;

/// <summary>
/// Deterministic stand-in for Monarch and NCBI ClinVar. What the user types selects the behaviour:
/// "manymatches" finds 10 diseases (a long list), "sickle" finds one disease with 8 variants (rs334 among them), "fanconi" finds a group that needs its subtypes and has 70
/// variants (more than Diana takes in one run), "empty" finds a disease without variants, "trunc" one whose list is incomplete,
/// "nomatch" nothing, "down" fails like an unreachable Monarch, "slowvars" waits until cancelled, and "flaky" fails its first
/// variant search and then works.
/// </summary>
public sealed class FakeDiseaseSource : IDiseaseVariantSource
{
    private int _flakyFailures = 1;

    public async Task<IReadOnlyList<DiseaseMatchDto>> FindDiseasesAsync(string text, CancellationToken cancellationToken = default)
    {
        await Task.Delay(100, cancellationToken);
        var t = text.ToLowerInvariant();
        if (t.Contains("down")) throw new GrnaUpstreamException("Monarch could not be reached.", Microsoft.FSharp.Core.FSharpOption<Exception>.None);
        if (t.Contains("nomatch")) return Array.Empty<DiseaseMatchDto>();
        if (t.Contains("manymatches"))   // as many as Monarch returns for a common name: enough to need the scroll list
            return Enumerable.Range(1, 10).Select(i => new DiseaseMatchDto($"MONDO:00{i:D5}", $"a disease with a fairly long name number {i}",
                i == 1 ? new[] { "100001" } : Array.Empty<string>(), Array.Empty<string>())).ToList();
        if (t.Contains("fanconi"))
            return new[]
            {
                new DiseaseMatchDto("MONDO:0019391", "Fanconi anemia", Array.Empty<string>(), new[] { "PS227650" }),
                new DiseaseMatchDto("MONDO:0009213", "Fanconi anemia complementation group C", new[] { "227645" }, Array.Empty<string>())
            };
        if (t.Contains("empty")) return new[] { new DiseaseMatchDto("MONDO:0000001", "an empty disease", new[] { "100001" }, Array.Empty<string>()) };
        if (t.Contains("trunc")) return new[] { new DiseaseMatchDto("MONDO:0000002", "a very large disease", new[] { "100002" }, Array.Empty<string>()) };
        if (t.Contains("slowvars")) return new[] { new DiseaseMatchDto("MONDO:0000003", "a slow disease", new[] { "100003" }, Array.Empty<string>()) };
        if (t.Contains("flaky")) return new[] { new DiseaseMatchDto("MONDO:0000004", "a flaky disease", new[] { "100004" }, Array.Empty<string>()) };
        return new[]
        {
            new DiseaseMatchDto("MONDO:0011382", "sickle cell disease", new[] { "603903" }, Array.Empty<string>()),
            new DiseaseMatchDto("MONDO:0016669", "sickle cell-hemoglobin C disease", new[] { "603903", "141900" }, Array.Empty<string>())
        };
    }

    public async Task<DiseaseSearchResultDto> FindVariantsAsync(DiseaseMatchDto disease, bool includeSubtypes, DiseaseSignificance minimum,
        IProgress<DiseaseProgressDto>? progress = null, CancellationToken cancellationToken = default)
    {
        progress?.Report(new DiseaseProgressDto("Finding ClinVar records", 0, 0));
        await Task.Delay(150, cancellationToken);
        if (disease.Id == "MONDO:0000003") await Task.Delay(TimeSpan.FromMinutes(2), cancellationToken);
        if (disease.Id == "MONDO:0000004" && Interlocked.Exchange(ref _flakyFailures, 0) == 1)
            throw new GrnaUpstreamException("NCBI esearch returned HTTP 503.", Microsoft.FSharp.Core.FSharpOption<Exception>.None);

        progress?.Report(new DiseaseProgressDto("Reading variant details", 0, 40));
        await Task.Delay(150, cancellationToken);
        progress?.Report(new DiseaseProgressDto("Reading variant details", 40, 40));

        return disease.Id switch
        {
            "MONDO:0011382" or "MONDO:0016669" => Result(new[] { "603903" }, 0, 0, Sickle(), false),
            "MONDO:0019391" or "MONDO:0009213" => Result(new[] { "227645", "227646" }, 22, 22, Many(70), false),
            "MONDO:0000001" => Result(new[] { "100001" }, 0, 0, Array.Empty<DiseaseVariantDto>(), false),
            "MONDO:0000002" => new DiseaseSearchResultDto(new[] { "100002" }, 0, 0, 4543, true, Many(30)),
            _ => Result(new[] { "100004" }, 0, 0, Many(5), false)
        };
    }

    private static DiseaseSearchResultDto Result(string[] omim, int subtypesChecked, int subtypesTotal, IReadOnlyList<DiseaseVariantDto> variants, bool truncated) =>
        new(omim, subtypesChecked, subtypesTotal, variants.Count, truncated, variants);

    private static DiseaseVariantDto V(string id, string name, string? gene, string? rs, string? hgvs, string significance, string review, int stars, params string[] conditions) =>
        new(id, name, gene, rs, hgvs, significance, review, stars, conditions);

    private static IReadOnlyList<DiseaseVariantDto> Sickle() => new[]
    {
        V("15333", "NM_000518.5(HBB):c.20A>T (p.Glu7Val)", "HBB", "rs334", "NC_000011.10:g.5227002T>A", "Pathogenic", "criteria provided, multiple submitters, no conflicts", 2, "Sickle cell disease"),
        V("15334", "NM_000518.5(HBB):c.19G>A (p.Glu7Lys)", "HBB", "rs33930165", "NC_000011.10:g.5227003C>T", "Pathogenic", "reviewed by expert panel", 3, "Hemoglobin C disease", "Sickle cell-hemoglobin C disease"),
        V("15335", "NM_000518.5(HBB):c.364G>C (p.Glu122Gln)", "HBB", "rs33950507", "NC_000011.10:g.5226762C>G", "Likely pathogenic", "criteria provided, single submitter", 1, "Hemoglobin D disease"),
        V("15336", "NM_000518.5(HBB):c.79G>A (p.Glu27Lys)", "HBB", "rs33946267", "NC_000011.10:g.5227000C>T", "Pathogenic/Likely pathogenic", "criteria provided, multiple submitters, no conflicts", 2, "Hemoglobin E disease"),
        V("15337", "NM_000518.5(HBB):c.92+1G>A", "HBB", "rs33971440", "NC_000011.10:g.5226929C>T", "Pathogenic", "criteria provided, single submitter", 1, "Beta thalassemia"),
        V("15338", "NM_000518.5(HBB):c.118C>T (p.Gln40Ter)", "HBB", "rs11549407", "NC_000011.10:g.5226962G>A", "Pathogenic", "practice guideline", 4, "Beta thalassemia", "Sickle cell disease"),
        V("3336287", "NC_000011.10:g.5225715G>C", "HBB", null, "NC_000011.10:g.5225715G>C", "Likely pathogenic", "no assertion criteria provided", 0, "Sickle cell disease"),
        V("3336288", "a variant with nothing to send", null, null, null, "Likely pathogenic", "no assertion criteria provided", 0)
    };

    private static IReadOnlyList<DiseaseVariantDto> Many(int count) =>
        Enumerable.Range(1, count).Select(i =>
            i % 7 == 0
                ? V($"9{i:D5}", $"NM_000001.1(GENE{i % 5}):c.{i}A>G", $"GENE{i % 5}", null, $"NC_000012.12:g.{102000000 + i}A>G", "Likely pathogenic", "criteria provided, single submitter", 1, "A condition")
                : V($"9{i:D5}", $"NM_000001.1(GENE{i % 5}):c.{i}A>G", $"GENE{i % 5}", $"rs{500000 + i}", $"NC_000012.12:g.{102000000 + i}A>G", i % 3 == 0 ? "Pathogenic" : "Likely pathogenic",
                    i % 3 == 0 ? "reviewed by expert panel" : "criteria provided, single submitter", i % 3 == 0 ? 3 : 1, "A condition", "Another condition"))
        .ToList();
}

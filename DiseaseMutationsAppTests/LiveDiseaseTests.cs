using gRNA;
using Microsoft.FSharp.Collections;
using static gRNA.DiseaseVariants;

namespace DiseaseMutationsAppTests;

/// <summary>
/// Contract tests against the REAL Monarch and NCBI services. They are skipped unless GRNA_LIVE_TESTS=1, and CI never sets it:
/// they exist to notice the day an upstream response changes shape, which is what ended the OMIM feature. Counts are
/// deliberately loose (ClinVar changes every month).
///   GRNA_LIVE_TESTS=1 dotnet test --filter Category=Live
/// </summary>
[Category("Live")]
[NonParallelizable]
public class LiveDiseaseTests
{
    private static DiseaseVariantClient Client => DiseaseVariantClient.Shared;

    [SetUp]
    public void RequireOptIn()
    {
        if (Environment.GetEnvironmentVariable("GRNA_LIVE_TESTS") != "1")
            Assert.Ignore("Live tests talk to Monarch and NCBI. Set GRNA_LIVE_TESTS=1 to run them.");
    }

    private static async Task<DiseaseVariantResult> FetchAsync(params string[] omim) =>
        await Client.GetVariantsAsync(ListModule.OfSeq(omim), MinSignificance.PathogenicOrLikely, new Progress<VariantProgress>(), CancellationToken.None);

    [Test]
    public async Task Monarch_FindsPhenylketonuriaByName_WithItsOmimNumber()
    {
        var matches = await Client.FindDiseasesAsync("phenylketonuria", CancellationToken.None);

        var pku = matches.FirstOrDefault(m => m.Id == "MONDO:0009861");
        Assert.That(pku, Is.Not.Null, string.Join(", ", matches.Select(m => m.Id)));
        Assert.That(pku!.OmimNumbers, Does.Contain("261600"));
    }

    [Test]
    public async Task Monarch_ResolvesAMondoIdAndAnOmimNumberToTheSameDisease()
    {
        var byMondo = await Client.FindDiseasesAsync("MONDO:0009861", CancellationToken.None);
        Assert.That(byMondo.Single().OmimNumbers, Does.Contain("261600"));
        Assert.That(await Client.FindDiseasesAsync("MONDO:9999999", CancellationToken.None), Is.Empty);
    }

    [Test]
    public async Task Monarch_FanconiAnemiaIsAPhenotypicSeries_ResolvedThroughSubtypes()
    {
        var parent = (await Client.FindDiseasesAsync("MONDO:0019391", CancellationToken.None)).Single();
        Assert.That(parent.OmimNumbers, Is.Empty, "the parent has only OMIMPS:227650");

        var resolved = await Client.ResolveOmimNumbersAsync(parent, false, CancellationToken.None);

        Assert.That(resolved.OmimNumbers.Count(), Is.GreaterThanOrEqualTo(10));
        Assert.That(resolved.SubtypesChecked, Is.EqualTo(resolved.SubtypesTotal).Or.EqualTo(40));
    }

    [Test]
    public async Task Ncbi_PhenylketonuriaHasManyPathogenicVariants_MostWithAnRsId()
    {
        var result = await FetchAsync("261600");

        Assert.Multiple(() =>
        {
            Assert.That(result.Variants.Count(), Is.GreaterThan(500));
            Assert.That(result.Truncated, Is.False);
            Assert.That(result.TotalMatching, Is.GreaterThanOrEqualTo(result.Variants.Count()));
            Assert.That(result.Variants.All(v => meetsSignificance(MinSignificance.PathogenicOrLikely, v.Significance)), Is.True);
            Assert.That(countWithRsId(result.Variants) * 100 / result.Variants.Count(), Is.GreaterThan(50), "most records carry a dbSNP cross-reference");
            Assert.That(result.Variants.Count(v => v.Hgvs is not null), Is.GreaterThan(result.Variants.Count() / 2));
            Assert.That(result.Variants.Select(v => v.ClinVarId), Is.Unique);
        });
    }

    [Test]
    public async Task Ncbi_SickleCellDisease_ContainsRs334()
    {
        var result = await FetchAsync("603903");

        var rs = result.Variants.Where(v => v.RsId is not null).Select(v => v.RsId.Value).ToList();
        Assert.That(rs, Does.Contain("rs334"));
    }

    [Test]
    public async Task Ncbi_EveryHgvsWeDerive_IsAcceptedByDiana()
    {
        var result = await FetchAsync("603903");

        var codes = result.Variants.Where(v => v.Hgvs is not null).Select(v => v.Hgvs.Value).ToList();
        Assert.That(codes, Is.Not.Empty);
        foreach (var code in codes)
            Assert.DoesNotThrow(() => new gRNA.HGVS.HGVS(code), code);
    }

    [Test]
    public async Task Ncbi_AnUnknownOmimNumber_GivesAnEmptyResult_NotAnError()
    {
        var result = await FetchAsync("999999999");

        Assert.That(result.Variants, Is.Empty);
        Assert.That(result.TotalMatching, Is.EqualTo(0));
    }

    [Test]
    public async Task Ncbi_PathogenicOnly_IsASubsetOfPathogenicOrLikely()
    {
        var either = await FetchAsync("603903");
        var only = await Client.GetVariantsAsync(ListModule.OfSeq(new[] { "603903" }), MinSignificance.PathogenicOnly, new Progress<VariantProgress>(), CancellationToken.None);

        Assert.That(only.Variants.Count(), Is.LessThanOrEqualTo(either.Variants.Count()));
        Assert.That(only.Variants.Select(v => v.ClinVarId), Is.SubsetOf(either.Variants.Select(v => v.ClinVarId).ToList()));
    }
}

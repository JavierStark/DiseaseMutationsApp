using DiseaseMutationsApp.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace DiseaseMutationsAppTests;

/// <summary>
/// Covers only the pure, dependency-free members of GrnaService. Methods that call into
/// Bowtie, ViennaRNA, or the NCBI/dbSNP APIs (GetBestgRNAFromHgvs, GetHgvsFromSnp, GetRnaFold)
/// are out of scope for unit tests: none of those external tools/services are available here.
/// Constructing gRNA.Services.BowtieService is safe because its constructor only allocates a
/// semaphore; no test in this file invokes ProcessMultipleSequencesAsync.
/// </summary>
public class GrnaServiceTests
{
    private static GrnaService CreateSut() =>
        new(NullLogger<GrnaService>.Instance, new gRNA.Services.BowtieService());

    [Test]
    public void Scaffold_MatchesSpacerFinderScaffold()
    {
        Assert.That(GrnaService.Scaffold, Is.EqualTo(gRNA.SpacerFinder.scaffold));
    }

    [Test]
    public void GetNcbiNuccoreUrl_ValidHgvs_ReturnsAccessionUrl()
    {
        var sut = CreateSut();
        var url = sut.GetNcbiNuccoreUrl("NC_000017.11:g.7674220C>T");

        Assert.That(url, Is.EqualTo("https://www.ncbi.nlm.nih.gov/nuccore/NC_000017.11"));
    }

    [Test]
    public void GetNcbiNuccoreUrl_NullOrWhitespace_ReturnsNull()
    {
        var sut = CreateSut();

        Assert.Multiple(() =>
        {
            Assert.That(sut.GetNcbiNuccoreUrl(null!), Is.Null);
            Assert.That(sut.GetNcbiNuccoreUrl(""), Is.Null);
            Assert.That(sut.GetNcbiNuccoreUrl("   "), Is.Null);
        });
    }

    [Test]
    public void GetNcbiNuccoreUrl_NoColon_UsesWholeStringAsAccession()
    {
        var sut = CreateSut();
        var url = sut.GetNcbiNuccoreUrl("NC_000017.11");

        Assert.That(url, Is.EqualTo("https://www.ncbi.nlm.nih.gov/nuccore/NC_000017.11"));
    }

    [Test]
    public void GetNcbiNuccoreUrl_EscapesAccession()
    {
        var sut = CreateSut();
        var url = sut.GetNcbiNuccoreUrl("NC 000017.11:g.7674220C>T");

        Assert.That(url, Is.EqualTo("https://www.ncbi.nlm.nih.gov/nuccore/NC%20000017.11"));
    }

    [Test]
    public void GetFornaUrl_ContainsSequenceAndStructure()
    {
        var sut = CreateSut();
        var url = sut.GetFornaUrl("GAUUUAGACUACCCCAAAAACGAAGGGGACUAAAAC", "(((...)))");

        Assert.Multiple(() =>
        {
            Assert.That(url, Does.Contain("GAUUUAGACUACCCCAAAAACGAAGGGGACUAAAAC"));
            Assert.That(url, Does.Contain("(((...)))"));
        });
    }
}

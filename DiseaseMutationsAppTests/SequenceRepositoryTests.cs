using gRNA;

namespace DiseaseMutationsAppTests;

/// <summary>What NCBI efetch may return is not trusted: only exactly one FASTA record becomes a sequence.</summary>
public class SequenceRepositoryTests
{
    [Test]
    public void SingleRecord_JoinsTheLines()
    {
        Assert.That(SequenceRepository.parseFasta("NC_1.1", ">NC_1.1 test\nACGT\nTTGA\n"), Is.EqualTo("ACGTTTGA"));
    }

    [Test]
    public void WindowsLineEndings_AreHandled()
    {
        Assert.That(SequenceRepository.parseFasta("NC_1.1", ">h\r\nACGT\r\nTT\r\n"), Is.EqualTo("ACGTTT"));
    }

    [Test]
    public void LowerCase_IsUpperCased()
    {
        Assert.That(SequenceRepository.parseFasta("NC_1.1", ">h\nacgtnn\n"), Is.EqualTo("ACGTNN"));
    }

    [TestCase("")]
    [TestCase(">only a header")]
    [TestCase(">header\n\n")]                                                   // no bases
    [TestCase("<html><body>Error 500</body></html>\nsecond line")]               // an HTML error page
    [TestCase("{\"error\":\"bad id\"}\n{\"x\":1}")]                              // a JSON error
    [TestCase("ACGT\nACGT")]                                                    // no header
    [TestCase(">h\nACGT\n>second record\nTTTT\n")]                              // two records
    [TestCase(">h\nACGT1234\n")]                                                // digits
    [TestCase(">h\nAC GT\n")]                                                   // inner whitespace
    public void NotExactlyOneFastaRecord_IsAnUpstreamError(string body)
    {
        Assert.Throws<GrnaUpstreamException>(() => SequenceRepository.parseFasta("NC_1.1", body));
    }

    [Test]
    public void TheErrorNamesTheAccession()
    {
        var ex = Assert.Throws<GrnaUpstreamException>(() => SequenceRepository.parseFasta("NC_000099.9", "oops"))!;
        Assert.That(ex.Message, Does.Contain("NC_000099.9"));
    }

    [TestCase("")]
    [TestCase("12x")]
    [TestCase("rs334")]               // callers strip the "rs"
    [TestCase("334/../../x")]
    [TestCase("334?x=1")]
    public async Task Snp_NonNumericRsNumber_IsRejectedBeforeAnyRequest(string number)
    {
        Assert.ThrowsAsync<GrnaInputException>(async () =>
            await SNP.getHgvsNotationsAsync(number, CancellationToken.None));
        await Task.CompletedTask;
    }
}

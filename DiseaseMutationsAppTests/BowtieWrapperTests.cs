using gRNA;
using Microsoft.FSharp.Core;

namespace DiseaseMutationsAppTests;

/// <summary>
/// Process-level behaviour of the Bowtie runner that needs no real Bowtie: reads travel in a temp file (the
/// command-line list overflowed the OS argument limit for long variants), and an empty list never starts a process.
/// </summary>
public class BowtieWrapperTests
{
    private static GrnaEnvironment MissingBinaryEnvironment() =>
        new(
            bowtieBinary: Path.Combine(Path.GetTempPath(), "no-such-dir", "no-such-bowtie"),
            bowtieIndexBase: FSharpOption<string>.Some("unused-index-base"),
            bowtieIndexDirectory: "unused",
            pythonExecutable: "python3",
            processTimeout: TimeSpan.FromSeconds(5),
            ncbiApiKey: FSharpOption<string>.None,
            ncbiContact: FSharpOption<string>.None,
            sequenceCacheEntries: 8);

    private static int ReadsFilesInTemp() =>
        Directory.GetFiles(Path.GetTempPath(), "grna-reads-*.txt").Length;

    private static Microsoft.FSharp.Collections.FSharpList<string> Reads(int count) =>
        Microsoft.FSharp.Collections.ListModule.OfSeq(Enumerable.Range(0, count).Select(_ => "ACGTACGTACGTACGTACGTACGTACGT"));

    [Test]
    public async Task EmptyReadList_ReturnsNoAlignments_WithoutStartingBowtie()
    {
        var result = await BowtieWrapper.runBowtieK(MissingBinaryEnvironment(), 6, 2, 2, Reads(0), CancellationToken.None);
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void BinaryThatCannotStart_IsADependencyError_AndLeavesNoTempFile()
    {
        var before = ReadsFilesInTemp();

        Assert.ThrowsAsync<GrnaDependencyException>(async () =>
            await BowtieWrapper.runBowtieK(MissingBinaryEnvironment(), 6, 2, 2, Reads(3), CancellationToken.None));

        Assert.That(ReadsFilesInTemp(), Is.EqualTo(before));
    }

    [Test]
    public void VeryLongReadList_DoesNotFailOnArgumentLength()
    {
        // 6,000 reads of 28 nt were about 175 KB of arguments, above the Linux per-argument limit of 128 KB.
        // With the reads in a file the only failure left is the missing binary, reported as a dependency error.
        var ex = Assert.ThrowsAsync<GrnaDependencyException>(async () =>
            await BowtieWrapper.runBowtieK(MissingBinaryEnvironment(), 6, 2, 2, Reads(6000), CancellationToken.None));

        Assert.That(ex!.Message, Does.Contain("Cannot start Bowtie").And.Not.Contain("too long"));
    }
}

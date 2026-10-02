using System.Diagnostics;
using gRNA;

namespace DiseaseMutationsAppTests;

/// <summary>
/// Runs the real ViennaRNA through Python. Skipped automatically when python3 or the RNA module is
/// unavailable (CI without the native dependency); runs in the container and on dev machines with ViennaRNA.
/// </summary>
public class RnaFoldIntegrationTests
{
    private static bool ViennaAvailable()
    {
        try
        {
            var psi = new ProcessStartInfo("python3") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add("import RNA");
            using var p = Process.Start(psi)!;
            p.WaitForExit(20000);
            return p.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    [Test]
    public async Task FoldMany_ReturnsStructuresInInputOrder()
    {
        if (!ViennaAvailable()) Assert.Ignore("python3 with ViennaRNA is not available.");

        var results = (await RNAFoldWrapper.foldMany(
            Microsoft.FSharp.Collections.ListModule.OfArray(new[] { "GCGCAAAAGCGC", "AAAAAAAAAAAA" }),
            CancellationToken.None)).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(results, Has.Length.EqualTo(2));
            Assert.That(results[0].Structure, Is.EqualTo("((((....))))"));
            Assert.That(results[0].Energy, Is.LessThan(0));
            Assert.That(results[1].Structure, Is.EqualTo("............"));
            Assert.That(results[1].Energy, Is.EqualTo(0).Within(1e-9));
        });
    }

    [Test]
    public async Task Fold_CancelledToken_Throws()
    {
        if (!ViennaAvailable()) Assert.Ignore("python3 with ViennaRNA is not available.");
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.CatchAsync<OperationCanceledException>(async () => await RNAFoldWrapper.fold("GCGC", cts.Token));
        await Task.CompletedTask;
    }
}

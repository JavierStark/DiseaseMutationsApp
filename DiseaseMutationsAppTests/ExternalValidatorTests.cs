using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.FSharp.Collections;
using FsExport = gRNA.Export;

namespace DiseaseMutationsAppTests;

/// <summary>
/// Reads Diana's GenBank and SBOL 3 files with the reference libraries (Biopython's SeqIO and pySBOL3, whose validator
/// must report no error and no warning) and compares what they understood with the layout the writers were given.
/// Skipped when Python or either library is missing (CI), like the ViennaRNA tests. To run it:
/// <c>python -m pip install biopython sbol3</c> (set GRNA_PYTHON to pick the interpreter).
/// </summary>
public class ExternalValidatorTests
{
    private static (int Exit, string Out, string Err) RunValidator(string gb, string xml)
    {
        var script = Path.Combine(AppContext.BaseDirectory, "Validators", "validate_export.py");
        var chosen = Environment.GetEnvironmentVariable("GRNA_PYTHON");
        var names = string.IsNullOrWhiteSpace(chosen) ? new[] { "python3", "python" } : new[] { chosen };
        foreach (var exe in names)
        {
            try
            {
                var psi = new ProcessStartInfo(exe) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
                foreach (var a in new[] { "-I", script, gb, xml }) psi.ArgumentList.Add(a);
                using var proc = Process.Start(psi)!;
                var stdout = proc.StandardOutput.ReadToEndAsync();
                var stderr = proc.StandardError.ReadToEndAsync();
                if (!proc.WaitForExit(120000)) { proc.Kill(true); return (-1, "", "timeout"); }
                // A Windows "python3" stub that only opens the Store exits non-zero without importing anything.
                if (proc.ExitCode != 0 && proc.ExitCode != 3 && stdout.Result.Length == 0 && stderr.Result.Contains("Python was not found")) continue;
                return (proc.ExitCode, stdout.Result, stderr.Result);
            }
            catch (Win32Exception) { /* try the next name */ }
        }
        return (3, "", "no python interpreter");
    }

    [Test]
    public void ReferenceLibraries_ReadBothFiles_AndAgreeWithTheLayout()
    {
        var dir = Path.Combine(Path.GetTempPath(), "grna-ext-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var combos = new[]
            {
                (ExportFixtures.Construct(), false),
                (ExportFixtures.Construct(frida: true, bioBricks: true, c3: "AAGG"), true),
                (ExportFixtures.Construct(dr: "none", c5: "GG"), true),
            };
            foreach (var (construct, t7) in combos)
            {
                var o = ExportFixtures.Options(construct, t7);
                var guides = new[] { ExportFixtures.Guide(), ExportFixtures.Guide("NC_000017.11:g.7674220C>T", true, "ACGUACGUACGUACGUACGUACGUACGU") };
                var gb = Path.Combine(dir, "t.gb");
                var xml = Path.Combine(dir, "t.xml");
                File.WriteAllText(gb, FsExport.toGenBank(o, ListModule.OfArray(guides)));
                File.WriteAllText(xml, FsExport.toSbol3(o, ListModule.OfArray(guides)));

                var (exit, stdout, stderr) = RunValidator(gb, xml);
                if (exit == 3) Assert.Ignore("Python with Biopython and sbol3 is not available: " + stderr.Trim());
                Assert.That(exit, Is.EqualTo(0), stderr);

                using var json = JsonDocument.Parse(stdout);
                var root = json.RootElement;
                var records = root.GetProperty("genbank").EnumerateArray().ToList();
                var components = root.GetProperty("sbolComponents").EnumerateArray().ToList();

                Assert.Multiple(() =>
                {
                    Assert.That(records, Has.Count.EqualTo(guides.Length));
                    Assert.That(components, Has.Count.EqualTo(guides.Length));
                    Assert.That(root.GetProperty("genbankRoundTripOk").GetBoolean(), Is.True);
                    Assert.That(root.GetProperty("sbolRoundTripOk").GetBoolean(), Is.True);
                    Assert.That(root.GetProperty("sbolErrors").GetArrayLength(), Is.EqualTo(0), root.GetProperty("sbolErrors").ToString());
                    Assert.That(root.GetProperty("sbolWarnings").GetArrayLength(), Is.EqualTo(0), root.GetProperty("sbolWarnings").ToString());

                    for (var i = 0; i < guides.Length; i++)
                    {
                        var segments = FsExport.layout(o, guides[i]).Select(s => (s.Start, s.End)).ToList();
                        var rec = records[i];
                        Assert.That(rec.GetProperty("sequence").GetString(), Is.EqualTo(FsExport.sequence(o, guides[i])));
                        Assert.That(rec.GetProperty("topology").GetString(), Is.EqualTo("linear"));
                        Assert.That(rec.GetProperty("division").GetString(), Is.EqualTo("SYN"));
                        // first feature is "source"; the rest follow the layout
                        var feats = rec.GetProperty("features").EnumerateArray().Skip(1)
                            .Select(f => (f.GetProperty("start").GetInt32(), f.GetProperty("end").GetInt32())).ToList();
                        Assert.That(feats, Is.EqualTo(segments));
                    }

                    // pySBOL3 lists components sorted by displayId, which can differ from input order: match by sequence.
                    foreach (var g in guides)
                    {
                        var dna = FsExport.sequence(o, g).ToUpperInvariant();
                        var match = components.Single(c => c.GetProperty("sequence").GetString() == dna);
                        var ranges = match.GetProperty("features").EnumerateArray()
                            .Select(f => f.GetProperty("ranges")[0]).Select(r => (r.GetProperty("start").GetInt32(), r.GetProperty("end").GetInt32()))
                            .OrderBy(r => r.Item1).ToList();
                        Assert.That(ranges, Is.EqualTo(FsExport.layout(o, g).Select(s => (s.Start, s.End)).ToList()));
                    }
                });
            }
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* best effort */ }
        }
    }
}

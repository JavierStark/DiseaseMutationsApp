using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using DiseaseMutationsApp.Services;
using CliProgram = gRNA.Cli.Program;
using FsExport = gRNA.Export;

namespace DiseaseMutationsAppTests;

/// <summary>`grna export` driven in-process: no network, no Bowtie, no ViennaRNA.</summary>
[NonParallelizable] // Console.SetOut / SetError are process-wide
public class CliExportTests
{
    private const string SpacerA = "CAGAGCGCGCAGCAGCAGCAGCAGAAGC";
    private const string SpacerB = "ACGUACGUACGUACGUACGUACGUACGU";
    private string _dir = "";

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "grna-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_dir, true); } catch { /* best effort */ }
    }

    private static (int Exit, string Out, string Err) Run(params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var oldOut = Console.Out;
        var oldErr = Console.Error;
        Console.SetOut(stdout);
        Console.SetError(stderr);
        try
        {
            var exit = CliProgram.run(args, CancellationToken.None).GetAwaiter().GetResult();
            return (exit, stdout.ToString(), stderr.ToString());
        }
        finally
        {
            Console.SetOut(oldOut);
            Console.SetError(oldErr);
        }
    }

    private string Write(string name, string content)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, content, new UTF8Encoding(false));
        return path;
    }

    /// <summary>A session exactly as the web app saves it.</summary>
    private string Session(ConstructOptions? construct = null, params ShortlistItem[] items) =>
        Write("session.json", SessionDocument.Serialize(new SessionDocument(SessionDocument.CurrentVersion, "rs334", 28, "10-17",
            items.Length == 0 ? new() { new ShortlistItem("NG_008690.2:g.5000A>T", false, SpacerA, "rs334", 0.8, 1), new ShortlistItem("NC_000017.11:g.7674220C>T", true, SpacerB, null, 1, 0) } : items.ToList(),
            construct)));

    private string Report() => Write("report.csv", string.Join("\n",
        "# Diana report; spacer=28; seed=10-17; generated=2026-10-19T00:00:00Z",
        "RS ID,HGVS,Sequence Type,Rank,Sequence,Score,GC Content,Offtargets,Seed Region,Homopolymers,Fold Energy,Strand,WT Mismatches,Engineered,Scaffold Intact,Spacer Unpaired",
        $"rs334,NG_008690.2:g.5000A>T,Mutated,1,{SpacerA},1,60.7,1,ACGUACGU,0,-3.5,Normal,3,Yes,Yes,0.8",
        $"rs334,NG_008690.2:g.5000A>T,Mutated,2,{SpacerB},0.5,50,2,ACGUACGU,0,-2.5,Normal,3,No,Yes,0.8",
        $"rs334,NG_008690.2:g.5000A>T,Original,1,{SpacerB},1,50,2,ACGUACGU,0,-2.5,Normal,3,No,Yes,0.8",
        $",NC_000017.11:g.7674220C>T,Mutated,1,{SpacerB},1,50,0,ACGUACGU,0,-2.5,Complement,2,No,Yes,0.8") + "\n");

    [Test]
    public void Session_ToGenBank_UsesTheShortlistAndTheConstructOfTheWebApp()
    {
        var construct = new ConstructOptions(Frida: true, BioBricks: true);
        var r = Run("export", Session(construct), "--t7", "--date", "2026-10-19");

        var o = new gRNA.Export.ExportOptions(ExportFixtures.Construct(frida: true, bioBricks: true), true, FsExport.DefaultNamespace, new DateTime(2026, 10, 19));
        Assert.Multiple(() =>
        {
            Assert.That(r.Exit, Is.EqualTo(0), r.Err);
            Assert.That(Regex.Matches(r.Out, @"(?m)^LOCUS ").Count, Is.EqualTo(2));
            Assert.That(r.Out, Does.Contain("19-OCT-2026").And.Contain("complement strand").And.Contain("rs334"));
            Assert.That(r.Out, Does.Contain(FsExport.sequence(o, ExportFixtures.Guide(spacer: SpacerA)).ToLowerInvariant().Substring(0, 10)));
            Assert.That(r.Err, Does.Contain("exported 2 guides as GenBank").And.Contain("Frida + BioBricks").And.Contain("T7"));
        });
    }

    [Test]
    public void Session_ToSbol_WritesOneComponentPerGuide()
    {
        var r = Run("export", Session(), "--format", "sbol");
        var doc = XDocument.Parse(r.Out);
        XNamespace sbol = "http://sbols.org/v3#";
        Assert.Multiple(() =>
        {
            Assert.That(r.Exit, Is.EqualTo(0), r.Err);
            Assert.That(doc.Root!.Elements(sbol + "Component").Count(), Is.EqualTo(2));
            Assert.That(r.Err, Does.Contain("SBOL 3"));
        });
    }

    [Test]
    public void Session_ToSbol2_WritesSbol2Definitions()
    {
        var r = Run("export", Session(), "--format", "sbol2");
        XNamespace s2 = "http://sbols.org/v2#";
        Assert.Multiple(() =>
        {
            Assert.That(r.Exit, Is.EqualTo(0), r.Err);
            Assert.That(XDocument.Parse(r.Out).Root!.Elements(s2 + "ComponentDefinition").Count(), Is.EqualTo(2));
            Assert.That(r.Err, Does.Contain("SBOL 2"));
        });
    }

    [Test]
    public void Report_TakesTheFirstMutatedGuidePerVariantAndStrand()
    {
        var r = Run("export", Report(), "--date", "2026-10-19");
        Assert.Multiple(() =>
        {
            Assert.That(r.Exit, Is.EqualTo(0), r.Err);
            Assert.That(Regex.Matches(r.Out, @"(?m)^LOCUS ").Count, Is.EqualTo(2), "one per variant and strand; Original rows are never exported");
            Assert.That(r.Out, Does.Contain("Rank: 1.").And.Contain("Offtargets (GRCh38, up to 2 mismatches, 6 = 6 or more): 1."));
            Assert.That(r.Out, Does.Contain(SpacerA).And.Not.Contain("Rank: 2."));
        });
    }

    [Test]
    public void Report_Top2_TakesTwoGuidesOfTheFirstVariant()
    {
        var r = Run("export", Report(), "--top", "2");
        Assert.That(Regex.Matches(r.Out, @"(?m)^LOCUS ").Count, Is.EqualTo(3));
    }

    [Test]
    public void Report_ConstructFlags_BuildTheConstruct()
    {
        var withFlags = Run("export", Report(), "--frida", "--dr", "none", "--custom3", "aagg", "--t7", "--date", "2026-10-19");
        var spacerOnly = Run("export", Report(), "--dr", "none", "--date", "2026-10-19");
        Assert.Multiple(() =>
        {
            Assert.That(withFlags.Exit, Is.EqualTo(0), withFlags.Err);
            Assert.That(withFlags.Out, Does.Contain("Frida adapter 5'").And.Contain("T7 promoter").And.Contain("Custom 3'"));
            Assert.That(withFlags.Out, Does.Not.Contain("DR\""));
            Assert.That(spacerOnly.Out, Does.Not.Contain("Frida").And.Not.Contain("T7 promoter"));
        });
    }

    [Test]
    public void Out_WritesAFile_AndKeepsStdoutEmpty()
    {
        var path = Path.Combine(_dir, "out.gb");
        var r = Run("export", Session(), "--out", path);
        Assert.Multiple(() =>
        {
            Assert.That(r.Exit, Is.EqualTo(0), r.Err);
            Assert.That(r.Out, Is.Empty);
            Assert.That(File.ReadAllText(path), Does.StartWith("LOCUS "));
            Assert.That(File.ReadAllBytes(path).Take(3), Is.Not.EqualTo(new byte[] { 0xEF, 0xBB, 0xBF }), "no BOM");
        });
    }

    [Test]
    public void Namespace_IsUsedForSbolIdentities()
    {
        var r = Run("export", Session(), "--format", "sbol", "--namespace", "https://example.org/lab/");
        Assert.That(r.Out, Does.Contain("rdf:about=\"https://example.org/lab/NG_008690_2_g_5000A_T\""));
    }

    private static readonly object[] BadInputs =
    {
        new object[] { new[] { "export" }, "exactly one file" },
        new object[] { new[] { "export", "a", "b" }, "exactly one file" },
        new object[] { new[] { "export", "missing.json" }, "not found" },
    };

    [TestCaseSource(nameof(BadInputs))]
    public void MissingOrWrongFiles_ExitWithBadInput(string[] args, string message)
    {
        var r = Run(args);
        Assert.Multiple(() =>
        {
            Assert.That(r.Exit, Is.EqualTo(CliProgram.ExitBadInput));
            Assert.That(r.Err, Does.Contain(message));
            Assert.That(r.Out, Is.Empty);
        });
    }

    [Test]
    public void BadOptions_ExitWithBadInput_BeforeAnyOutput()
    {
        var session = Session();
        var report = Report();
        var cases = new (string[] Args, string Message)[]
        {
            (new[] { "export", session, "--format", "pdf" }, "--format must be genbank, sbol (SBOL 3) or sbol2"),
            (new[] { "export", report, "--top", "0" }, "--top must be 1 or more"),
            (new[] { "export", report, "--top", "x" }, "--top must be an integer"),
            (new[] { "export", report, "--dr", "cas12" }, "--dr must be"),
            (new[] { "export", report, "--custom5", "ACGX" }, "--custom5 may only contain"),
            (new[] { "export", report, "--date", "19/10/2026" }, "--date must look like"),
            (new[] { "export", report, "--format", "sbol", "--namespace", "not a url" }, "not a usable namespace"),
            (new[] { "export", session, "--frida" }, "cannot be combined with a session"),
            (new[] { "export", session, "--dr", "none" }, "cannot be combined with a session"),
            (new[] { "export", session, "--top", "2" }, "--top applies to a design report"),
            (new[] { "export", report, "--bogus", "1" }, "Unknown option --bogus"),
            (new[] { "export", report, "--complement" }, "does not apply to 'export'"),
        };
        foreach (var (args, message) in cases)
        {
            var r = Run(args);
            Assert.That(r.Exit, Is.EqualTo(CliProgram.ExitBadInput), string.Join(" ", args));
            Assert.That(r.Err, Does.Contain(message), string.Join(" ", args));
            Assert.That(r.Out, Is.Empty, string.Join(" ", args));
        }
    }

    [Test]
    public void FilesThatAreNotSessionsOrReports_AreRefused()
    {
        var notReport = Write("list.txt", "rs334\nrs28929474\n");
        var emptyShortlist = Write("empty.json", SessionDocument.Serialize(new SessionDocument(1, "rs1", 28, "10-17", new())));
        var brokenJson = Write("broken.json", "{ not json");
        var badSpacer = Session(null, new ShortlistItem("NG_1:g.1A>T", false, "ACGX", null, 1, 1));
        var noMutated = Write("orig.csv", "RS ID,HGVS,Sequence Type,Rank,Sequence\nrs1,NG_1:g.1A>T,Original,1," + SpacerA + "\n");

        foreach (var (file, message) in new[]
        {
            (notReport, "neither a session file nor a Diana design report"),
            (emptyShortlist, "shortlist of"),
            (brokenJson, "not a valid session file"),
            (badSpacer, "letters other than A, C, G, T, U"),
            (noMutated, "No Mutated guides"),
        })
        {
            var r = Run("export", file);
            Assert.That(r.Exit, Is.EqualTo(CliProgram.ExitBadInput), file);
            Assert.That(r.Err, Does.Contain(message), file);
            Assert.That(r.Out, Is.Empty, file);
        }
    }

    [Test]
    public void Help_MentionsExport()
    {
        var r = Run("export", "--help");
        Assert.Multiple(() =>
        {
            Assert.That(r.Exit, Is.EqualTo(0));
            Assert.That(r.Out, Does.Contain("grna export"));
        });
    }
}

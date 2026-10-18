using System.Text;
using gRNA;
using CliProgram = gRNA.Cli.Program;

namespace DiseaseMutationsAppTests;

/// <summary>
/// Drives `grna` in-process through CliProgram.run and checks exit codes and messages. Every case here fails (or succeeds)
/// before any NCBI or Bowtie work, so no network and no native tools are needed.
/// </summary>
[NonParallelizable] // Console.SetOut / SetError are process-wide
public class CliTests
{
    private string _dir = "";

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "grna-cli-" + Guid.NewGuid().ToString("N"));
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

    private string File_(string name, string content)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, content, new UTF8Encoding(false));
        return path;
    }

    // ---- help and usage ----------------------------------------------------------------------------

    [Test]
    public void NoArguments_PrintsUsage_AndExitsWithBadInput()
    {
        var r = Run();
        Assert.Multiple(() =>
        {
            Assert.That(r.Exit, Is.EqualTo(CliProgram.ExitBadInput));
            Assert.That(r.Out, Does.Contain("grna design"));
        });
    }

    [TestCase("--help")]
    [TestCase("help")]
    [TestCase("-h")]
    public void TopLevelHelp_ExitsZero(string flag)
    {
        var r = Run(flag);
        Assert.Multiple(() =>
        {
            Assert.That(r.Exit, Is.EqualTo(CliProgram.ExitOk));
            Assert.That(r.Out, Does.Contain("Usage:"));
        });
    }

    [TestCase("design", "--help")]
    [TestCase("design", "-h")]
    [TestCase("pool", "--help")]
    [TestCase("doctor", "--help")]
    public void SubcommandHelp_ExitsZero_InsteadOfComplainingAboutArguments(string command, string flag)
    {
        var r = Run(command, flag);
        Assert.Multiple(() =>
        {
            Assert.That(r.Exit, Is.EqualTo(CliProgram.ExitOk));
            Assert.That(r.Out, Does.Contain("Usage:"));
        });
    }

    [Test]
    public void UnknownCommand_IsBadInput()
    {
        Assert.That(Run("bogus").Exit, Is.EqualTo(CliProgram.ExitBadInput));
    }

    // ---- unknown options are typos, not noise ------------------------------------------------------

    [Test]
    public void UnknownOption_IsRejected_AndNamed()
    {
        var r = Run("design", "--hgvs", "NC_000017.11:g.7674220C>T", "--bogus-flag", "1");
        Assert.Multiple(() =>
        {
            Assert.That(r.Exit, Is.EqualTo(CliProgram.ExitBadInput));
            Assert.That(r.Err, Does.Contain("--bogus-flag"));
        });
    }

    [Test]
    public void FlagThatDoesNotApplyToTheCommand_IsRejected()
    {
        var r = Run("pool", "--count", "5", "--complement");
        Assert.Multiple(() =>
        {
            Assert.That(r.Exit, Is.EqualTo(CliProgram.ExitBadInput));
            Assert.That(r.Err, Does.Contain("--complement"));
        });
    }

    // ---- design: input validation -------------------------------------------------------------------

    [TestCase("17")]
    [TestCase("61")]
    [TestCase("0")]
    [TestCase("-5")]
    public void Design_SpacerOutOfRange_IsBadInput(string spacer)
    {
        var r = Run("design", "--hgvs", "NC_000017.11:g.7674220C>T", "--spacer", spacer);
        Assert.Multiple(() =>
        {
            Assert.That(r.Exit, Is.EqualTo(CliProgram.ExitBadInput));
            Assert.That(r.Err, Does.Contain("between 18 and 60"));
        });
    }

    [TestCase("--snp-pos", "0")]
    [TestCase("--snp-pos", "29")]
    [TestCase("--mismatch-pos", "0")]
    [TestCase("--mismatch-pos", "29")]
    [TestCase("--mismatch-pos", "3")]   // equal to the default variant position
    public void Design_SubstitutionRulePositionsOutOfRange_AreBadInput(string option, string value)
    {
        var r = Run("design", "--hgvs", "NC_000017.11:g.7674220C>T", option, value);
        Assert.Multiple(() =>
        {
            Assert.That(r.Exit, Is.EqualTo(CliProgram.ExitBadInput));
            Assert.That(r.Err, Does.Contain("position").Or.Contain("different positions"));
        });
    }

    [Test]
    public void Design_SubstitutionRuleOptionsNeedAValue_AndAreListedInHelp()
    {
        Assert.That(Run("design", "--hgvs", "x", "--snp-pos").Exit, Is.EqualTo(CliProgram.ExitBadInput));
        var help = Run("--help");
        Assert.Multiple(() =>
        {
            Assert.That(help.Out, Does.Contain("--snp-pos"));
            Assert.That(help.Out, Does.Contain("--mismatch-pos"));
        });
    }

    [TestCase("20-5")]
    [TestCase("10-28")]
    [TestCase("abc")]
    [TestCase("10")]
    public void Design_BadSeed_IsBadInput(string seed)
    {
        Assert.That(Run("design", "--hgvs", "NC_000017.11:g.7674220C>T", "--seed", seed).Exit, Is.EqualTo(CliProgram.ExitBadInput));
    }

    [TestCase("NC_000017.11")]                     // no colon
    [TestCase("NC_000017.11:g.7674220A>A")]        // not a change
    [TestCase("NC_000017.11:g.99999999999C>T")]    // overflow
    [TestCase("rs")]                               // a bare "rs" used to abort the process
    [TestCase("rs12x")]
    public void Design_MalformedVariant_IsBadInput_NotACrash(string variant)
    {
        var r = Run("design", "--hgvs", variant);
        Assert.Multiple(() =>
        {
            Assert.That(r.Exit, Is.EqualTo(CliProgram.ExitBadInput), r.Err);
            Assert.That(r.Err, Does.Not.Contain("Unhandled exception"));
            Assert.That(r.Err, Does.Not.Contain("outside the bounds"));
        });
    }

    [Test]
    public void Design_EmptyInputFile_IsBadInput_NotSilentSuccess()
    {
        var empty = File_("empty.txt", "");
        var r = Run("design", "--input", empty, "--out", Path.Combine(_dir, "out.csv"));
        Assert.Multiple(() =>
        {
            Assert.That(r.Exit, Is.EqualTo(CliProgram.ExitBadInput));
            Assert.That(r.Err, Does.Contain("No variants found"));
        });
    }

    [Test]
    public void Design_MissingInputFile_IsBadInput()
    {
        Assert.That(Run("design", "--input", Path.Combine(_dir, "nope.txt")).Exit, Is.EqualTo(CliProgram.ExitBadInput));
    }

    [Test]
    public void Design_ExitCodeOfAllFailed_IsTheFirstFailureInInputOrder()
    {
        var list = File_("list.txt", "NC_000017.11\nNC_000017.11:g.7674220A>A\n");
        var r = Run("design", "--input", list, "--out", Path.Combine(_dir, "out.csv"));
        Assert.That(r.Exit, Is.EqualTo(CliProgram.ExitBadInput));
    }

    // ---- design: resume ------------------------------------------------------------------------------

    [Test]
    public void Resume_EmptyExistingReport_DoesNotCrash()
    {
        var list = File_("list.txt", "NC_000017.11\n");
        var out_ = File_("out.csv", "");
        var r = Run("design", "--input", list, "--out", out_);
        Assert.That(r.Exit, Is.EqualTo(CliProgram.ExitBadInput), r.Err);
        Assert.That(r.Err, Does.Not.Contain("Unhandled"));
    }

    [Test]
    public void Resume_ReportMadeWithOtherParameters_IsRefused()
    {
        var list = File_("list.txt", "NC_000017.11:g.7674220C>T\n");
        var out_ = File_("out.csv", "# Diana report; spacer=30; seed=10-17; generated=2026-01-01T00:00:00Z\n" + ReportSchema.header + "\n");
        var r = Run("design", "--input", list, "--out", out_);
        Assert.Multiple(() =>
        {
            Assert.That(r.Exit, Is.EqualTo(CliProgram.ExitBadInput));
            Assert.That(r.Err, Does.Contain("spacer=30").And.Contain("spacer=28"));
        });
    }

    [Test]
    public void Resume_VariantAlreadyInTheReport_IsSkipped_WithoutAnyWork()
    {
        var list = File_("list.txt", "NC_000017.11:g.7674220C>T\n");
        var report = "# Diana report; spacer=28; seed=10-17; generated=2026-01-01T00:00:00Z\n" + ReportSchema.header + "\n"
                     + ",NC_000017.11:g.7674220C>T,Mutated,1,AGUUCCUGCAUGGGCGGCAUGAACAAGA,1,53.57,1,UGGGCGGC,0,-13.1,Normal\n";
        var out_ = File_("out.csv", report);

        var r = Run("design", "--input", list, "--out", out_);

        Assert.Multiple(() =>
        {
            Assert.That(r.Exit, Is.EqualTo(CliProgram.ExitOk), r.Err);
            Assert.That(File.ReadAllText(out_), Is.EqualTo(report), "nothing new, nothing appended");
        });
    }

    // ---- resolve / fold ------------------------------------------------------------------------------

    [TestCase("rs")]
    [TestCase("abc")]
    [TestCase("rs12x")]
    public void Resolve_NotAnRsId_IsBadInput_BeforeAnyRequest(string id)
    {
        var r = Run("resolve", id);
        Assert.Multiple(() =>
        {
            Assert.That(r.Exit, Is.EqualTo(CliProgram.ExitBadInput));
            Assert.That(r.Err, Does.Contain("not an rsID"));
        });
    }

    [TestCase("ACGTNN")]
    [TestCase("")]
    [TestCase("hello")]
    public void Fold_InvalidSequence_IsBadInput(string sequence)
    {
        Assert.That(Run("fold", sequence).Exit, Is.EqualTo(CliProgram.ExitBadInput));
    }

    [TestCase("rs334", true)]
    [TestCase("RS334", true)]
    [TestCase("rs", false)]
    [TestCase("rs12x", false)]
    [TestCase("334", false)]
    [TestCase("", false)]
    public void IsRsId(string input, bool expected)
    {
        Assert.That(CliProgram.isRsId(input), Is.EqualTo(expected));
    }

    // ---- pool ----------------------------------------------------------------------------------------

    [Test]
    public void Pool_CapacityZero_IsBadInput_NotACrash()
    {
        var r = Run("pool", "--count", "10", "--capacity", "0");
        Assert.Multiple(() =>
        {
            Assert.That(r.Exit, Is.EqualTo(CliProgram.ExitBadInput), r.Err);
            Assert.That(r.Err, Does.Contain("at least 1"));
            Assert.That(r.Err, Does.Not.Contain("Unhandled"));
        });
    }

    [TestCase("--capacity", "abc")]
    [TestCase("--capacity", "-3")]
    [TestCase("--plate", "12")]
    [TestCase("--model", "4d")]
    public void Pool_BadOption_IsBadInput(string option, string value)
    {
        Assert.That(Run("pool", "--count", "10", option, value).Exit, Is.EqualTo(CliProgram.ExitBadInput));
    }

    [TestCase("0")]
    [TestCase("-1")]
    [TestCase("100001")]
    [TestCase("2147483647")]   // used to try to allocate two billion labels
    public void Pool_CountOutOfRange_IsBadInput(string count)
    {
        Assert.That(Run("pool", "--count", count).Exit, Is.EqualTo(CliProgram.ExitBadInput));
    }

    [Test]
    public void Pool_Count_WritesAPlanCsv()
    {
        var r = Run("pool", "--count", "12", "--capacity", "5");
        var lines = r.Out.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r')).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(r.Exit, Is.EqualTo(CliProgram.ExitOk), r.Err);
            Assert.That(lines[0], Is.EqualTo("Tube ID,Guides in Tandem,Well"));
            Assert.That(lines.Length, Is.GreaterThan(1));
            Assert.That(r.Err, Does.Contain("V=12"));
        });
    }

    [Test]
    public void Pool_FromAReportWithBothStrands_UsesOneGuidePerVariant()
    {
        var report = "# Diana report; spacer=28; seed=10-17; generated=2026-01-01T00:00:00Z\n" + ReportSchema.header + "\n"
                     + ",NC_1.1:g.5C>T,Mutated,1,AAAA,1,50,1,AAAA,0,-1,Normal\n"
                     + ",NC_1.1:g.5C>T,Mutated,1,CCCC,1,50,0,CCCC,0,-1,Complement\n"
                     + ",NC_1.1:g.9G>A,Mutated,1,GGGG,1,50,1,GGGG,0,-1,Normal\n";
        var r = Run("pool", "--guides", File_("report.csv", report), "--capacity", "5");
        Assert.Multiple(() =>
        {
            Assert.That(r.Exit, Is.EqualTo(CliProgram.ExitOk), r.Err);
            Assert.That(r.Err, Does.Contain("V=2"));
        });
    }

    // ---- option checking ----------------------------------------------------------------------------

    [Test]
    public void CheckKnownOptions_AcceptsWhatTheCommandTakes()
    {
        var args = CliProgram.parseArgs(Microsoft.FSharp.Collections.ListModule.OfSeq(new[] { "--hgvs", "x", "--complement", "--spacer", "28" }));
        Assert.That(() => CliProgram.checkKnownOptions("design", args), Throws.Nothing);
    }

    [Test]
    public void CheckKnownOptions_NamesTheUnknownOption()
    {
        var args = CliProgram.parseArgs(Microsoft.FSharp.Collections.ListModule.OfSeq(new[] { "--hgvs", "x", "--spaser", "28" }));
        Assert.That(() => CliProgram.checkKnownOptions("design", args), Throws.Exception.Message.Contains("--spaser"));
    }
}

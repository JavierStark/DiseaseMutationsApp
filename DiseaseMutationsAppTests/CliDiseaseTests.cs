using System.Net;
using gRNA;
using static DiseaseMutationsAppTests.Stubs;
using CliProgram = gRNA.Cli.Program;

namespace DiseaseMutationsAppTests;

/// <summary>
/// `grna disease` driven in-process over a stubbed Monarch/NCBI layer: option checking, output formats, exit codes and the notes
/// written to stderr. Nothing here touches the network.
/// </summary>
[NonParallelizable] // Console.SetOut / SetError and the CLI's client are process-wide
public class CliDiseaseTests
{
    private string _dir = "";
    private DiseaseVariants.DiseaseVariantClient _savedClient = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "grna-cli-disease-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _savedClient = CliProgram.diseaseClient;
    }

    [TearDown]
    public void TearDown()
    {
        CliProgram.diseaseClient = _savedClient;
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

    /// <summary>Monarch searches answer with the Fanconi fixture, entities with the Fanconi tree, NCBI with the PKU walk-through.</summary>
    private static Rig UseRig(Func<Seen, HttpResponseMessage>? monarch = null)
    {
        var rig = new Rig();
        var ncbi = NcbiPku();
        var tree = FanconiTree();
        rig.Respond = seen =>
        {
            if (seen.Url.Host == "eutils.test") return ncbi(seen);
            if (monarch is not null) return monarch(seen);
            return seen.Url.AbsolutePath.EndsWith("/search")
                ? Json(DiseaseFixtures.Load("monarch_search_fanconi.json"))
                : tree(seen);
        };
        CliProgram.diseaseClient = rig.Client;
        return rig;
    }

    // ---- everything refused before the first request ---------------------------------------------------

    [TestCase(new string[0], "needs a disease name")]
    [TestCase(new[] { "one", "two" }, "one search text")]
    [TestCase(new[] { "x", "--format", "xml" }, "--format must be")]
    [TestCase(new[] { "x", "--min-significance", "maybe" }, "--min-significance must be")]
    [TestCase(new[] { "x", "--pick", "0" }, "--pick must be 1 or more")]
    [TestCase(new[] { "x", "--pick", "abc" }, "--pick must be an integer")]
    [TestCase(new[] { "OMIM:12ab" }, "not an OMIM number")]
    [TestCase(new[] { "22" }, "not an OMIM number")]
    [TestCase(new[] { "MONDO:123456789" }, "not a MONDO id")]
    [TestCase(new[] { "x", "--bogus", "1" }, "Unknown option --bogus")]
    [TestCase(new[] { "x", "--complement" }, "does not apply to 'disease'")]
    [TestCase(new[] { "x", "--format" }, "needs a value")]
    public void BadArguments_ExitWithBadInput_AndMakeNoRequest(string[] extra, string message)
    {
        var rig = UseRig();

        var (exit, _, err) = Run(new[] { "disease" }.Concat(extra).ToArray());

        Assert.Multiple(() =>
        {
            Assert.That(exit, Is.EqualTo(2));
            Assert.That(err, Does.Contain(message));
            Assert.That(rig.Requests, Is.Empty);
        });
    }

    [Test]
    public void Help_ShowsTheCommand()
    {
        UseRig();
        var (exit, output, _) = Run("disease", "--help");
        Assert.That(exit, Is.EqualTo(0));
        Assert.That(output, Does.Contain("grna disease <name | OMIM number | MONDO id>"));
        Assert.That(Run("--help").Out, Does.Contain("grna disease"));
    }

    [Test]
    public void TheOptionsOfDisease_AreRefusedByOtherCommands()
    {
        UseRig();
        Assert.That(Run("resolve", "rs334", "--subtypes").Exit, Is.EqualTo(2));
        Assert.That(Run("design", "--hgvs", "NC_000017.11:g.7674220C>T", "--list").Exit, Is.EqualTo(2));
    }

    // ---- output ----------------------------------------------------------------------------------------

    [Test]
    public void ByOmimNumber_PrintsOneRsIdPerLine_AndReportsTheCountsOnStderr()
    {
        var rig = UseRig();

        var (exit, output, err) = Run("disease", "261600");

        Assert.Multiple(() =>
        {
            Assert.That(exit, Is.EqualTo(0));
            Assert.That(output, Is.EqualTo("rs2499617336\nrs334\n"));
            Assert.That(err, Does.Contain("923 ClinVar records matched; 6 listed, 2 with an rsID, 4 without."));
            Assert.That(err, Does.Contain("records without an rsID are left out"));
            Assert.That(err, Does.Contain("only part of the 923 matching records"), "the fixture search returned 5 ids of 923");
            Assert.That(rig.Monarch, Is.Empty, "an OMIM number needs no Monarch lookup");
        });
    }

    [Test]
    public void TheRsList_IsAcceptedByGrnaDesignAsAnInputFile()
    {
        UseRig();
        var file = Path.Combine(_dir, "rs.txt");

        var (exit, output, _) = Run("disease", "261600", "--out", file);

        Assert.Multiple(() =>
        {
            Assert.That(exit, Is.EqualTo(0));
            Assert.That(output, Is.Empty, "with --out nothing goes to stdout");
            Assert.That(File.ReadAllText(file), Is.EqualTo("rs2499617336\nrs334\n"));
            // The same text, read the way the web app and `grna design --input` read an uploaded list:
            Assert.That(DiseaseMutationsApp.Services.SessionDocument.InputFromUpload(File.ReadAllText(file)), Is.EqualTo("rs2499617336, rs334"));
        });
    }

    [Test]
    public void Csv_HasAHeader_AndOneRowPerVariant_IncludingThoseWithoutAnRsId()
    {
        UseRig();

        var (exit, output, _) = Run("disease", "OMIM:261600", "--format", "csv");

        var lines = output.TrimEnd('\n').Split('\n');
        Assert.Multiple(() =>
        {
            Assert.That(exit, Is.EqualTo(0));
            Assert.That(lines[0], Does.StartWith("rsID,HGVS,Gene,"));
            Assert.That(lines, Has.Length.EqualTo(7), "header plus 6 pathogenic records");
            Assert.That(lines.Count(l => l.StartsWith(",NC_")), Is.EqualTo(4), "no rsID, but the genomic HGVS is there");
        });
    }

    [Test]
    public void Tsv_AndJson_AreWellFormed()
    {
        UseRig();

        var tsv = Run("disease", "261600", "--format", "tsv").Out.TrimEnd('\n').Split('\n');
        var json = Run("disease", "261600", "--format", "json").Out;

        using var doc = System.Text.Json.JsonDocument.Parse(json);
        Assert.Multiple(() =>
        {
            Assert.That(tsv, Has.Length.EqualTo(7));
            Assert.That(tsv.All(l => l.Split('\t').Length == 9), Is.True);
            Assert.That(doc.RootElement.GetProperty("variants").GetArrayLength(), Is.EqualTo(6));
            Assert.That(doc.RootElement.GetProperty("totalMatching").GetInt32(), Is.EqualTo(923));
        });
    }

    [Test]
    public void MinSignificanceAny_ListsEveryRecord_AndIsSentToNcbi()
    {
        var rig = UseRig();

        var (_, output, _) = Run("disease", "261600", "--format", "csv", "--min-significance", "any");

        Assert.Multiple(() =>
        {
            Assert.That(output.TrimEnd('\n').Split('\n'), Has.Length.EqualTo(11));
            Assert.That(rig.Ncbi.First(r => r.Endpoint == "esearch.fcgi").Form["term"], Is.EqualTo("#1"));
        });
    }

    [Test]
    public void FormatNames_AreCaseInsensitive()
    {
        UseRig();
        Assert.That(Run("disease", "261600", "--format", "CSV").Exit, Is.EqualTo(0));
        Assert.That(Run("disease", "261600", "--min-significance", "Pathogenic").Exit, Is.EqualTo(0));
    }

    // ---- choosing among matches ------------------------------------------------------------------------

    [Test]
    public void List_PrintsTheMatches_AndSearchesNoVariants()
    {
        var rig = UseRig();

        var (exit, output, _) = Run("disease", "Fanconi anemia", "--list");

        var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        Assert.Multiple(() =>
        {
            Assert.That(exit, Is.EqualTo(0));
            Assert.That(lines, Has.Length.EqualTo(4));
            Assert.That(lines[0], Is.EqualTo("1\tMONDO:0019391\tFanconi anemia\tphenotypic series PS227650 (use --subtypes)"));
            Assert.That(lines[1], Is.EqualTo("2\tMONDO:0009213\tFanconi anemia complementation group C\tOMIM 227645"));
            Assert.That(lines[3], Is.EqualTo("4\tMONDO:7770734\tFanconi anemia, pig\tno OMIM number"));
            Assert.That(rig.Ncbi, Is.Empty);
        });
    }

    [Test]
    public void ANameWithSeveralMatches_UsesTheFirst_AndSaysSo_AndExpandsAPhenotypicSeries()
    {
        var rig = UseRig();

        var (exit, _, err) = Run("disease", "Fanconi anemia");

        Assert.Multiple(() =>
        {
            Assert.That(exit, Is.EqualTo(0));
            Assert.That(err, Does.Contain("using match 1 of 4: Fanconi anemia (MONDO:0019391)"));
            Assert.That(err, Does.Contain("consulted 22 of 22 subtypes"));
            Assert.That(rig.Ncbi.First(r => r.Endpoint == "elink.fcgi").Form["id"]!.Split(',').Length, Is.EqualTo(22));
        });
    }

    [Test]
    public void Pick_ChoosesAnotherMatch_WithoutTheNote()
    {
        var rig = UseRig();

        var (exit, _, err) = Run("disease", "Fanconi anemia", "--pick", "2");

        Assert.Multiple(() =>
        {
            Assert.That(exit, Is.EqualTo(0));
            Assert.That(err, Does.Not.Contain("using match"));
            Assert.That(rig.Ncbi.First(r => r.Endpoint == "elink.fcgi").Form["id"], Is.EqualTo("227645"));
        });
    }

    [Test]
    public void PickBeyondTheMatches_IsBadInput()
    {
        UseRig();
        var (exit, _, err) = Run("disease", "Fanconi anemia", "--pick", "9");
        Assert.That(exit, Is.EqualTo(2));
        Assert.That(err, Does.Contain("only 4 matches"));
    }

    [Test]
    public void Subtypes_ExpandsADiseaseThatHasItsOwnOmimNumberToo()
    {
        var rig = UseRig();

        // Match 2 (group C) has its own number; --subtypes makes the command ask Monarch for its subtypes as well.
        Run("disease", "Fanconi anemia", "--pick", "2", "--subtypes");

        Assert.That(rig.Monarch.Any(r => r.Url.AbsolutePath.EndsWith("MONDO%3A0009213") || r.Url.AbsolutePath.EndsWith("MONDO:0009213")), Is.True);
    }

    // ---- nothing found, and failures -------------------------------------------------------------------

    [Test]
    public void NoMatchingDisease_IsBadInput()
    {
        UseRig(_ => Json("""{"items":[]}"""));
        var (exit, output, err) = Run("disease", "zzzzqq");
        Assert.Multiple(() =>
        {
            Assert.That(exit, Is.EqualTo(2));
            Assert.That(output, Is.Empty);
            Assert.That(err, Does.Contain("no disease found for 'zzzzqq'"));
        });
    }

    [Test]
    public void ADiseaseWithoutAnyOmimNumber_ExplainsWhy()
    {
        UseRig(seen => seen.Url.AbsolutePath.EndsWith("/search")
            ? Json("""{"items":[{"id":"MONDO:0000999","category":"biolink:Disease","name":"lonely disease","xref":["ORPHA:1"]}]}""")
            : Json("""{"id":"MONDO:0000999","name":"lonely disease","xref":["ORPHA:1"],"node_hierarchy":{"sub_classes":[]}}"""));

        var (exit, _, err) = Run("disease", "lonely disease");

        Assert.That(exit, Is.EqualTo(2));
        Assert.That(err, Does.Contain("has no numeric OMIM number"));
    }

    [Test]
    public void AnOmimNumberWithNoClinVarRecords_IsAnEmptySuccess()
    {
        var rig = UseRig();
        var inner = rig.Respond;
        rig.Respond = seen => seen.Endpoint == "elink.fcgi" ? Json("""{"linksets":[{"dbfrom":"omim","ids":["999999999"]}]}""") : inner(seen);

        var (exit, output, err) = Run("disease", "OMIM:999999999");

        Assert.Multiple(() =>
        {
            Assert.That(exit, Is.EqualTo(0));
            Assert.That(output, Is.Empty);
            Assert.That(err, Does.Contain("0 ClinVar records matched"));
        });
    }

    [Test]
    public void AnUpstreamFailure_ExitsWithCode4()
    {
        var rig = UseRig();
        rig.Respond = _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);

        var (nameExit, _, nameErr) = Run("disease", "phenylketonuria");
        var (omimExit, _, omimErr) = Run("disease", "261600");

        Assert.Multiple(() =>
        {
            Assert.That(nameExit, Is.EqualTo(4));
            Assert.That(nameErr, Does.Contain("Monarch").And.Contain("503"));
            Assert.That(omimExit, Is.EqualTo(4));
            Assert.That(omimErr, Does.Contain("NCBI"));
        });
    }
}

using System.Net;
using System.Text;
using System.Web;
using gRNA;
using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;
using static gRNA.DiseaseVariants;
using static DiseaseMutationsAppTests.Stubs;

namespace DiseaseMutationsAppTests;

/// <summary>Shared helpers: captured real responses (Fixtures/disease) and F# interop conveniences.</summary>
internal static class DiseaseFixtures
{
    public static string Load(string name) =>
        File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "disease", name));

    public static FSharpList<string> L(params string[] items) => ListModule.OfSeq(items);

    public static string? Opt(FSharpOption<string>? option) => option is null ? null : option.Value;
}

/// <summary>The pure parts: what the user typed, what Monarch and NCBI return, and how variants are written out.</summary>
public class DiseaseVariantsParsingTests
{
    // ---- what the user typed ---------------------------------------------------------------------------

    [TestCase("261600", "261600")]
    [TestCase("OMIM:219700", "219700")]
    [TestCase("omim 219700", "219700")]
    [TestCase("MIM:603903", "603903")]
    [TestCase("#143100", "143100")]
    [TestCase("  261600  ", "261600")]
    public void Query_OmimNumbers_AreRecognisedInEveryForm(string text, string number)
    {
        Assert.That(parseQuery(text), Is.EqualTo(DiseaseQuery.NewByOmim(number)));
    }

    [TestCase("MONDO:0009861", "MONDO:0009861")]
    [TestCase("mondo:9861", "MONDO:0009861")]
    [TestCase("MONDO 0009861", "MONDO:0009861")]
    public void Query_MondoIds_AreNormalisedToSevenDigits(string text, string id)
    {
        Assert.That(parseQuery(text), Is.EqualTo(DiseaseQuery.NewByMondo(id)));
    }

    [TestCase("phenylketonuria")]
    [TestCase("Fanconi anemia")]
    [TestCase("beta-thalassemia, 2nd type")]
    [TestCase("334/../../x")]            // a name is only ever percent-encoded, never used as a path
    [TestCase("a&b=c?d")]
    public void Query_Names_StayNames(string text)
    {
        Assert.That(parseQuery(text), Is.EqualTo(DiseaseQuery.NewByName(text)));
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase("22")]                        // too short for an OMIM number
    [TestCase("12345678901")]               // too long
    [TestCase("OMIM:12ab")]
    [TestCase("OMIM:")]
    [TestCase("MONDO:123456789")]           // more than 7 digits
    [TestCase("bad\u0007name")]             // control character
    public void Query_Invalid_IsRefusedAsInput(string text)
    {
        Assert.Throws<GrnaInputException>(() => parseQuery(text));
    }

    [Test]
    public void Query_TooLong_IsRefused()
    {
        Assert.Throws<GrnaInputException>(() => parseQuery(new string('a', 121)));
        Assert.DoesNotThrow(() => parseQuery(new string('a', 120)));
    }

    [Test]
    public void Query_Null_IsRefused() => Assert.Throws<GrnaInputException>(() => parseQuery(null!));

    // ---- Monarch ---------------------------------------------------------------------------------------

    [Test]
    public void MonarchSearch_Pku_ReadsOmimNumberAndName()
    {
        var matches = parseMonarchSearch(DiseaseFixtures.Load("monarch_search_pku.json"));
        Assert.That(matches, Is.Not.Empty);
        Assert.Multiple(() =>
        {
            Assert.That(matches[0].Id, Is.EqualTo("MONDO:0009861"));
            Assert.That(matches[0].Name, Is.EqualTo("phenylketonuria"));
            Assert.That(matches[0].OmimNumbers.ToArray(), Is.EqualTo(new[] { "261600" }));
            Assert.That(matches[0].PhenotypicSeries, Is.Empty);
        });
    }

    [Test]
    public void MonarchSearch_PhenotypicSeries_AreKeptApart_AndOmimDiseasesComeFirst()
    {
        var matches = parseMonarchSearch(DiseaseFixtures.Load("monarch_search_fanconi.json"));
        // Monarch's order was: the parent (phenotypic series), the pig disease (no OMIM), group C, group D2.
        Assert.Multiple(() =>
        {
            Assert.That(matches.Select(m => m.Id), Is.EqualTo(new[] { "MONDO:0019391", "MONDO:0009213", "MONDO:0009214", "MONDO:7770734" }),
                "diseases with an OMIM link first, the pig disease last");
            Assert.That(matches[0].OmimNumbers, Is.Empty);
            Assert.That(matches[0].PhenotypicSeries.ToArray(), Is.EqualTo(new[] { "PS227650" }));
            Assert.That(matches[1].OmimNumbers.ToArray(), Is.EqualTo(new[] { "227645" }));
        });
    }

    [Test]
    public void MonarchEntity_ReadsSubtypes()
    {
        var entity = parseMonarchEntity(DiseaseFixtures.Load("monarch_entity_fanconi.json"));
        Assert.Multiple(() =>
        {
            Assert.That(entity.Name, Is.EqualTo("Fanconi anemia"));
            Assert.That(entity.Subtypes.Count(), Is.EqualTo(22));
            Assert.That(entity.Subtypes.All(s => s.Id.StartsWith("MONDO:")), Is.True);
        });
    }

    [TestCase("")]
    [TestCase("<html><body>502 Bad Gateway</body></html>")]
    [TestCase("not json")]
    public void MonarchSearch_NotJson_IsAnUpstreamError(string body)
    {
        var ex = Assert.Throws<GrnaUpstreamException>(() => parseMonarchSearch(body))!;
        Assert.That(ex.Message, Does.Contain("Monarch"));
    }

    [Test]
    public void MonarchSearch_DropsNonDiseases_AndToleratesMissingFields()
    {
        var json = """{"items":[{"id":"HGNC:1","category":"biolink:Gene","name":"x"},{"id":"MONDO:0000001","name":"bare"},{"name":"no id"}]}""";
        var matches = parseMonarchSearch(json);
        Assert.That(matches.Select(m => m.Id), Is.EqualTo(new[] { "MONDO:0000001" }));
        Assert.That(matches[0].OmimNumbers, Is.Empty);
    }

    [Test]
    public void MonarchEntity_WithoutId_IsAnUpstreamError()
    {
        Assert.Throws<GrnaUpstreamException>(() => parseMonarchEntity("""{"name":"x"}"""));
    }

    // ---- NCBI ------------------------------------------------------------------------------------------

    [Test]
    public void Elink_History_ReadsWebEnvAndQueryKey()
    {
        var handle = parseElinkHistory(DiseaseFixtures.Load("elink_history_pku.json"));
        Assert.That(handle, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(handle!.Value.Item1, Does.StartWith("MCID_"));
            Assert.That(handle.Value.Item2, Is.EqualTo("1"));
        });
    }

    [Test]
    public void Elink_NoLinks_IsNone()
    {
        const string json = """{"header":{"type":"elink"},"linksets":[{"dbfrom":"omim","ids":["999999999"]}]}""";
        Assert.That(parseElinkHistory(json), Is.Null);
        Assert.That(parseElinkHistory("""{"linksets":[]}"""), Is.Null);
    }

    [Test]
    public void Elink_ErrorBody_IsAnUpstreamError()
    {
        var ex = Assert.Throws<GrnaUpstreamException>(() => parseElinkHistory("""{"error":"API rate limit exceeded"}"""))!;
        Assert.That(ex.Message, Does.Contain("rate limit"));
    }

    [Test]
    public void Esearch_ReadsCountAndIds()
    {
        var (count, ids) = parseEsearch(DiseaseFixtures.Load("esearch_pku_filtered.json")).ToValueTuple();
        Assert.Multiple(() =>
        {
            Assert.That(count, Is.EqualTo(923));
            Assert.That(ids.ToArray(), Is.EqualTo(new[] { "5016062", "5016061", "5016045", "5014657", "5014236" }));
        });
    }

    [TestCase("""{"esearchresult":{"ERROR":"Invalid query"}}""")]
    [TestCase("""{"error":"API rate limit exceeded"}""")]
    [TestCase("<html>error</html>")]
    public void Esearch_ErrorShapes_AreUpstreamErrors(string body)
    {
        Assert.Throws<GrnaUpstreamException>(() => parseEsearch(body));
    }

    [Test]
    public void Esummary_MapsEveryFieldOfARecord()
    {
        var variants = parseEsummary(DiseaseFixtures.Load("esummary_sample.json"));
        var rs334 = variants.Single(v => v.ClinVarId == "15333");
        Assert.Multiple(() =>
        {
            Assert.That(rs334.Name, Is.EqualTo("NM_000518.5(HBB):c.20A>T (p.Glu7Val)"));
            Assert.That(DiseaseFixtures.Opt(rs334.RsId), Is.EqualTo("rs334"));
            Assert.That(DiseaseFixtures.Opt(rs334.Gene), Is.EqualTo("HBB"));
            Assert.That(DiseaseFixtures.Opt(rs334.Hgvs), Is.EqualTo("NC_000011.10:g.5227002T>A"), "Monarch labels the same variant NC_000011.10:g.5227002T>A");
            Assert.That(rs334.Significance, Is.EqualTo("Pathogenic"));
            Assert.That(rs334.ReviewStatus, Is.EqualTo("criteria provided, multiple submitters, no conflicts"));
            Assert.That(rs334.Traits.ToArray(), Is.EqualTo(new[] { "Hereditary persistence of fetal hemoglobin", "METHEMOGLOBINEMIA, BETA TYPE" }));
        });
    }

    [Test]
    public void Esummary_RecordWithoutDbSnp_HasNoRsId_ButKeepsItsHgvs()
    {
        var deletion = parseEsummary(DiseaseFixtures.Load("esummary_sample.json")).Single(v => v.ClinVarId == "5016062");
        Assert.Multiple(() =>
        {
            Assert.That(deletion.RsId, Is.Null);
            Assert.That(DiseaseFixtures.Opt(deletion.Hgvs), Is.EqualTo("NC_000012.12:g.102877506del"));
        });
    }

    [Test]
    public void Esummary_RecordWithNoGermlineClassification_IsKept_WithEmptyClassification()
    {
        var none = parseEsummary(DiseaseFixtures.Load("esummary_sample.json")).Single(v => v.ClinVarId == "9999999");
        Assert.Multiple(() =>
        {
            Assert.That(none.Significance, Is.EqualTo(""));
            Assert.That(none.Hgvs, Is.Null, "an empty SPDI gives no HGVS");
            Assert.That(none.RsId, Is.Null);
        });
    }

    [Test]
    public void Esummary_ReadsEveryRecordInOrder()
    {
        var ids = parseEsummary(DiseaseFixtures.Load("esummary_sample.json")).Select(v => v.ClinVarId).ToList();
        Assert.That(ids.ToArray(), Is.EqualTo(new[] { "5016062", "5016061", "5016045", "5012435", "5005024", "4799094", "2768314", "2677441", "15333", "9999999" }));
    }

    [Test]
    public void Esummary_ErrorShapes_AreUpstreamErrors()
    {
        Assert.Throws<GrnaUpstreamException>(() => parseEsummary("""{"error":"Too many UIDs"}"""));
        Assert.Throws<GrnaUpstreamException>(() => parseEsummary("<html>Service unavailable</html>"));
    }

    [Test]
    public void Esummary_ToleratesMissingVariationSet_AndNonNumericDbSnp()
    {
        var json = """
            {"result":{"uids":["1","2"],
              "1":{"uid":"1","title":"bare record"},
              "2":{"uid":"2","title":"odd","variation_set":[{"variation_xrefs":[{"db_source":"dbSNP","db_id":"12x"}]}]}}}
            """;
        var variants = parseEsummary(json);
        Assert.That(variants.Count(), Is.EqualTo(2));
        Assert.That(variants.All(v => v.RsId is null && v.Hgvs is null), Is.True);
    }

    // ---- SPDI to HGVS ----------------------------------------------------------------------------------

    [TestCase("NC_000011.10:5227001:T:A", "NC_000011.10:g.5227002T>A", Description = "substitution")]
    [TestCase("NC_000012.12:102877504:TT:T", "NC_000012.12:g.102877506del", Description = "one-base deletion (anchored form)")]
    [TestCase("NC_000012.12:100:ACG:A", "NC_000012.12:g.102_103del", Description = "multi-base deletion")]
    [TestCase("NC_000012.12:102866630:CC:CCC", "NC_000012.12:g.102866632dup", Description = "duplication of the previous base")]
    [TestCase("NC_000012.12:50:ATAT:ATATAT", "NC_000012.12:g.53_54dup", Description = "duplication of two bases")]
    [TestCase("NC_000012.12:102846906::AA", "NC_000012.12:g.102846906_102846907insAA", Description = "insertion with no neighbours known")]
    [TestCase("NC_000012.12:102852926:GT:AAATCC", "NC_000012.12:g.102852927_102852928delinsAAATCC", Description = "deletion-insertion")]
    [TestCase("NC_000012.12:10:G:AT", "NC_000012.12:g.11delinsAT", Description = "one base replaced by two")]
    [TestCase("NC_000012.12:10:AC:AT", "NC_000012.12:g.12C>T", Description = "common prefix removed from a substitution")]
    [TestCase("NC_000012.12:10:AC:GT", "NC_000012.12:g.11_12delinsGT", Description = "two-base replacement")]
    [TestCase("NC_000012.12:10:CAG:CTG", "NC_000012.12:g.12A>T", Description = "common prefix and suffix removed")]
    public void SpdiToHgvs_Converts(string spdi, string expected)
    {
        Assert.That(DiseaseFixtures.Opt(spdiToHgvs(spdi)), Is.EqualTo(expected));
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase("NC_000012.12:10:A:A")]            // no change
    [TestCase("NC_000012.12:10::")]              // nothing at all
    [TestCase("NG_008690.2:10:A:T")]             // not a chromosome accession
    [TestCase("NC_000012.12:10:3:")]             // a deleted length instead of bases
    [TestCase("NC_000012.12:10:a:t")]            // lower case
    [TestCase("NC_000012.12:10:N:T")]            // not A/C/G/T
    [TestCase("NC_000012.12:-1:A:T")]            // negative position
    [TestCase("NC_000012.12:x:A:T")]
    [TestCase("NC_000012.12:10:A")]              // wrong number of fields
    [TestCase("NC_000012.12:0::A")]              // insertion before the first base
    public void SpdiToHgvs_RefusesWhatItCannotRepresent(string spdi)
    {
        Assert.That(spdiToHgvs(spdi), Is.Null);
    }

    [Test]
    public void SpdiToHgvs_EveryFixtureResult_IsAcceptedByDianasOwnHgvsParser()
    {
        var produced = parseEsummary(DiseaseFixtures.Load("esummary_sample.json"))
            .Select(v => DiseaseFixtures.Opt(v.Hgvs)).Where(h => h is not null).Cast<string>().ToList();
        Assert.That(produced, Has.Count.EqualTo(9));
        foreach (var code in produced)
            Assert.DoesNotThrow(() => new gRNA.HGVS.HGVS(code), code);
    }

    [TestCase("NC_000012.12:10:ACG:A")]
    [TestCase("NC_000012.12:50:ATAT:ATATAT")]
    [TestCase("NC_000012.12:10:GT:AAATCC")]
    [TestCase("NC_000012.12:5:G:AT")]
    public void SpdiToHgvs_Results_AreAcceptedByDianasParser(string spdi)
    {
        var code = DiseaseFixtures.Opt(spdiToHgvs(spdi))!;
        Assert.DoesNotThrow(() => new gRNA.HGVS.HGVS(code), code);
    }

    // ---- classification --------------------------------------------------------------------------------

    [TestCase("practice guideline", 4)]
    [TestCase("reviewed by expert panel", 3)]
    [TestCase("criteria provided, multiple submitters, no conflicts", 2)]
    [TestCase("criteria provided, single submitter", 1)]
    [TestCase("criteria provided, conflicting classifications", 1)]
    [TestCase("no assertion criteria provided", 0)]
    [TestCase("no classification provided", 0)]
    [TestCase("", 0)]
    [TestCase("Reviewed By Expert Panel", 3)]
    public void ReviewStars_FollowClinVarsDefinitions(string status, int stars)
    {
        Assert.That(reviewStars(status), Is.EqualTo(stars));
    }

    [Test]
    public void ReviewStars_Null_IsZero() => Assert.That(reviewStars(null!), Is.EqualTo(0));

    [TestCase("Pathogenic", true, true, true)]
    [TestCase("Likely pathogenic", false, true, true)]
    [TestCase("Pathogenic/Likely pathogenic", true, true, true)]
    [TestCase("Pathogenic, low penetrance", true, true, true)]
    [TestCase("Uncertain significance", false, false, true)]
    [TestCase("Likely benign", false, false, true)]
    [TestCase("Benign", false, false, true)]
    [TestCase("Conflicting classifications of pathogenicity", false, false, true)]
    [TestCase("", false, false, true)]
    public void Significance_Filters(string classification, bool pathogenicOnly, bool pathogenicOrLikely, bool any)
    {
        Assert.Multiple(() =>
        {
            Assert.That(meetsSignificance(MinSignificance.PathogenicOnly, classification), Is.EqualTo(pathogenicOnly), "PathogenicOnly");
            Assert.That(meetsSignificance(MinSignificance.PathogenicOrLikely, classification), Is.EqualTo(pathogenicOrLikely), "PathogenicOrLikely");
            Assert.That(meetsSignificance(MinSignificance.AnyClassification, classification), Is.EqualTo(any), "Any");
        });
    }

    // ---- exports ---------------------------------------------------------------------------------------

    private static FSharpList<DiseaseVariant> Sample() => ListModule.OfSeq(new[]
    {
        new DiseaseVariant("1", "NM_1.1(G):c.1A>T", FSharpOption<string>.Some("G"), FSharpOption<string>.Some("rs10"), FSharpOption<string>.Some("NC_000001.11:g.5A>T"),
            "Pathogenic", "criteria provided, single submitter", L("Condition, with comma")),
        new DiseaseVariant("2", "=SUM(A1)", FSharpOption<string>.Some("G"), FSharpOption<string>.None, FSharpOption<string>.Some("NC_000001.11:g.7del"),
            "Likely pathogenic", "reviewed by expert panel", L("a", "b")),
        new DiseaseVariant("3", "dup", FSharpOption<string>.None, FSharpOption<string>.Some("rs10"), FSharpOption<string>.None,
            "Pathogenic", "", L())
    });

    private static FSharpList<string> L(params string[] items) => ListModule.OfSeq(items);

    [Test]
    public void RsList_IsOneDistinctRsIdPerLine_AndSkipsRecordsWithout()
    {
        Assert.That(formatRsList(Sample()), Is.EqualTo("rs10\n"));
        Assert.That(formatRsList(ListModule.Empty<DiseaseVariant>()), Is.EqualTo(""));
    }

    [Test]
    public void Csv_HasHeader_QuotesFields_AndNeutralisesFormulas()
    {
        var lines = formatCsv(Sample()).TrimEnd('\n').Split('\n');
        Assert.Multiple(() =>
        {
            Assert.That(lines[0], Is.EqualTo("rsID,HGVS,Gene,Classification,Review status,Stars,ClinVar ID,Variant,Conditions"));
            Assert.That(lines[1], Is.EqualTo("rs10,NC_000001.11:g.5A>T,G,Pathogenic,\"criteria provided, single submitter\",1,1,NM_1.1(G):c.1A>T,\"Condition, with comma\""));
            Assert.That(lines[2], Does.Contain("'=SUM(A1)"), "a field starting with = must not run as a formula");
            Assert.That(lines[2], Does.Contain("a; b"));
        });
        Assert.That(lines, Has.Length.EqualTo(4));
    }

    [Test]
    public void Tsv_NeverContainsExtraTabsOrNewlinesInFields()
    {
        var variants = ListModule.OfSeq(new[]
        {
            new DiseaseVariant("1", "name\twith\ttabs\nand newline", FSharpOption<string>.None, FSharpOption<string>.Some("rs1"), FSharpOption<string>.None, "Pathogenic", "", L())
        });
        var rows = formatTsv(variants).TrimEnd('\n').Split('\n');
        Assert.Multiple(() =>
        {
            Assert.That(rows, Has.Length.EqualTo(2));
            Assert.That(rows[1].Split('\t'), Has.Length.EqualTo(9));
            Assert.That(rows[1], Does.StartWith("rs1\t"));
        });
    }

    [Test]
    public void Json_RsIdIsAStringOrNull_AndCarriesTheTotals()
    {
        var result = new DiseaseVariantResult(L("261600"), 923, Sample(), true);
        using var doc = System.Text.Json.JsonDocument.Parse(formatJson(result));
        var root = doc.RootElement;
        Assert.Multiple(() =>
        {
            Assert.That(root.GetProperty("totalMatching").GetInt32(), Is.EqualTo(923));
            Assert.That(root.GetProperty("truncated").GetBoolean(), Is.True);
            Assert.That(root.GetProperty("omimNumbers")[0].GetString(), Is.EqualTo("261600"));
            Assert.That(root.GetProperty("variants")[0].GetProperty("rsId").GetString(), Is.EqualTo("rs10"));
            Assert.That(root.GetProperty("variants")[1].GetProperty("rsId").ValueKind, Is.EqualTo(System.Text.Json.JsonValueKind.Null));
            Assert.That(root.GetProperty("variants")[1].GetProperty("stars").GetInt32(), Is.EqualTo(3));
        });
    }

    [Test]
    public void CountWithRsId_CountsRecordsNotDistinctIds()
    {
        Assert.That(countWithRsId(Sample()), Is.EqualTo(2));
    }
}

/// <summary>
/// The client against a stubbed HTTP layer. The global NCBI settings are replaced per test, so these cannot run in parallel
/// with other tests that read them.
/// </summary>
[NonParallelizable]
public class DiseaseVariantClientTests
{
    private GrnaEnvironment _savedEnvironment = null!;

    [SetUp]
    public void SaveEnvironment()
    {
        _savedEnvironment = GrnaEnvironmentModule.getCurrent();
        GrnaEnvironmentModule.setCurrent(new GrnaEnvironment(
            _savedEnvironment.BowtieBinary, _savedEnvironment.BowtieIndexBase, _savedEnvironment.BowtieIndexDirectory,
            _savedEnvironment.PythonExecutable, _savedEnvironment.ProcessTimeout, FSharpOption<string>.None, FSharpOption<string>.None,
            _savedEnvironment.SequenceCacheEntries));
    }

    [TearDown]
    public void RestoreEnvironment() => GrnaEnvironmentModule.setCurrent(_savedEnvironment);

    private static void WithNcbiIdentity(string? apiKey, string? contact)
    {
        var e = GrnaEnvironmentModule.getCurrent();
        GrnaEnvironmentModule.setCurrent(new GrnaEnvironment(
            e.BowtieBinary, e.BowtieIndexBase, e.BowtieIndexDirectory, e.PythonExecutable, e.ProcessTimeout,
            apiKey is null ? FSharpOption<string>.None : FSharpOption<string>.Some(apiKey),
            contact is null ? FSharpOption<string>.None : FSharpOption<string>.Some(contact),
            e.SequenceCacheEntries));
    }

    private static IProgress<VariantProgress> NoProgress => new SyncProgress(_ => { });

    private sealed class SyncProgress : IProgress<VariantProgress>
    {
        private readonly Action<VariantProgress> _on;
        public SyncProgress(Action<VariantProgress> on) => _on = on;
        public void Report(VariantProgress value) => _on(value);
    }

    private static Task<DiseaseVariantResult> Variants(Rig rig, MinSignificance min = null!, CancellationToken ct = default, params string[] omim) =>
        rig.Client.GetVariantsAsync(DiseaseFixtures.L(omim.Length == 0 ? new[] { "261600" } : omim), min ?? MinSignificance.PathogenicOrLikely, NoProgress, ct);

    // ---- finding diseases ------------------------------------------------------------------------------

    [Test]
    public async Task FindDiseases_ByName_SearchesMonarchOnly()
    {
        var rig = new Rig { Respond = _ => Json(DiseaseFixtures.Load("monarch_search_pku.json")) };

        var matches = await rig.Client.FindDiseasesAsync("phenylketonuria", CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(matches.Select(m => m.Id), Does.Contain("MONDO:0009861"));
            Assert.That(rig.Requests, Has.Count.EqualTo(1));
            Assert.That(rig.Requests[0].Url.ToString(), Is.EqualTo($"{MonarchBase}/search?q=phenylketonuria&category=biolink:Disease&limit=10"));
            Assert.That(rig.Ncbi, Is.Empty);
        });
    }

    [Test]
    public async Task FindDiseases_ByName_PercentEncodesTheText()
    {
        var rig = new Rig { Respond = _ => Json("""{"items":[]}""") };

        var matches = await rig.Client.FindDiseasesAsync("a&b=c/../d é", CancellationToken.None);

        Assert.That(matches, Is.Empty);
        Assert.That(rig.Requests[0].Url.Query, Does.StartWith("?q=a%26b%3Dc%2F..%2Fd%20%C3%A9&category="));
    }

    [Test]
    public async Task FindDiseases_ByOmimNumber_NeedsNoNetwork()
    {
        var rig = new Rig();

        var matches = await rig.Client.FindDiseasesAsync("OMIM:261600", CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(matches, Has.Length.EqualTo(1));
            Assert.That(matches[0].Id, Is.EqualTo("OMIM:261600"));
            Assert.That(matches[0].OmimNumbers.ToArray(), Is.EqualTo(new[] { "261600" }));
            Assert.That(rig.Requests, Is.Empty);
        });
    }

    [Test]
    public async Task FindDiseases_ByMondoId_FetchesTheEntity_And404MeansNoMatch()
    {
        var rig = new Rig { Respond = s => s.Url.AbsolutePath.EndsWith("MONDO:0009861") ? Json(DiseaseFixtures.Load("monarch_entity_pku.json")) : new HttpResponseMessage(HttpStatusCode.NotFound) };

        var found = await rig.Client.FindDiseasesAsync("MONDO:9861", CancellationToken.None);
        var missing = await rig.Client.FindDiseasesAsync("MONDO:0000001", CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(found.Single().OmimNumbers.ToArray(), Is.EqualTo(new[] { "261600" }));
            Assert.That(missing, Is.Empty);
        });
    }

    [Test]
    public void FindDiseases_BadInput_FailsBeforeAnyRequest()
    {
        var rig = new Rig();
        Assert.ThrowsAsync<GrnaInputException>(() => rig.Client.FindDiseasesAsync("OMIM:12ab", CancellationToken.None));
        Assert.That(rig.Requests, Is.Empty);
    }

    // ---- resolving OMIM numbers ------------------------------------------------------------------------

    private static DiseaseMatch Disease(string id, string[] omim, string[]? series = null) =>
        new(id, "name", DiseaseFixtures.L(omim), DiseaseFixtures.L(series ?? Array.Empty<string>()), ListModule.Empty<DiseaseRef>());

    [Test]
    public async Task Resolve_ADiseaseWithItsOwnOmimNumber_NeedsNoNetworkUnlessSubtypesAreRequested()
    {
        var rig = new Rig { Respond = FanconiTree() };

        var resolved = await rig.Client.ResolveOmimNumbersAsync(Disease("MONDO:0009861", new[] { "261600" }), false, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(resolved.OmimNumbers.ToArray(), Is.EqualTo(new[] { "261600" }));
            Assert.That(resolved.SubtypesChecked, Is.EqualTo(0));
            Assert.That(rig.Requests, Is.Empty);
        });
    }

    [Test]
    public async Task Resolve_AnOmimDisease_NeverLooksAtMonarch_EvenWithSubtypes()
    {
        var rig = new Rig();
        var disease = Disease("OMIM:261600", new[] { "261600" });

        var resolved = await rig.Client.ResolveOmimNumbersAsync(disease, true, CancellationToken.None);

        Assert.That(resolved.OmimNumbers.ToArray(), Is.EqualTo(new[] { "261600" }));
        Assert.That(rig.Requests, Is.Empty);
    }

    [Test]
    public async Task Resolve_APhenotypicSeries_IsExpandedThroughItsSubtypes()
    {
        var rig = new Rig { Respond = FanconiTree() };

        var resolved = await rig.Client.ResolveOmimNumbersAsync(Disease("MONDO:0019391", Array.Empty<string>(), new[] { "PS227650" }), false, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(resolved.SubtypesTotal, Is.EqualTo(22));
            Assert.That(resolved.SubtypesChecked, Is.EqualTo(22));
            Assert.That(resolved.OmimNumbers, Has.Length.EqualTo(22));
            Assert.That(resolved.OmimNumbers, Is.Unique);
            Assert.That(rig.Monarch.Count(), Is.EqualTo(23), "the parent plus its 22 subtypes");
        });
    }

    [Test]
    public async Task Resolve_SubtypeExpansion_IsCapped_AndSaysSo()
    {
        var rig = new Rig(o => ClientOptionsModule.withLimits(3000, 200, 5, o)) { Respond = FanconiTree() };

        var resolved = await rig.Client.ResolveOmimNumbersAsync(Disease("MONDO:0019391", Array.Empty<string>()), true, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(resolved.SubtypesChecked, Is.EqualTo(5));
            Assert.That(resolved.SubtypesTotal, Is.EqualTo(22));
            Assert.That(resolved.OmimNumbers, Has.Length.EqualTo(5));
        });
    }

    [Test]
    public async Task Resolve_ASubtypeThatDisappeared_IsSkipped()
    {
        var rig = new Rig { Respond = FanconiTree(new HashSet<string> { "MONDO:0009215" }) };

        var resolved = await rig.Client.ResolveOmimNumbersAsync(Disease("MONDO:0019391", Array.Empty<string>()), true, CancellationToken.None);

        Assert.That(resolved.OmimNumbers, Has.Length.EqualTo(21));
    }

    [Test]
    public void Resolve_AParentThatMonarchDoesNotKnow_IsAnUpstreamError()
    {
        var rig = new Rig();   // every request 404s
        Assert.ThrowsAsync<GrnaUpstreamException>(() => rig.Client.ResolveOmimNumbersAsync(Disease("MONDO:0019391", Array.Empty<string>()), true, CancellationToken.None));
    }

    // ---- fetching variants -----------------------------------------------------------------------------

    [Test]
    public async Task Variants_WalkElinkEsearchEsummary_AndFilterByClassification()
    {
        var rig = new Rig(o => ClientOptionsModule.withLimits(3000, 2, 40, o)) { Respond = NcbiPku() };

        var result = await Variants(rig);

        Assert.Multiple(() =>
        {
            Assert.That(rig.Ncbi.Select(r => r.Endpoint), Is.EqualTo(new[] { "elink.fcgi", "esearch.fcgi", "esummary.fcgi", "esummary.fcgi", "esummary.fcgi" }),
                "five ids in batches of two make three esummary calls");
            Assert.That(result.TotalMatching, Is.EqualTo(923));
            Assert.That(result.Truncated, Is.True, "923 matched but only 5 ids were returned");
            // The stub returns the same 10-record sample for every batch: duplicates collapse, the filter keeps pathogenic ones.
            Assert.That(result.Variants.Select(v => v.ClinVarId), Is.EquivalentTo(new[] { "5016062", "5016061", "5016045", "5012435", "2768314", "15333" }));
            Assert.That(countWithRsId(result.Variants), Is.EqualTo(2));
        });
    }

    [Test]
    public async Task Variants_RequestsCarryTheDocumentedParameters()
    {
        WithNcbiIdentity("KEY123", "team@example.org");
        var rig = new Rig(o => ClientOptionsModule.withLimits(3000, 2, 40, o)) { Respond = NcbiPku() };

        await Variants(rig, MinSignificance.PathogenicOrLikely, default, "261600", "603903");

        var elink = rig.Ncbi.First(r => r.Endpoint == "elink.fcgi").Form;
        var esearch = rig.Ncbi.First(r => r.Endpoint == "esearch.fcgi").Form;
        var esummary = rig.Ncbi.First(r => r.Endpoint == "esummary.fcgi").Form;
        Assert.Multiple(() =>
        {
            Assert.That(rig.Ncbi.All(r => r.Method == HttpMethod.Post), Is.True, "no query string: long id lists go in the body");
            Assert.That(elink["dbfrom"], Is.EqualTo("omim"));
            Assert.That(elink["db"], Is.EqualTo("clinvar"));
            Assert.That(elink["id"], Is.EqualTo("261600,603903"));
            Assert.That(elink["cmd"], Is.EqualTo("neighbor_history"));
            Assert.That(esearch["db"], Is.EqualTo("clinvar"));
            Assert.That(esearch["WebEnv"], Does.StartWith("MCID_"), "the history handle from elink is passed on");
            Assert.That(esearch["query_key"], Is.EqualTo("1"));
            Assert.That(esearch["term"], Is.EqualTo("#1 AND (clinsig_pathogenic[prop] OR clinsig_likely_pathogenic[prop])"));
            Assert.That(esearch["retmax"], Is.EqualTo("3000"));
            Assert.That(esummary["db"], Is.EqualTo("clinvar"));
            Assert.That(esummary["id"], Is.EqualTo("5016062,5016061"));
            foreach (var form in new[] { elink, esearch, esummary })
            {
                Assert.That(form["tool"], Is.EqualTo("gRNA"));
                Assert.That(form["email"], Is.EqualTo("team@example.org"));
                Assert.That(form["api_key"], Is.EqualTo("KEY123"));
                Assert.That(form["retmode"], Is.EqualTo("json"));
            }
        });
    }

    [Test]
    public async Task Variants_WithoutAKeyOrContact_SendOnlyTheToolName()
    {
        var rig = new Rig { Respond = NcbiPku() };

        await Variants(rig);

        var form = rig.Ncbi.First().Form;
        Assert.Multiple(() =>
        {
            Assert.That(form["tool"], Is.EqualTo("gRNA"));
            Assert.That(form["email"], Is.Null);
            Assert.That(form["api_key"], Is.Null);
        });
    }

    [TestCase("PathogenicOnly", "#1 AND clinsig_pathogenic[prop]")]
    [TestCase("AnyClassification", "#1")]
    public async Task Variants_MinSignificance_ChoosesTheServerSideFilter(string which, string term)
    {
        var rig = new Rig { Respond = NcbiPku() };
        var min = which == "PathogenicOnly" ? MinSignificance.PathogenicOnly : MinSignificance.AnyClassification;

        var result = await Variants(rig, min);

        Assert.That(rig.Ncbi.First(r => r.Endpoint == "esearch.fcgi").Form["term"], Is.EqualTo(term));
        if (which == "AnyClassification") Assert.That(result.Variants.Count(), Is.EqualTo(10), "nothing is dropped client-side either");
    }

    [Test]
    public async Task Variants_PathogenicOnly_AlsoFiltersClientSide()
    {
        var rig = new Rig { Respond = NcbiPku() };

        var result = await Variants(rig, MinSignificance.PathogenicOnly);

        Assert.That(result.Variants.All(v => v.Significance.StartsWith("Pathogenic")), Is.True);
    }

    [Test]
    public async Task Variants_NothingLinked_ReturnsEmpty_WithoutFurtherCalls()
    {
        var rig = new Rig { Respond = _ => Json("""{"linksets":[{"dbfrom":"omim","ids":["999999999"]}]}""") };

        var result = await Variants(rig, null!, default, "999999999");

        Assert.Multiple(() =>
        {
            Assert.That(result.TotalMatching, Is.EqualTo(0));
            Assert.That(result.Variants, Is.Empty);
            Assert.That(result.Truncated, Is.False);
            Assert.That(rig.Requests, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task Variants_MoreMatchesThanTheCap_AreReportedAsTruncated_AndMaxRecordsIsSent()
    {
        var ids = string.Join(",", Enumerable.Range(1, 3).Select(i => $"\"{i}\""));
        var rig = new Rig(o => ClientOptionsModule.withLimits(3, 200, 40, o))
        {
            Respond = NcbiPku(s => new HttpResponseMessage(HttpStatusCode.NotFound))
        };
        var inner = rig.Respond;
        rig.Respond = s => s.Endpoint == "esearch.fcgi"
            ? Json("{\"esearchresult\":{\"count\":\"5000\",\"idlist\":[" + ids + "]}}")
            : inner(s);

        var result = await Variants(rig);

        Assert.Multiple(() =>
        {
            Assert.That(rig.Ncbi.First(r => r.Endpoint == "esearch.fcgi").Form["retmax"], Is.EqualTo("3"));
            Assert.That(result.TotalMatching, Is.EqualTo(5000));
            Assert.That(result.Truncated, Is.True);
        });
    }

    [Test]
    public async Task Variants_AllMatchesFetched_IsNotTruncated()
    {
        var rig = new Rig { Respond = NcbiPku() };
        var inner = rig.Respond;
        rig.Respond = s => s.Endpoint == "esearch.fcgi" ? Json("""{"esearchresult":{"count":"2","idlist":["5016062","5016061"]}}""") : inner(s);

        var result = await Variants(rig);

        Assert.That(result.Truncated, Is.False);
    }

    [Test]
    public async Task Variants_ReportProgressInEachStage()
    {
        var rig = new Rig(o => ClientOptionsModule.withLimits(3000, 2, 40, o)) { Respond = NcbiPku() };
        var reports = new List<VariantProgress>();

        await rig.Client.GetVariantsAsync(DiseaseFixtures.L("261600"), MinSignificance.PathogenicOrLikely, new SyncProgress(reports.Add), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(reports.First().Stage, Is.EqualTo("Finding ClinVar records"));
            Assert.That(reports.Last().Stage, Is.EqualTo("Reading variant details"));
            Assert.That(reports.Last().Total, Is.EqualTo(5));
            Assert.That(reports.Last().Done, Is.EqualTo(5), "all five ids were read");
            Assert.That(reports.Select(r => r.Done).Where(d => d > 0), Is.Ordered, "progress never goes backwards");
        });
    }

    [Test]
    public async Task Variants_NullProgress_IsAllowed()
    {
        var rig = new Rig { Respond = NcbiPku() };

        var result = await rig.Client.GetVariantsAsync(DiseaseFixtures.L("261600"), MinSignificance.PathogenicOrLikely, null!, CancellationToken.None);

        Assert.That(result.Variants, Is.Not.Empty);
    }

    [TestCase("")]
    [TestCase("12x")]
    [TestCase("1234567890")]
    [TestCase("261600/../x")]
    public void Variants_BadOmimNumber_IsRefusedBeforeAnyRequest(string number)
    {
        var rig = new Rig { Respond = NcbiPku() };
        Assert.ThrowsAsync<GrnaInputException>(() => Variants(rig, null!, default, number));
        Assert.That(rig.Requests, Is.Empty);
    }

    [Test]
    public void Variants_NoOmimNumbers_IsRefused()
    {
        var rig = new Rig();
        Assert.ThrowsAsync<GrnaInputException>(() =>
            rig.Client.GetVariantsAsync(ListModule.Empty<string>(), MinSignificance.PathogenicOrLikely, NoProgress, CancellationToken.None));
        Assert.That(rig.Requests, Is.Empty);
    }

    [Test]
    public void Variants_MoreThan100OmimNumbers_AreRefused()
    {
        var rig = new Rig();
        var many = Enumerable.Range(100000, 101).Select(n => n.ToString()).ToArray();
        Assert.ThrowsAsync<GrnaInputException>(() => Variants(rig, null!, default, many));
    }

    // ---- pacing and failures ---------------------------------------------------------------------------

    [Test]
    public async Task Ncbi_CallsAreSpacedByTheMinimumInterval_AndMonarchCallsAreNot()
    {
        var rig = new Rig(o => ClientOptionsModule.withLimits(3000, 2, 40, o)) { Respond = NcbiPku(s => Json(DiseaseFixtures.Load("monarch_search_pku.json"))) };

        await rig.Client.FindDiseasesAsync("phenylketonuria", CancellationToken.None);
        await rig.Client.FindDiseasesAsync("PKU", CancellationToken.None);
        Assert.That(rig.Waits, Is.Empty, "Monarch is a different service with its own limits");

        await Variants(rig);

        // elink, esearch and three esummary calls: the first goes straight out, each of the other four waits 340 ms.
        Assert.That(rig.Waits, Is.EqualTo(Enumerable.Repeat(TimeSpan.FromMilliseconds(340), 4)));
    }

    [Test]
    public async Task Ncbi_WithAnApiKey_UsesTheShorterInterval()
    {
        WithNcbiIdentity("KEY", null);
        var rig = new Rig(o => ClientOptionsModule.withLimits(3000, 2, 40, o)) { Respond = NcbiPku() };

        await Variants(rig);

        Assert.That(rig.Waits.Distinct(), Is.EqualTo(new[] { TimeSpan.FromMilliseconds(110) }));
    }

    [Test]
    public async Task ATransientFailure_IsRetriedOnce()
    {
        var calls = 0;
        var rig = new Rig();
        rig.Respond = s => ++calls == 1 ? new HttpResponseMessage(HttpStatusCode.TooManyRequests) : Json(DiseaseFixtures.Load("monarch_search_pku.json"));

        var matches = await rig.Client.FindDiseasesAsync("phenylketonuria", CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(matches, Is.Not.Empty);
            Assert.That(rig.Requests, Has.Count.EqualTo(2));
            Assert.That(rig.Waits, Is.EqualTo(new[] { TimeSpan.FromSeconds(2) }), "one back-off before the retry");
        });
    }

    [TestCase(HttpStatusCode.ServiceUnavailable, "503")]
    [TestCase(HttpStatusCode.InternalServerError, "500")]
    [TestCase(HttpStatusCode.TooManyRequests, "429")]
    public async Task ARepeatedFailure_IsAnUpstreamError_NamingTheService(HttpStatusCode status, string code)
    {
        var rig = new Rig { Respond = _ => new HttpResponseMessage(status) };

        var monarch = Assert.ThrowsAsync<GrnaUpstreamException>(() => rig.Client.FindDiseasesAsync("phenylketonuria", CancellationToken.None))!;
        var ncbi = Assert.ThrowsAsync<GrnaUpstreamException>(() => Variants(rig))!;

        Assert.Multiple(() =>
        {
            Assert.That(monarch.Message, Does.Contain("Monarch").And.Contain(code));
            Assert.That(ncbi.Message, Does.Contain("NCBI elink").And.Contain(code));
            Assert.That(rig.Requests, Has.Count.EqualTo(4), "two attempts per call, no more");
        });
    }

    [Test]
    public void AClientError_IsNotRetried()
    {
        var rig = new Rig { Respond = _ => new HttpResponseMessage(HttpStatusCode.BadRequest) };

        var ex = Assert.ThrowsAsync<GrnaUpstreamException>(() => rig.Client.FindDiseasesAsync("phenylketonuria", CancellationToken.None))!;

        Assert.That(ex.Message, Does.Contain("400"));
        Assert.That(rig.Requests, Has.Count.EqualTo(1));
    }

    [Test]
    public void ANetworkFailure_IsAnUpstreamError_NotARawHttpException()
    {
        var rig = new Rig { Respond = _ => throw new HttpRequestException("name resolution failed") };

        var ex = Assert.ThrowsAsync<GrnaUpstreamException>(() => rig.Client.FindDiseasesAsync("phenylketonuria", CancellationToken.None))!;

        Assert.That(ex.Message, Does.Contain("could not be reached"));
    }

    [TestCase("<html><body>Service Unavailable</body></html>")]
    [TestCase("")]
    [TestCase("not json at all")]
    public void AnHtmlOrEmptyBody_IsAnUpstreamError_NeverDesignedAgainst(string body)
    {
        var rig = new Rig { Respond = _ => Json(body) };

        Assert.ThrowsAsync<GrnaUpstreamException>(() => rig.Client.FindDiseasesAsync("phenylketonuria", CancellationToken.None));
        Assert.ThrowsAsync<GrnaUpstreamException>(() => Variants(rig));
    }

    [Test]
    public void NcbiErrorBody_WithHttp200_IsAnUpstreamError()
    {
        var rig = new Rig { Respond = _ => Json("""{"error":"API rate limit exceeded","count":"4"}""") };

        var ex = Assert.ThrowsAsync<GrnaUpstreamException>(() => Variants(rig))!;

        Assert.That(ex.Message, Does.Contain("rate limit"));
    }

    [Test]
    public void ACancelledToken_StopsBeforeAnyRequest()
    {
        var rig = new Rig { Respond = NcbiPku() };
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.CatchAsync<OperationCanceledException>(() => Variants(rig, null!, cts.Token));
        Assert.CatchAsync<OperationCanceledException>(() => rig.Client.FindDiseasesAsync("phenylketonuria", cts.Token));
        Assert.That(rig.Requests, Is.Empty);
    }

    [Test]
    public void CancellingMidRun_StopsTheWalk()
    {
        using var cts = new CancellationTokenSource();
        var rig = new Rig(o => ClientOptionsModule.withLimits(3000, 2, 40, o));
        rig.Respond = s =>
        {
            if (s.Endpoint == "esummary.fcgi") cts.Cancel();   // the user presses Cancel while details are being read
            return NcbiPku()(s);
        };

        Assert.CatchAsync<OperationCanceledException>(() => Variants(rig, null!, cts.Token));
        Assert.That(rig.Ncbi.Count(r => r.Endpoint == "esummary.fcgi"), Is.EqualTo(1), "no further batches after the cancel");
    }
}

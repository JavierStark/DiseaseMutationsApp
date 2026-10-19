using System.Text.RegularExpressions;
using System.Xml.Linq;
using DiseaseMutationsApp.Services;
using Microsoft.FSharp.Core;
using FsConstruct = gRNA.Construct;
using FsExport = gRNA.Export;

namespace DiseaseMutationsAppTests;

/// <summary>Helpers shared by the export tests.</summary>
internal static class ExportFixtures
{
    public const string Spacer = "CAGAGCGCGCAGCAGCAGCAGCAGAAGC";
    public static readonly DateTime Date = new(2026, 10, 19);

    public static FsExport.ExportGuide Guide(string hgvs = "NG_008690.2:g.5000A>T", bool complement = false, string spacer = Spacer,
        string? rs = "rs334", int? rank = 1, double? score = 1.0, int? offtargets = 1) =>
        new(hgvs, rs is null ? FSharpOption<string>.None : FSharpOption<string>.Some(rs), complement, spacer,
            rank is null ? FSharpOption<int>.None : FSharpOption<int>.Some(rank.Value),
            score is null ? FSharpOption<double>.None : FSharpOption<double>.Some(score.Value),
            offtargets is null ? FSharpOption<int>.None : FSharpOption<int>.Some(offtargets.Value));

    public static FsConstruct.ConstructOptions Construct(string dr = "lwa", bool frida = false, bool bioBricks = false, string c5 = "", string c3 = "") =>
        new(dr, frida, bioBricks, c5, c3);

    public static FsExport.ExportOptions Options(FsConstruct.ConstructOptions? construct = null, bool t7 = false, string ns = FsExport.DefaultNamespace) =>
        new(construct ?? FsConstruct.defaultOptions, t7, ns, Date);

    /// <summary>Every combination of the construct switches the interface offers.</summary>
    public static IEnumerable<(FsConstruct.ConstructOptions Construct, bool T7)> AllCombinations()
    {
        foreach (var dr in new[] { "lwa", "none" })
        foreach (var frida in new[] { false, true })
        foreach (var bio in new[] { false, true })
        foreach (var c5 in new[] { "", "ggtt" })
        foreach (var c3 in new[] { "", "AAUc" })
        foreach (var t7 in new[] { false, true })
            yield return (Construct(dr, frida, bio, c5, c3), t7);
    }

    public static ConstructOptions ToApp(FsConstruct.ConstructOptions o) => new(o.DrId, o.Frida, o.BioBricks, o.Custom5, o.Custom3);
}

public class ConstructParityTests
{
    [Test]
    public void FSharpBuilder_AndTheAppFacade_AgreeOnEveryCombination()
    {
        foreach (var (c, _) in ExportFixtures.AllCombinations())
        {
            var app = ExportFixtures.ToApp(c);
            var fsParts = FsConstruct.parts(ExportFixtures.Spacer, c).Select(p => (p.Label, p.Rna)).ToList();
            var csParts = GuideConstruct.Parts(ExportFixtures.Spacer, app).Select(p => (p.Label, p.Rna)).ToList();
            Assert.That(csParts, Is.EqualTo(fsParts));
            Assert.That(GuideConstruct.Build(ExportFixtures.Spacer, app), Is.EqualTo(FsConstruct.build(ExportFixtures.Spacer, c)));
            Assert.That(GuideConstruct.Describe(app), Is.EqualTo(FsConstruct.describe(c)));
        }
    }

    [Test]
    public void T7Constant_IsTheSameInBothLayers() =>
        Assert.That(OligoExport.T7Promoter, Is.EqualTo(FsConstruct.T7Promoter));
}

public class ExportLayoutTests
{
    [Test]
    public void Segments_TileTheSequenceExactly_AndMatchTheOligoTemplate()
    {
        foreach (var (c, t7) in ExportFixtures.AllCombinations())
        {
            var o = ExportFixtures.Options(c, t7);
            var segments = FsExport.layout(o, ExportFixtures.Guide());
            var dna = FsExport.sequence(o, ExportFixtures.Guide());

            Assert.Multiple(() =>
            {
                Assert.That(segments.First().Start, Is.EqualTo(1));
                Assert.That(segments.Last().End, Is.EqualTo(dna.Length));
                for (var i = 1; i < segments.Length; i++)
                    Assert.That(segments[i].Start, Is.EqualTo(segments[i - 1].End + 1), "no gap and no overlap");
                Assert.That(string.Concat(segments.Select(s => s.Dna)), Is.EqualTo(dna));
                foreach (var s in segments)
                    Assert.That(dna.Substring(s.Start - 1, s.End - s.Start + 1), Is.EqualTo(s.Dna));
                // The independent, older path that builds the oligo order must give the same DNA.
                Assert.That(dna, Is.EqualTo(OligoExport.Template(ExportFixtures.Spacer, t7, OligoStrand.Sense, ExportFixtures.ToApp(c))));
                Assert.That(segments.Count(s => s.Role.IsSpacerRegion), Is.EqualTo(1));
            });
        }
    }

    [Test]
    public void T7_IsFirst_17Long_AndTheNextBaseIsTheDirectRepeatsG()
    {
        var segments = FsExport.layout(ExportFixtures.Options(t7: true), ExportFixtures.Guide());
        Assert.Multiple(() =>
        {
            Assert.That(segments[0].Role.IsT7Promoter, Is.True);
            Assert.That((segments[0].Start, segments[0].End), Is.EqualTo((1, 17)));
            Assert.That(segments[1].Role.IsDirectRepeat, Is.True);
            Assert.That(segments[1].Dna[0], Is.EqualTo('G'), "the +1 base");
        });
        Assert.That(FsExport.layout(ExportFixtures.Options(), ExportFixtures.Guide()).Any(s => s.Role.IsT7Promoter), Is.False);
    }

    [Test]
    public void Spacer_AcceptsRnaOrDnaLetters_AndRefusesOtherText()
    {
        var o = ExportFixtures.Options();
        Assert.That(FsExport.sequence(o, ExportFixtures.Guide(spacer: ExportFixtures.Spacer.Replace('T', 'U'))),
            Is.EqualTo(FsExport.sequence(o, ExportFixtures.Guide())));
        Assert.Throws<gRNA.GrnaInputException>(() => FsExport.layout(o, ExportFixtures.Guide(spacer: "ACGX")));
        Assert.Throws<gRNA.GrnaInputException>(() => FsExport.layout(o, ExportFixtures.Guide(spacer: "  ")));
    }
}

public class GenBankExportTests
{
    private static string Export(FsExport.ExportOptions? o = null, params FsExport.ExportGuide[] guides) =>
        FsExport.toGenBank(o ?? ExportFixtures.Options(t7: true), Microsoft.FSharp.Collections.ListModule.OfArray(guides.Length == 0 ? new[] { ExportFixtures.Guide() } : guides));

    private static string Origin(string record) =>
        string.Concat(Regex.Matches(record, @"(?m)^\s*\d+ ([acgt ]+)$").Select(m => m.Groups[1].Value.Replace(" ", "")));

    [Test]
    public void LocusLine_FollowsTheFlatFileColumns()
    {
        var locus = Export().Split('\n')[0];
        var dna = FsExport.sequence(ExportFixtures.Options(t7: true), ExportFixtures.Guide());
        Assert.Multiple(() =>
        {
            Assert.That(locus, Does.Match(@"^LOCUS {7}Diana_001 {7}\s+\d+ bp {4}DNA {5}linear {3}SYN 19-OCT-2026$"));
            Assert.That(locus.Substring(12, 16).Trim(), Is.EqualTo("Diana_001"));
            Assert.That(locus.Substring(29, 11).Trim(), Is.EqualTo(dna.Length.ToString()));
            Assert.That(locus.Substring(41, 2), Is.EqualTo("bp"));
            Assert.That(locus.Substring(55, 6), Is.EqualTo("linear"));
            Assert.That(locus.Substring(64, 3), Is.EqualTo("SYN"));
            Assert.That(locus.Substring(68), Is.EqualTo("19-OCT-2026"));
        });
    }

    [Test]
    public void Origin_IsLowerCase_60PerLine_InGroupsOf10_NumberedFromOne()
    {
        var text = Export();
        var dna = FsExport.sequence(ExportFixtures.Options(t7: true), ExportFixtures.Guide());
        var lines = text.Split('\n').SkipWhile(l => l != "ORIGIN").Skip(1).TakeWhile(l => l != "//").ToList();
        Assert.Multiple(() =>
        {
            Assert.That(Origin(text), Is.EqualTo(dna.ToLowerInvariant()));
            Assert.That(lines, Has.Count.EqualTo((dna.Length + 59) / 60));
            for (var i = 0; i < lines.Count; i++)
            {
                Assert.That(lines[i], Does.StartWith($"{i * 60 + 1,9} "));
                var groups = lines[i].Substring(10).Split(' ');
                Assert.That(groups.Take(groups.Length - 1).All(g => g.Length == 10), Is.True);
            }
        });
    }

    [Test]
    public void Features_HaveTheSameCoordinatesAsTheLayout_AndNoLineIsWiderThan79()
    {
        var o = ExportFixtures.Options(ExportFixtures.Construct(frida: true, bioBricks: true, c3: "AAGG"), t7: true);
        var text = FsExport.toGenBank(o, Microsoft.FSharp.Collections.ListModule.OfArray(new[] { ExportFixtures.Guide() }));
        var segments = FsExport.layout(o, ExportFixtures.Guide());
        var found = Regex.Matches(text, @"(?m)^ {5}(\w+) +(\d+)\.\.(\d+)$").Select(m => (m.Groups[1].Value, int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value))).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(found.First(), Is.EqualTo(("source", 1, segments.Last().End)));
            Assert.That(found.Skip(1).Select(f => (f.Item2, f.Item3)), Is.EqualTo(segments.Select(s => (s.Start, s.End))));
            Assert.That(found.Skip(1).Select(f => f.Item1), Is.EqualTo(segments.Select(s =>
                s.Role.IsT7Promoter ? "regulatory" : s.Role.IsDirectRepeat ? "repeat_region" : "misc_feature")));
            Assert.That(text, Does.Contain("/regulatory_class=promoter").And.Contain("/rpt_type=direct"));
            Assert.That(text.Split('\n').Max(l => l.Length), Is.LessThanOrEqualTo(79));
        });
    }

    [Test]
    public void SeveralGuides_GiveSeveralRecords_EachEndingWithTheTerminator()
    {
        var text = Export(null, ExportFixtures.Guide(), ExportFixtures.Guide("NC_000017.11:g.7674220C>T", true));
        Assert.Multiple(() =>
        {
            Assert.That(Regex.Matches(text, @"(?m)^LOCUS ").Count, Is.EqualTo(2));
            Assert.That(Regex.Matches(text, @"(?m)^//$").Count, Is.EqualTo(2));
            Assert.That(text, Does.Contain("Diana_002").And.Contain("complement strand"));
        });
    }

    [Test]
    public void FreeText_CannotBreakTheFormat()
    {
        var hostile = ExportFixtures.Guide("NG_1:g.1A>T \"quoted\"\nLOCUS injected\r\n//\té", rs: "rs1\"2");
        var text = Export(null, hostile);
        Assert.Multiple(() =>
        {
            Assert.That(Regex.Matches(text, @"(?m)^LOCUS ").Count, Is.EqualTo(1), "a newline in the name must not start a record");
            Assert.That(Regex.Matches(text, @"(?m)^//$").Count, Is.EqualTo(1));
            Assert.That(text, Does.Not.Contain("\r").And.Not.Contain("\t"));
            Assert.That(text.All(c => c == '\n' || (c >= ' ' && c <= '~')), Is.True, "printable ASCII only");
        });
    }

    [Test]
    public void Comment_CarriesTheProvenance_AndOmitsWhatIsUnknown()
    {
        var full = Export();
        var bare = Export(null, ExportFixtures.Guide(rs: null, rank: null, score: null, offtargets: null));
        Assert.Multiple(() =>
        {
            Assert.That(full, Does.Contain("rs334").And.Contain("Rank: 1.").And.Contain("Offtargets").And.Contain("has not been"));
            Assert.That(bare, Does.Not.Contain("dbSNP").And.Not.Contain("Rank:").And.Not.Contain("Offtargets"));
            Assert.That(bare, Does.Contain("Variant: NG_008690.2:g.5000A>T."));
        });
    }

    [Test]
    public void SameInput_GivesIdenticalBytes()
    {
        var first = Export();
        var second = Export();
        Assert.That(second, Is.EqualTo(first));
    }

    [Test]
    public void NoGuides_IsRefused() =>
        Assert.Throws<gRNA.GrnaInputException>(() => FsExport.toGenBank(ExportFixtures.Options(), Microsoft.FSharp.Collections.FSharpList<FsExport.ExportGuide>.Empty));
}

public class SbolExportTests
{
    private static readonly XNamespace Rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
    private static readonly XNamespace Sbol = "http://sbols.org/v3#";

    private static XDocument Export(FsExport.ExportOptions? o = null, params FsExport.ExportGuide[] guides) =>
        XDocument.Parse(FsExport.toSbol3(o ?? ExportFixtures.Options(ExportFixtures.Construct(frida: true), t7: true),
            Microsoft.FSharp.Collections.ListModule.OfArray(guides.Length == 0 ? new[] { ExportFixtures.Guide() } : guides)));

    private static string About(XElement e) => (string)e.Attribute(Rdf + "about")!;
    private static string Resource(XElement e, string child) => e.Elements(Sbol + child).Select(c => (string?)c.Attribute(Rdf + "resource")).First()!;

    [Test]
    public void Document_HasOneComponentAndOneSequencePerGuide_WithMatchingElements()
    {
        var o = ExportFixtures.Options(ExportFixtures.Construct(frida: true), t7: true);
        var doc = Export(o, ExportFixtures.Guide(), ExportFixtures.Guide("NC_000017.11:g.7674220C>T", true));
        var components = doc.Root!.Elements(Sbol + "Component").ToList();
        var sequences = doc.Root.Elements(Sbol + "Sequence").ToDictionary(About);
        var dna = FsExport.sequence(o, ExportFixtures.Guide()).ToLowerInvariant();

        Assert.Multiple(() =>
        {
            Assert.That(components, Has.Count.EqualTo(2));
            Assert.That(sequences, Has.Count.EqualTo(2));
            foreach (var c in components)
            {
                Assert.That(sequences[Resource(c, "hasSequence")].Element(Sbol + "elements")!.Value, Is.EqualTo(dna));
                Assert.That(c.Elements(Sbol + "type").Select(t => (string)t.Attribute(Rdf + "resource")!),
                    Is.EquivalentTo(new[] { "https://identifiers.org/SBO:0000251", "https://identifiers.org/SO:0000987" }));
                Assert.That(Resource(c, "hasNamespace"), Is.EqualTo(FsExport.DefaultNamespace));
            }
        });
    }

    [Test]
    public void Ranges_AreOneBasedInclusive_AndEqualTheLayout()
    {
        var o = ExportFixtures.Options(ExportFixtures.Construct(frida: true, bioBricks: true), t7: true);
        var comp = Export(o).Root!.Element(Sbol + "Component")!;
        var ranges = comp.Elements(Sbol + "hasFeature").Select(f => f.Element(Sbol + "SequenceFeature")!)
            .Select(f => (Name: f.Element(Sbol + "name")!.Value, Role: Resource(f, "role"), Range: f.Element(Sbol + "hasLocation")!.Element(Sbol + "Range")!))
            .Select(x => (x.Name, x.Role, Start: int.Parse(x.Range.Element(Sbol + "start")!.Value), End: int.Parse(x.Range.Element(Sbol + "end")!.Value),
                Orientation: Resource(x.Range, "orientation"))).ToList();
        var segments = FsExport.layout(o, ExportFixtures.Guide());

        Assert.Multiple(() =>
        {
            Assert.That(ranges.Select(r => (r.Start, r.End)), Is.EqualTo(segments.Select(s => (s.Start, s.End))));
            Assert.That(ranges.Select(r => r.Name), Is.EqualTo(segments.Select(s => s.Label)));
            Assert.That(ranges.All(r => r.Orientation == "http://sbols.org/v3#inline"), Is.True);
            Assert.That(ranges.First().Role, Is.EqualTo("https://identifiers.org/SO:0001207"), "T7 promoter");
            Assert.That(ranges.Single(r => r.Name.EndsWith(" DR")).Role, Is.EqualTo("https://identifiers.org/SO:0000314"));
        });
    }

    [Test]
    public void Identities_AreUnderTheNamespace_AndDisplayIdsAreValidAndUnique()
    {
        var doc = Export(null, ExportFixtures.Guide("1:g.5A>T"), ExportFixtures.Guide("1:g.5A>T"), ExportFixtures.Guide("1:g.5A>T", true), ExportFixtures.Guide("rs 1/<&>"));
        var tops = doc.Root!.Elements().Where(e => e.Attribute(Rdf + "about") is not null).ToList();
        var uris = doc.Descendants().Select(e => (string?)e.Attribute(Rdf + "about")).Where(u => u is not null).ToList();
        var ids = tops.Select(t => t.Element(Sbol + "displayId")!.Value).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(uris, Is.Unique);
            Assert.That(uris.All(u => u!.StartsWith(FsExport.DefaultNamespace + "/")), Is.True);
            Assert.That(ids, Is.Unique);
            Assert.That(ids.All(i => Regex.IsMatch(i, "^[A-Za-z_][A-Za-z0-9_]*$")), Is.True);
            foreach (var t in tops) Assert.That(About(t), Is.EqualTo(FsExport.DefaultNamespace + "/" + t.Element(Sbol + "displayId")!.Value));
        });
    }

    [Test]
    public void HostileText_IsEscapedAndCannotBreakTheXml()
    {
        var doc = Export(null, ExportFixtures.Guide("<script>alert(1)</script>&\"'\u0001\u0000]]>"));
        var comp = doc.Root!.Element(Sbol + "Component")!;
        Assert.Multiple(() =>
        {
            Assert.That(comp.Element(Sbol + "name")!.Value, Does.Contain("<script>"), "kept as text, not as markup");
            Assert.That(doc.Descendants().Any(e => e.Name.LocalName == "script"), Is.False);
        });
    }

    [Test]
    public void Namespace_IsValidated_AndATrailingSlashIsRemoved()
    {
        Assert.That(FsExport.normaliseNamespace("https://example.org/lab/"), Is.EqualTo("https://example.org/lab"));
        foreach (var bad in new[] { "", "not a url", "ftp://example.org/x", "https://example.org/a#b", "https://example.org/a b" })
            Assert.Throws<gRNA.GrnaInputException>(() => FsExport.normaliseNamespace(bad), bad);
        var doc = Export(ExportFixtures.Options(ns: "https://example.org/lab/"));
        Assert.That(About(doc.Root!.Element(Sbol + "Component")!), Does.StartWith("https://example.org/lab/NG_008690_2_g_5000A_T"));
    }

    [Test]
    public void SameInput_GivesIdenticalBytes()
    {
        var guides = Microsoft.FSharp.Collections.ListModule.OfArray(new[] { ExportFixtures.Guide() });
        var first = FsExport.toSbol3(ExportFixtures.Options(), guides);
        Assert.That(FsExport.toSbol3(ExportFixtures.Options(), guides), Is.EqualTo(first));
    }
}

public class ShortlistExportTests
{
    private static readonly ShortlistItem[] Items =
    {
        new("NG_008690.2:g.5000A>T", false, ExportFixtures.Spacer, "rs334", 0.8, 2),
        new("NC_000017.11:g.7674220C>T", true, "ACGUACGUACGUACGUACGUACGUACGU", null, 1, 0),
    };

    [Test]
    public void ShortlistGenBank_OneRecordPerItem_WithTheShortlistsConstructAndT7()
    {
        var construct = new ConstructOptions(Frida: true);
        var text = OligoExport.ShortlistGenBank(Items, true, construct);
        var first = Regex.Match(text, @"(?m)^\s*1 ([acgt ]+)$").Groups[1].Value.Replace(" ", "");
        Assert.Multiple(() =>
        {
            Assert.That(Regex.Matches(text, @"(?m)^LOCUS ").Count, Is.EqualTo(2));
            Assert.That(text, Does.Contain("rs334").And.Contain("Score: 0.8").And.Contain("Frida adapter 5'"));
            Assert.That(first, Is.EqualTo(OligoExport.Template(Items[0].Spacer, true, OligoStrand.Sense, construct).Substring(0, 60).ToLowerInvariant()));
        });
    }

    [Test]
    public void ShortlistSbol_IsWellFormed_AndUsesTheNamespaceGiven()
    {
        var doc = XDocument.Parse(OligoExport.ShortlistSbol(Items, false, null, "https://example.org/team"));
        XNamespace sbol = "http://sbols.org/v3#";
        Assert.That(doc.Root!.Elements(sbol + "Component").Count(), Is.EqualTo(2));
        Assert.That(doc.ToString(), Does.Contain("https://example.org/team/NG_008690_2_g_5000A_T"));
    }

    [Test]
    public void EmptyShortlist_IsRefusedByTheLibrary() =>
        Assert.Throws<gRNA.GrnaInputException>(() => OligoExport.ShortlistGenBank(Array.Empty<ShortlistItem>(), true));
}

public class Sbol2ExportTests
{
    private static readonly XNamespace Rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
    private static readonly XNamespace S2 = "http://sbols.org/v2#";

    private static XDocument Export(FsExport.ExportOptions o, params FsExport.ExportGuide[] guides) =>
        XDocument.Parse(FsExport.toSbol2(o, Microsoft.FSharp.Collections.ListModule.OfArray(guides)));

    [Test]
    public void Document_DeclaresTheSbol2Namespace_AndRefusesNoSbol3Terms()
    {
        var text = FsExport.toSbol2(ExportFixtures.Options(), Microsoft.FSharp.Collections.ListModule.OfArray(new[] { ExportFixtures.Guide() }));
        var doc = XDocument.Parse(text);
        Assert.Multiple(() =>
        {
            Assert.That(doc.Root!.GetNamespaceOfPrefix("sbol")!.NamespaceName, Is.EqualTo("http://sbols.org/v2#"));
            Assert.That(text, Does.Not.Contain("sbols.org/v3"));
            Assert.That(doc.Root.Elements(S2 + "ComponentDefinition").Count(), Is.EqualTo(1));
            Assert.That(doc.Root.Elements(S2 + "Sequence").Count(), Is.EqualTo(1));
        });
    }

    [Test]
    public void Annotations_AreOneBasedInclusive_AndEqualTheLayout_WithCompliantIdentities()
    {
        var o = ExportFixtures.Options(ExportFixtures.Construct(frida: true, bioBricks: true), t7: true);
        var doc = Export(o, ExportFixtures.Guide());
        var cd = doc.Root!.Element(S2 + "ComponentDefinition")!;
        var ranges = cd.Elements(S2 + "sequenceAnnotation").Select(a => a.Element(S2 + "SequenceAnnotation")!.Element(S2 + "location")!.Element(S2 + "Range")!)
            .Select(r => (int.Parse(r.Element(S2 + "start")!.Value), int.Parse(r.Element(S2 + "end")!.Value))).ToList();
        var seq = doc.Root.Element(S2 + "Sequence")!;

        Assert.Multiple(() =>
        {
            Assert.That(ranges, Is.EqualTo(FsExport.layout(o, ExportFixtures.Guide()).Select(s => (s.Start, s.End)).ToList()));
            Assert.That(seq.Element(S2 + "elements")!.Value, Is.EqualTo(FsExport.sequence(o, ExportFixtures.Guide()).ToLowerInvariant()));
            var about = (string)cd.Attribute(Rdf + "about")!;
            Assert.That(about, Does.EndWith("/1"));
            Assert.That((string)cd.Element(S2 + "persistentIdentity")!.Attribute(Rdf + "resource")!, Is.EqualTo(about[..^2]));
            Assert.That(cd.Element(S2 + "version")!.Value, Is.EqualTo("1"));
            Assert.That((string)cd.Element(S2 + "sequence")!.Attribute(Rdf + "resource")!, Is.EqualTo((string)seq.Attribute(Rdf + "about")!));
            var uris = doc.Descendants().Select(e => (string?)e.Attribute(Rdf + "about")).Where(u => u is not null).ToList();
            Assert.That(uris, Is.Unique);
        });
    }

    [Test]
    public void HostileText_CannotBreakTheXml() =>
        Assert.DoesNotThrow(() => Export(ExportFixtures.Options(), ExportFixtures.Guide("<a>&\"'\u0001]]>")));
}

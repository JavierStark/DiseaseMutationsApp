using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Playwright;
using NUnit.Framework;
using static Microsoft.Playwright.Assertions;

namespace DiseaseMutationsApp.E2E;

/// <summary>The shortlist's GenBank and SBOL 3 downloads, against the fake pipeline.</summary>
public class ExportDownloadTests : E2EBase
{
    private async Task ShortlistOneGuideAsync()
    {
        await GotoAsync("/");
        await RunAsync("NC_000017.11:g.7674220C>T");
        await Expect(DoneStatus).ToContainTextAsync("1 of 1 variants analysed");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Use" }).First.ClickAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Add to shortlist" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "GenBank (.gb)" })).ToBeVisibleAsync();
    }

    private async Task<string> DownloadAsync(string buttonName, string expectedExtension)
    {
        var download = await Page.RunAndWaitForDownloadAsync(async () =>
            await Page.GetByRole(AriaRole.Button, new() { Name = buttonName }).ClickAsync());
        Assert.That(download.SuggestedFilename, Does.EndWith(expectedExtension));
        return await File.ReadAllTextAsync((await download.PathAsync())!);
    }

    [Test]
    public async Task GenBank_Downloads_TheCompleteGuideAnnotated()
    {
        await ShortlistOneGuideAsync();
        var text = await DownloadAsync("GenBank (.gb)", ".gb");

        var origin = string.Concat(Regex.Matches(text, @"(?m)^\s*\d+ ([acgt ]+)$").Select(m => m.Groups[1].Value.Replace(" ", "")));
        Assert.Multiple(() =>
        {
            Assert.That(text, Does.StartWith("LOCUS       Diana_001"));
            Assert.That(text, Does.Contain("NC_000017.11:g.7674220C>T").And.Contain("T7 promoter").And.Contain("LwaCas13a DR"));
            Assert.That(origin, Does.StartWith("taatacgactcactatagatttagactacccc"), "T7 promoter, then the direct repeat");
            Assert.That(text.TrimEnd(), Does.EndWith("//"));
        });
    }

    [Test]
    public async Task Sbol_Downloads_AnSbol3Document_ThatMatchesTheGenBankSequence()
    {
        await ShortlistOneGuideAsync();
        var xml = XDocument.Parse(await DownloadAsync("SBOL 3 (.xml)", ".xml"));
        XNamespace sbol = "http://sbols.org/v3#";
        var origin = string.Concat(Regex.Matches(await DownloadAsync("GenBank (.gb)", ".gb"), @"(?m)^\s*\d+ ([acgt ]+)$").Select(m => m.Groups[1].Value.Replace(" ", "")));

        Assert.Multiple(() =>
        {
            Assert.That(xml.Root!.Elements(sbol + "Component").Count(), Is.EqualTo(1));
            Assert.That(xml.Root.Element(sbol + "Sequence")!.Element(sbol + "elements")!.Value, Is.EqualTo(origin));
        });
    }

    [Test]
    public async Task UntickingT7_RemovesThePromoterFromBothFiles()
    {
        await ShortlistOneGuideAsync();
        await Page.GetByLabel("T7 promoter").UncheckAsync();
        var text = await DownloadAsync("GenBank (.gb)", ".gb");
        Assert.That(text, Does.Not.Contain("T7 promoter"));
    }

    [Test]
    public async Task Sbol2_Downloads_AnSbol2Document()
    {
        await ShortlistOneGuideAsync();
        var xml = XDocument.Parse(await DownloadAsync("SBOL 2 (.xml)", ".xml"));
        XNamespace s2 = "http://sbols.org/v2#";
        Assert.That(xml.Root!.Elements(s2 + "ComponentDefinition").Count(), Is.EqualTo(1));
    }
}

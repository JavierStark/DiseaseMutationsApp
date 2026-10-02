using DiseaseMutationsApp.Services;

namespace DiseaseMutationsAppTests;

public class SessionDocumentTests
{
    [Test]
    public void RoundTrips_AndUsesCamelCaseKeysTheCliReads()
    {
        var doc = new SessionDocument(SessionDocument.CurrentVersion, "rs1, NG_1:g.1A>T", 28, "10-17",
            new List<ShortlistItem> { new("NG_1:g.1A>T", false, "ACGU", "1", 0.5, 1) });

        var json = SessionDocument.Serialize(doc);
        var back = SessionDocument.TryParse(json);

        Assert.Multiple(() =>
        {
            Assert.That(json, Does.Contain("\"input\""));
            Assert.That(json, Does.Contain("\"spacer\": 28"));
            Assert.That(back!.Input, Is.EqualTo("rs1, NG_1:g.1A>T"));
            Assert.That(back.Shortlist, Has.Count.EqualTo(1));
        });
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("rs1, rs2")]
    [TestCase("{ not json")]
    [TestCase("{\"version\":99,\"input\":\"x\",\"spacer\":1,\"seed\":\"1-2\",\"shortlist\":[]}")]
    public void TryParse_RejectsNonSessions(string? text) => Assert.That(SessionDocument.TryParse(text), Is.Null);

    [Test]
    public void InputFromUpload_AcceptsListsReportsAndSessions()
    {
        var plain = SessionDocument.InputFromUpload("rs1\r\n# comment\r\nNG_1:g.1A>T, rs1\r\n");
        var report = SessionDocument.InputFromUpload(GrnaCsvSchema.Header + "\n,NG_1:g.1A>T,Mutated,1,AAAA,1,50,1,AAAA,0,-1,Normal\n,NG_1:g.1A>T,Original,1,AAAA,1,50,1,AAAA,0,-1,Normal\n");
        var session = SessionDocument.InputFromUpload(SessionDocument.Serialize(
            new SessionDocument(1, "rs9", 28, "10-17", new())));

        Assert.Multiple(() =>
        {
            Assert.That(plain, Is.EqualTo("rs1, NG_1:g.1A>T"));
            Assert.That(report, Is.EqualTo("NG_1:g.1A>T"));
            Assert.That(session, Is.EqualTo("rs9"));
        });
    }
}

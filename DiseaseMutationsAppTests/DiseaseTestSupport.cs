using System.Net;
using System.Text;
using System.Web;
using gRNA;
using static gRNA.DiseaseVariants;

namespace DiseaseMutationsAppTests;

/// <summary>Stub HTTP layer shared by the disease client tests and the CLI tests: no network, a fake clock.</summary>
internal static class Stubs
{
    public const string MonarchBase = "https://monarch.test/v3/api";
    public const string EutilsBase = "https://eutils.test/entrez/eutils";

    public static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    /// <summary>NCBI answers for the PKU walk-through, using the captured responses.</summary>
    public static Func<Seen, HttpResponseMessage> NcbiPku(Func<Seen, HttpResponseMessage>? fallback = null) => seen => seen.Endpoint switch
    {
        "elink.fcgi" => Json(DiseaseFixtures.Load("elink_history_pku.json")),
        "esearch.fcgi" => Json(DiseaseFixtures.Load("esearch_pku_filtered.json")),
        "esummary.fcgi" => Json(DiseaseFixtures.Load("esummary_sample.json")),
        _ => fallback?.Invoke(seen) ?? new HttpResponseMessage(HttpStatusCode.NotFound)
    };

    /// <summary>Parent from the real fixture; every subtype answers with one invented OMIM number derived from its id.</summary>
    public static Func<Seen, HttpResponseMessage> FanconiTree(HashSet<string>? notFound = null) => seen =>
    {
        var id = Uri.UnescapeDataString(seen.Url.AbsolutePath.Split('/').Last());
        if (id == "MONDO:0019391") return Json(DiseaseFixtures.Load("monarch_entity_fanconi.json"));
        if (notFound?.Contains(id) == true) return new HttpResponseMessage(HttpStatusCode.NotFound);
        var n = id["MONDO:".Length..].TrimStart('0');
        return Json($$"""{"id":"{{id}}","name":"sub {{n}}","xref":["OMIM:{{200000 + int.Parse(n)}}"]}""");
    };
}

/// <summary>One request the client made, with its body already read.</summary>
internal sealed record Seen(HttpMethod Method, Uri Url, string Body)
{
    public string Endpoint => Url.AbsolutePath.Split('/').Last();
    public System.Collections.Specialized.NameValueCollection Form => HttpUtility.ParseQueryString(Body);
}

/// <summary>A stub HTTP layer plus a fake clock: waits advance the clock instead of sleeping.</summary>
internal sealed class Rig
{
    public readonly List<Seen> Requests = new();
    public readonly List<TimeSpan> Waits = new();
    public DateTimeOffset Clock = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);
    public Func<Seen, HttpResponseMessage> Respond = _ => new HttpResponseMessage(HttpStatusCode.NotFound);
    public ClientOptions Options;
    public DiseaseVariantClient Client;

    public Rig(Func<ClientOptions, ClientOptions>? tweak = null)
    {
        var options = ClientOptionsModule.create(Stubs.MonarchBase, Stubs.EutilsBase,
            (d, ct) => { Waits.Add(d); Clock += d; return Task.CompletedTask; },
            () => Clock);
        Options = tweak?.Invoke(options) ?? options;
        var http = new HttpClient(new StubHandler(this));
        Client = new DiseaseVariantClient(http, Options, new RateGate(Options));
    }

    public IEnumerable<Seen> Ncbi => Requests.Where(r => r.Url.Host == "eutils.test");
    public IEnumerable<Seen> Monarch => Requests.Where(r => r.Url.Host == "monarch.test");
}

internal sealed class StubHandler : HttpMessageHandler
{
    private readonly Rig _rig;
    public StubHandler(Rig rig) => _rig = rig;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        var seen = new Seen(request.Method, request.RequestUri!, body);
        _rig.Requests.Add(seen);
        return _rig.Respond(seen);
    }
}

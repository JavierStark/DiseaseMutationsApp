# Running the library as a separate service

This is documentation only: there is no service project in this repository. It describes how to wrap the library in an
HTTP service, if you want one (for example to share one index between several front-ends).

> The project used to be a Blazor WebAssembly client plus a separate API. It was merged into one Blazor Server process
> (see the README's Migration Notes) because a single local container is simpler to deploy and the API added a hop
> without adding capability. Reintroduce a service only if you really need to share one deployment between consumers.

## Endpoint sketch

All bodies are JSON and mirror the `grna` CLI's `--format json`.

| Endpoint | Request | Response |
|---|---|---|
| `POST /design` | `{ "hgvs": "NG_008690.2:g.5000A>T", "spacer": 28, "seed": [10, 17], "complement": false }` | `{ "hgvs", "strand", "mutated": [candidate...], "original": [candidate...] }` (candidate: `rank, sequence, score, gcContent, alignments, seedRegion, homopolymers, energy, structure`) |
| `POST /resolve` | `{ "rsid": "rs334" }` | `{ "rsid", "hgvs": ["NG_000007.3:g.70614A>T", ...] }` (empty list = no `NG_` mapping) |
| `POST /fold` | `{ "sequence": "GAUUUAGAC..." }` | `{ "structure", "energy" }` |
| `POST /pool` | `{ "guides": ["..."] \| "count": 100, "capacity": 5, "plate": 96, "model": "auto" }` | `{ "model", "wells", "pools": [{ "id", "name", "guides", "well" }] }` |
| `GET /healthz` | | the diagnostics report; 503 when a required check fails |

Map the library's typed exceptions to status codes: `GrnaInputException` to 400, `GrnaDependencyException` to 503,
`GrnaUpstreamException` to 502. Accept a cancellation token from the request so a client disconnect kills the child processes.

## Skeleton (ASP.NET Core minimal API)

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<gRNA.Services.BowtieService>();   // exactly one per process
var app = builder.Build();

app.MapPost("/design", async (DesignRequest r, gRNA.Services.BowtieService bowtie, CancellationToken ct) =>
{
    var result = await Main.getBestgRNAFromHGVS(r.Hgvs, r.Spacer, r.Seed[0], r.Seed[1], bowtie, ct, r.Complement);
    return Results.Ok(result);
});
app.MapGet("/healthz", async () => /* Diagnostics.runAll(GrnaEnvironmentModule.getCurrent(), false) */ Results.Ok());
app.Run();
```

## Dockerfile

Derive from the same index base image so the index stays in the layer (never a volume, see
[runtime-contract.md](runtime-contract.md)):

```dockerfile
ARG BOWTIE_BASE=disease-mutations-bowtie:grch38-noalt-20260526
FROM ${BOWTIE_BASE}
# ... python3 + "viennarna==2.7.2", the published service, the checksum-verified bowtie-align-s ...
USER app
ENTRYPOINT ["dotnet", "GrnaService.dll"]
```

## Compose service

```yaml
services:
  grna-service:
    build: ./service
    ports: ["127.0.0.1:8080:8080"]
    environment: { ASPNETCORE_URLS: "http://+:8080", GRNA_NCBI_API_KEY: "${GRNA_NCBI_API_KEY}" }
    deploy: { resources: { limits: { memory: 4G, cpus: "1.5" } } }
```

## Scaling

Bowtie is serialised per process (one alignment batch at a time) and every replica needs its own in-image index. So you
scale by **replicas at roughly index-size memory each (~4 GB)**, not by threads. Put a load balancer in front and keep
requests for one variant on one replica if you want to benefit from its sequence cache.

## Pointing the Blazor app at it

The app calls the library in-process through `GrnaService`. To use a remote service, implement `IGrnaAnalysis`
(`GetBestgRNAFromHgvs`, `GetHgvsFromSnp`, `GetNcbiNuccoreUrl`) with an `HttpClient` and register it in place of
`GrnaService`; `AnalysisRunner` depends only on that interface. `GetRnaFold` is used directly by the detail panel and would
need the same treatment.

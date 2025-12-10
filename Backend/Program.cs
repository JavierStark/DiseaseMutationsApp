using gRNA;
using static Microsoft.FSharp.Control.FSharpAsync;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Add CORS services
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins("http://localhost:8080")
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(10);
    serverOptions.Limits.RequestHeadersTimeout = TimeSpan.FromMinutes(10);
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Enable CORS
app.UseCors("AllowFrontend");

app.UseHttpsRedirection();

app.MapGet("/", () => "Disease Mutations Backend is running.");



app.MapGet("/getallignments", async (string sequence, int mismatches, int threads) => 
    Results.Ok(await BowtieWrapper.runBowtie(mismatches, threads, sequence))).WithName("GetAllAlignments");

app.MapGet("/getbestgrnafromhgvs", async (string hgvs, int window) => 
{
    var fsharpAsync = Main.getBestgRNAFromHGVS(hgvs, window);
    var task = StartAsTask(fsharpAsync, null, null);
    var result = await task;
    return Results.Ok(result);
}).WithName("GetBestgRNAFromHgvs");

app.MapGet("/gethgvsfromsnp", async (string rsid) => 
{
    var fsharpAsync = SNP.getHgvsNotationsAsync(rsid);
    var task = StartAsTask(fsharpAsync, null, null);
    var result = await task;
    Console.WriteLine($"HGVS notations for rsid {rsid}: {string.Join(", ", result)}");
    return Results.Ok(result.ToList());
}).WithName("GetHGVSFromSNP");

app.MapGet("/getrsfromomim", async (int omim) =>
{
    var fsharpAsync = Omim.rsFromOmim(omim);
    var task = StartAsTask(fsharpAsync, null, null);
    var result = await task;
    return Results.Ok(result);
}).WithName("GetRsFromOmim");

app.MapGet("/getrnafold", async (string sequence) => 
    Results.Ok(await RNAFoldWrapper.fold(sequence))).WithName("GetRNAFold");

app.MapGet("/getfornaurl", (string sequence, string structure) => 
{
    var url = $"http://nibiru.tbi.univie.ac.at/forna/forna.html?id=url/name&sequence={sequence}&structure={structure}";
    return Results.Ok(url);
}).WithName("GetFornaUrl");



app.Run();
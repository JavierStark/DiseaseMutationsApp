var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
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

app.UseHttpsRedirection();

app.MapGet("/", () => "Disease Mutations Backend is running.");

app.MapGet("/getbestrna", async (int window, string sequence) => 
    Results.Ok(await gRNA.SpacerFinder.getBestgRNA(window, sequence))).WithName("GetBestgRNA");
app.MapGet("/getallignments", async (string sequence, int mismatches, int threads) => 
    Results.Ok(await gRNA.BowtieWrapper.runBowtie(mismatches, threads, sequence))).WithName("GetAllAlignments");

app.Run();
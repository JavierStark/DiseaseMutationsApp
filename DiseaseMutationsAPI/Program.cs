using System.Diagnostics;
using System.Runtime.InteropServices;

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure Swagger
app.UseSwagger();
app.UseSwaggerUI();

// Paths
var indexPath = "/tmp/indexes";   // local ephemeral index path
var bowtieScriptPath = "/tmp/bowtie"; // Python wrapper / launcher in /tmp

// Ensure indexes exist (copy if needed)
var sharedIndexPath = "/app/indexes";
if (!Directory.Exists(indexPath))
{
    Console.WriteLine("Copying Bowtie indexes to /tmp...");
    Directory.CreateDirectory(indexPath);
    foreach (var dirPath in Directory.GetDirectories(sharedIndexPath, "*", SearchOption.AllDirectories))
        Directory.CreateDirectory(dirPath.Replace(sharedIndexPath, indexPath));
    foreach (var newPath in Directory.GetFiles(sharedIndexPath, "*.*", SearchOption.AllDirectories))
        File.Copy(newPath, newPath.Replace(sharedIndexPath, indexPath), true);
}

// Bowtie endpoint
app.MapGet("/bowtie", async (string sequence, int mismatches, HttpContext http) =>
{
    if (string.IsNullOrWhiteSpace(sequence))
        return Results.BadRequest("Missing or empty sequence.");
    if (mismatches < 0 || mismatches > 10)
        return Results.BadRequest("Invalid mismatch count (0–10).");

    var cts = CancellationTokenSource.CreateLinkedTokenSource(http.RequestAborted);
    cts.CancelAfter(TimeSpan.FromMinutes(5));

    string outputFile = Path.Combine(AppContext.BaseDirectory, "offtargets.txt");

    if (!File.Exists(bowtieScriptPath))
        return Results.Problem($"Bowtie script not found at {bowtieScriptPath}", statusCode: 500);

    if (File.Exists(outputFile))
        File.Delete(outputFile);

    var psi = new ProcessStartInfo
    {
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        CreateNoWindow = true,
        WorkingDirectory = AppContext.BaseDirectory
    };

    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
    {
        // Use Python on Windows
        psi.FileName = "python3";
        psi.ArgumentList.Add(bowtieScriptPath);
    }
    else
    {
        // Use /tmp/bowtie directly on Linux
        psi.FileName = bowtieScriptPath;
    }

    // Bowtie arguments
    psi.ArgumentList.Add("-v");
    psi.ArgumentList.Add(mismatches.ToString());
    psi.ArgumentList.Add("-a");
    psi.ArgumentList.Add("-x");
    psi.ArgumentList.Add(Path.Combine(indexPath, "grch38_1kgmaj")); // index prefix in /tmp
    psi.ArgumentList.Add("-c");
    psi.ArgumentList.Add(sequence);

    try
    {
        using var process = new Process { StartInfo = psi };
        process.Start();

        string stdout = await process.StandardOutput.ReadToEndAsync();
        string stderr = await process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync(cts.Token);

        string? resultFileContent = null;
        if (File.Exists(outputFile))
            resultFileContent = await File.ReadAllTextAsync(outputFile, cts.Token);

        var response = new
        {
            ExitCode = process.ExitCode,
            StdOut = stdout,
            StdErr = stderr,
            ResultFile = resultFileContent
        };

        return Results.Json(response);
    }
    catch (OperationCanceledException)
    {
        return Results.Problem("Process cancelled or timed out.", statusCode: 504);
    }
    catch (Exception ex)
    {
        return Results.Problem($"Error executing Bowtie: {ex.Message}", statusCode: 500);
    }
});

app.Run();

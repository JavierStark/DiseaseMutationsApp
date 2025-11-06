using System.Diagnostics;
using System.Runtime.InteropServices;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
// if (app.Environment.IsDevelopment())
// {
    app.UseSwagger();
    app.UseSwaggerUI();
// }

app.MapGet("/bowtie", async (string sequence, int mismatches, HttpContext http) =>
{
    if (string.IsNullOrWhiteSpace(sequence))
        return Results.BadRequest("Missing or empty sequence.");
    if (mismatches < 0 || mismatches > 10)
        return Results.BadRequest("Invalid mismatch count (0–10).");

    var cts = CancellationTokenSource.CreateLinkedTokenSource(http.RequestAborted);
    cts.CancelAfter(TimeSpan.FromMinutes(5)); // timeout safeguard

    string workingDir = AppContext.BaseDirectory;
    string scriptPath = Path.Combine(workingDir, "bowtie"); // your Python script
    string outputFile = Path.Combine(workingDir, "offtargets.txt");

    if (!File.Exists(scriptPath))
        return Results.Problem($"Bowtie script not found at {scriptPath}", statusCode: 500);

    // Remove old output if exists
    if (File.Exists(outputFile))
        File.Delete(outputFile);

    // Cross-platform command setup
    string executable;
    var psi = new ProcessStartInfo
    {
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        CreateNoWindow = true,
        WorkingDirectory = workingDir
    };

    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
    {
        // On Windows, use python interpreter
        executable = "python3"; // or "python3" depending on your system
        psi.FileName = executable;
        psi.ArgumentList.Add(scriptPath);
    }
    else
    {
        // On Linux/macOS, execute script directly (shebang will invoke python3)
        executable = "./bowtie";
        psi.FileName = executable;
    }

    // Add arguments
    psi.ArgumentList.Add("-v");
    psi.ArgumentList.Add(mismatches.ToString());
    psi.ArgumentList.Add("-a");
    psi.ArgumentList.Add("-x");
    psi.ArgumentList.Add("grch38_1kgmaj"); // your index prefix
    psi.ArgumentList.Add("-c");
    psi.ArgumentList.Add(sequence);

    try
    {
        using var process = new Process { StartInfo = psi };
        process.Start();

        string stdout = await process.StandardOutput.ReadToEndAsync();
        string stderr = await process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync(cts.Token);

        int exitCode = process.ExitCode;
        string? resultFileContent = null;
        if (File.Exists(outputFile))
            resultFileContent = await File.ReadAllTextAsync(outputFile, cts.Token);

        var response = new
        {
            ExitCode = exitCode,
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
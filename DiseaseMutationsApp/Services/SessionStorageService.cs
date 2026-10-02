using System.Text.Json;
using Microsoft.JSInterop;

namespace DiseaseMutationsApp.Services;

/// <summary>
/// The small, versioned snapshot persisted across a page refresh: inputs and parameters only.
/// Results are never persisted (they are megabytes); the exported CSV is the durable artifact.
/// </summary>
public record SessionSnapshot(int Version, string? Input, int SpacerSize, int SeedStart, int SeedEnd, string? ActiveTab, DateTimeOffset SavedAt)
{
    public const int CurrentVersion = 1;
}

/// <summary>Reads and writes the snapshot in the browser's localStorage. Never throws.</summary>
public class SessionStorageService
{
    private const string Key = "grna.session.v1";
    private readonly IJSRuntime _js;
    private CancellationTokenSource? _pendingSave;

    public SessionStorageService(IJSRuntime js) => _js = js;

    /// <summary>Returns null for anything unusable: missing, corrupt, wrong version, JS unavailable.</summary>
    public async Task<SessionSnapshot?> TryLoadAsync()
    {
        try
        {
            var json = await _js.InvokeAsync<string?>("localStorage.getItem", Key);
            return Deserialize(json);
        }
        catch
        {
            return null;
        }
    }

    public static SessionSnapshot? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var snapshot = JsonSerializer.Deserialize<SessionSnapshot>(json);
            return snapshot is { Version: SessionSnapshot.CurrentVersion } ? snapshot : null;
        }
        catch
        {
            return null;
        }
    }

    public static string Serialize(SessionSnapshot snapshot) => JsonSerializer.Serialize(snapshot);

    /// <summary>Debounced, fire-and-forget save. Safe to call from any lifecycle method except Dispose.</summary>
    public void SaveDebounced(SessionSnapshot snapshot)
    {
        var cts = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _pendingSave, cts);
        previous?.Cancel();
        _ = SaveAfterDelayAsync(snapshot, cts.Token);
    }

    private async Task SaveAfterDelayAsync(SessionSnapshot snapshot, CancellationToken token)
    {
        try
        {
            await Task.Delay(500, token);
            await _js.InvokeVoidAsync("localStorage.setItem", token, Key, Serialize(snapshot));
        }
        catch
        {
            // Quota exceeded, circuit gone, cancelled by a newer save: all fine to drop.
        }
    }

    public async Task ClearAsync()
    {
        try { await _js.InvokeVoidAsync("localStorage.removeItem", Key); }
        catch { /* best effort */ }
    }
}

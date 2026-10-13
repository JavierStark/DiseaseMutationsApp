using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace DiseaseMutationsApp.E2E;

/// <summary>Collects the server's warnings and errors (including exceptions caught by error boundaries) so a failing browser test can show them.</summary>
public sealed class ServerLog : ILoggerProvider
{
    public static readonly ConcurrentQueue<string> Entries = new();

    public ILogger CreateLogger(string categoryName) => new Logger(categoryName);
    public void Dispose() { }

    private sealed class Logger(string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            Entries.Enqueue($"[server {logLevel}] {category}: {formatter(state, exception)}{(exception is null ? "" : Environment.NewLine + exception)}");
        }
    }
}

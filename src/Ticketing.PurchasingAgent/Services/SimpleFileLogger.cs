using Microsoft.Extensions.Logging;

namespace Ticketing.PurchasingAgent.Services;

/// <summary>
/// Optional file logger that lets you tail PurchasingAgent activity from the
/// shell without going through the Aspire dashboard. Activated by setting
/// PURCHASING_AGENT_LOG_FILE in the environment.
/// </summary>
public sealed class SimpleFileLoggerProvider : ILoggerProvider
{
    private readonly string _path;
    private readonly object _gate = new();

    public SimpleFileLoggerProvider(string path)
    {
        _path = path;
        try { File.WriteAllText(_path, $"--- log start {DateTime.UtcNow:O} ---\n"); }
        catch { }
    }

    public ILogger CreateLogger(string categoryName) => new SimpleFileLogger(this, categoryName);

    public void Dispose() { }

    internal void Write(string line)
    {
        lock (_gate)
        {
            try { File.AppendAllText(_path, line); } catch { }
        }
    }

    private sealed class SimpleFileLogger : ILogger
    {
        private readonly SimpleFileLoggerProvider _provider;
        private readonly string _category;

        public SimpleFileLogger(SimpleFileLoggerProvider provider, string category)
        {
            _provider = provider;
            _category = category;
        }

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            var msg = formatter(state, exception);
            var line = $"{DateTime.UtcNow:HH:mm:ss.fff} [{logLevel.ToString()[..4]}] {_category}: {msg}\n";
            if (exception != null) line += $"  EX: {exception}\n";
            _provider.Write(line);
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}

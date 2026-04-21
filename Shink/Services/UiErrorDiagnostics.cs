using Microsoft.Extensions.Logging;

namespace Shink.Services;

public sealed record UiErrorDiagnosticEntry(
    DateTimeOffset OccurredAtUtc,
    string Category,
    string Level,
    string Message,
    string? ExceptionText);

public sealed class UiErrorDiagnosticsStore
{
    private const int MaxEntries = 40;
    private readonly object _gate = new();
    private readonly LinkedList<UiErrorDiagnosticEntry> _entries = new();

    public void Add(UiErrorDiagnosticEntry entry)
    {
        lock (_gate)
        {
            _entries.AddFirst(entry);
            while (_entries.Count > MaxEntries)
            {
                _entries.RemoveLast();
            }
        }
    }

    public UiErrorDiagnosticEntry? GetLatest(string? contains = null)
    {
        lock (_gate)
        {
            foreach (var entry in _entries)
            {
                if (string.IsNullOrWhiteSpace(contains) || Matches(entry, contains))
                {
                    return entry;
                }
            }
        }

        return null;
    }

    private static bool Matches(UiErrorDiagnosticEntry entry, string contains)
    {
        return entry.Category.Contains(contains, StringComparison.OrdinalIgnoreCase) ||
               entry.Message.Contains(contains, StringComparison.OrdinalIgnoreCase) ||
               (entry.ExceptionText?.Contains(contains, StringComparison.OrdinalIgnoreCase) ?? false);
    }
}

public sealed class UiErrorDiagnosticsLoggerProvider(UiErrorDiagnosticsStore store) : ILoggerProvider
{
    private readonly UiErrorDiagnosticsStore _store = store;

    public ILogger CreateLogger(string categoryName) =>
        new UiErrorDiagnosticsLogger(categoryName, _store);

    public void Dispose()
    {
    }

    private sealed class UiErrorDiagnosticsLogger(string categoryName, UiErrorDiagnosticsStore store) : ILogger
    {
        private readonly string _categoryName = categoryName;
        private readonly UiErrorDiagnosticsStore _store = store;

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (exception is null && logLevel < LogLevel.Error)
            {
                return;
            }

            var message = formatter(state, exception);
            if (string.IsNullOrWhiteSpace(message) && exception is null)
            {
                return;
            }

            _store.Add(new UiErrorDiagnosticEntry(
                OccurredAtUtc: DateTimeOffset.UtcNow,
                Category: _categoryName,
                Level: logLevel.ToString(),
                Message: message,
                ExceptionText: exception?.ToString()));
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose()
            {
            }
        }
    }
}

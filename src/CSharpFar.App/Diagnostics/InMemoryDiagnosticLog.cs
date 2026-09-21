namespace CSharpFar.App.Diagnostics;

internal sealed class InMemoryDiagnosticLog : IDiagnosticLog
{
    internal const int DefaultCapacity = 1000;

    private readonly object _sync = new();
    private readonly Queue<DiagnosticEntry> _entries;
    private readonly int _capacity;
    private readonly Func<DateTimeOffset> _clock;

    public InMemoryDiagnosticLog(
        int capacity = DefaultCapacity,
        Func<DateTimeOffset>? clock = null)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));

        _capacity = capacity;
        _entries = new Queue<DiagnosticEntry>(Math.Min(capacity, DefaultCapacity));
        _clock = clock ?? (() => DateTimeOffset.Now);
    }

    public bool IsEnabled => true;

    public void Write(DiagnosticCategory category, string message)
    {
        try
        {
            Add(new DiagnosticEntry(
                _clock(),
                category,
                DiagnosticSanitizer.SanitizeText(message)));
        }
        catch (Exception)
        {
        }
    }

    public void WriteException(
        DiagnosticCategory category,
        Exception exception,
        string? context = null)
    {
        if (exception is null)
            return;

        try
        {
            Add(new DiagnosticEntry(
                _clock(),
                category,
                DiagnosticSanitizer.SanitizeText(context ?? exception.GetType().Name),
                CaptureException(exception)));
        }
        catch (Exception)
        {
        }
    }

    public IReadOnlyList<DiagnosticEntry> GetSnapshot()
    {
        try
        {
            lock (_sync)
                return _entries.ToArray();
        }
        catch (Exception)
        {
            return [];
        }
    }

    public void Clear()
    {
        try
        {
            lock (_sync)
                _entries.Clear();
        }
        catch (Exception)
        {
        }
    }

    private void Add(DiagnosticEntry entry)
    {
        lock (_sync)
        {
            while (_entries.Count >= _capacity)
                _entries.Dequeue();
            _entries.Enqueue(entry);
        }
    }

    private static DiagnosticExceptionInfo CaptureException(Exception exception, int depth = 0)
    {
        if (depth >= 16)
        {
            return new DiagnosticExceptionInfo(
                exception.GetType().FullName ?? exception.GetType().Name,
                "[inner exception depth limit reached]",
                null,
                []);
        }

        IReadOnlyList<DiagnosticExceptionInfo> inner = exception.InnerException is null
            ? []
            : [CaptureException(exception.InnerException, depth + 1)];

        return new DiagnosticExceptionInfo(
            exception.GetType().FullName ?? exception.GetType().Name,
            DiagnosticSanitizer.SanitizeText(exception.Message),
            string.IsNullOrWhiteSpace(exception.StackTrace)
                ? null
                : DiagnosticSanitizer.SanitizeText(exception.StackTrace),
            inner);
    }
}

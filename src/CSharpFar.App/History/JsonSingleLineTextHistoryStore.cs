using System.Text.Json;
using CSharpFar.Ui;

namespace CSharpFar.App.History;

public sealed class JsonSingleLineTextHistoryStore : ISingleLineTextHistoryStore
{
    private const int Version = 1;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
    private readonly object _sync = new();
    private readonly string _filePath;
    private Dictionary<string, List<string>> _fields = new(StringComparer.Ordinal);
    private readonly ITextFieldHistoryDiagnostics _diagnostics;
    private readonly Action<string, string, bool> _moveFile;

    public JsonSingleLineTextHistoryStore(string configDirectory, ITextFieldHistoryDiagnostics? diagnostics = null)
        : this(configDirectory, diagnostics, File.Move)
    {
    }

    internal JsonSingleLineTextHistoryStore(string configDirectory,
        ITextFieldHistoryDiagnostics? diagnostics, Action<string, string, bool> moveFile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configDirectory);
        ArgumentNullException.ThrowIfNull(moveFile);
        _filePath = Path.GetFullPath(Path.Combine(configDirectory, "field-history.json"));
        _diagnostics = diagnostics ?? new TraceTextFieldHistoryDiagnostics();
        _moveFile = moveFile;
        LoadDocument();
    }

    public IReadOnlyList<string> Load(string fieldKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldKey);
        lock (_sync)
        {
            LoadDocument();
            return _fields.TryGetValue(fieldKey, out var values) ? values.ToArray() : [];
        }
    }

    public IReadOnlyList<string>? Promote(string fieldKey, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldKey);
        ArgumentNullException.ThrowIfNull(value);
        lock (_sync)
        {
            try
            {
                using var fileLock = AtomicHistoryFile.Acquire(_filePath);
                var fields = ReadDocument();
                if (!fields.TryGetValue(fieldKey, out var values))
                    fields[fieldKey] = values = [];

                var previous = values.ToArray();
                if (value.Length > 0)
                {
                    values.RemoveAll(item => string.Equals(item, value, StringComparison.Ordinal));
                    values.Insert(0, value);
                    if (values.Count > TextHistory.MaxItemsPerField)
                        values.RemoveRange(TextHistory.MaxItemsPerField, values.Count - TextHistory.MaxItemsPerField);
                }

                if (!previous.SequenceEqual(values, StringComparer.Ordinal))
                    AtomicHistoryFile.Write(_filePath,
                        JsonSerializer.Serialize(new Document { Version = Version, Fields = fields }, JsonOptions),
                        _moveFile);

                _fields = fields;
                return values.ToArray();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                InvalidDataException or JsonException or NotSupportedException)
            {
                ReportFailure(TextHistoryPersistenceOperation.Save, ex);
                return null;
            }
        }
    }

    private void LoadDocument()
    {
        lock (_sync)
        {
            try
            {
                _fields = ReadDocument();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                InvalidDataException or JsonException or NotSupportedException)
            {
                ReportFailure(TextHistoryPersistenceOperation.Load, ex);
            }
        }
    }

    private Dictionary<string, List<string>> ReadDocument()
    {
        var fields = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        string json;
        try
        {
            json = File.ReadAllText(_filePath);
        }
        catch (FileNotFoundException)
        {
            return fields;
        }
        catch (DirectoryNotFoundException)
        {
            return fields;
        }
        if (string.IsNullOrWhiteSpace(json))
            return fields;

        Document? document = JsonSerializer.Deserialize<Document>(json, JsonOptions);
        if (document is null || document.Version != Version || document.Fields is null)
            throw new InvalidDataException("Unsupported field history document.");

        foreach (var (key, values) in document.Fields)
        {
            if (!string.IsNullOrWhiteSpace(key))
                fields[key] = Normalize(values);
        }
        return fields;
    }

    private static List<string> Normalize(IEnumerable<string>? values)
    {
        var result = new List<string>();
        if (values is null) return result;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string value in values)
        {
            if (!string.IsNullOrEmpty(value) && seen.Add(value)) result.Add(value);
            if (result.Count == TextHistory.MaxItemsPerField) break;
        }
        return result;
    }

    private void ReportFailure(TextHistoryPersistenceOperation operation, Exception exception)
    {
        AtomicHistoryFile.Report("field-history.json", operation.ToString(), exception);
        try
        {
            _diagnostics.ReportPersistenceFailure(operation,
                new InvalidOperationException($"Field history {exception.GetType().Name}."));
        }
        catch
        {
            // Diagnostics are best effort.
        }
    }

    private sealed class Document
    {
        public int Version { get; set; }
        public Dictionary<string, List<string>>? Fields { get; set; }
    }

    private sealed class TraceTextFieldHistoryDiagnostics : ITextFieldHistoryDiagnostics
    {
        public void ReportPersistenceFailure(TextHistoryPersistenceOperation operation, Exception exception) =>
            System.Diagnostics.Trace.TraceError($"Field history {operation} failed ({exception.GetType().Name}).");
    }
}

using System.Text.Json;
using System.Text.Json.Serialization;
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
    private readonly Dictionary<string, List<string>> _fields = new(StringComparer.Ordinal);

    public JsonSingleLineTextHistoryStore(string configDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configDirectory);
        _filePath = Path.Combine(configDirectory, "field-history.json");
        LoadDocument();
    }

    public IReadOnlyList<string> Load(string fieldKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldKey);
        lock (_sync)
            return _fields.TryGetValue(fieldKey, out List<string>? items) ? items.ToArray() : [];
    }

    public void Save(string fieldKey, IReadOnlyList<string> items)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldKey);
        ArgumentNullException.ThrowIfNull(items);
        lock (_sync)
        {
            List<string> normalized = Normalize(items);
            if (_fields.TryGetValue(fieldKey, out List<string>? current) && current.SequenceEqual(normalized, StringComparer.Ordinal))
                return;
            _fields[fieldKey] = normalized;
            SaveDocument();
        }
    }

    private void LoadDocument()
    {
        lock (_sync)
        {
            try
            {
                if (!File.Exists(_filePath)) return;
                Document? document = JsonSerializer.Deserialize<Document>(File.ReadAllText(_filePath), JsonOptions);
                if (document is null || document.Version != Version || document.Fields is null) return;
                foreach ((string key, List<string>? values) in document.Fields)
                    if (!string.IsNullOrWhiteSpace(key)) _fields[key] = Normalize(values);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
            {
                _fields.Clear();
            }
        }
    }

    private void SaveDocument()
    {
        try
        {
            string directory = Path.GetDirectoryName(_filePath)!;
            Directory.CreateDirectory(directory);
            string temporaryPath = Path.Combine(directory, $".{Path.GetFileName(_filePath)}.{Guid.NewGuid():N}.tmp");
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(new Document { Version = Version, Fields = _fields }, JsonOptions));
            File.Move(temporaryPath, _filePath, overwrite: true);
        }
        catch { }
    }

    private static List<string> Normalize(IEnumerable<string>? values)
    {
        var result = new List<string>();
        if (values is null) return result;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string value in values)
        {
            if (!string.IsNullOrWhiteSpace(value) && seen.Add(value)) result.Add(value);
            if (result.Count == SingleLineTextHistoryState.MaxItemsPerField) break;
        }
        return result;
    }

    private sealed class Document
    {
        public int Version { get; set; }
        public Dictionary<string, List<string>>? Fields { get; set; }
    }
}

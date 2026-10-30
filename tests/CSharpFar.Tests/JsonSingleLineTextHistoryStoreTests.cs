using CSharpFar.App.History;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

public sealed class JsonSingleLineTextHistoryStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "CSharpFar.Tests", Guid.NewGuid().ToString("N"));
    private string HistoryPath => Path.Combine(_directory, "field-history.json");

    [Fact]
    public void Missing_file_loads_empty_history_and_promote_creates_it()
    {
        var store = new JsonSingleLineTextHistoryStore(_directory);
        Assert.Empty(store.Load("SearchDialog.Mask"));

        Assert.Equal(["*.cs"], store.Promote("SearchDialog.Mask", "*.cs"));

        Assert.True(File.Exists(HistoryPath));
        Assert.Equal(["*.cs"], new JsonSingleLineTextHistoryStore(_directory).Load("SearchDialog.Mask"));
    }

    [Fact]
    public void Fields_are_isolated_and_preserve_whitespace_values()
    {
        var store = new JsonSingleLineTextHistoryStore(_directory);
        store.Promote("A", "old");
        store.Promote("A", "new");
        store.Promote("A", "old");
        store.Promote("A", " ");
        store.Promote("B", "other");

        Assert.Equal([" ", "old", "new"], store.Load("A"));
        Assert.Equal(["other"], store.Load("B"));
    }

    [Fact]
    public void Two_instances_merge_same_key_and_different_keys()
    {
        var a = new JsonSingleLineTextHistoryStore(_directory);
        var b = new JsonSingleLineTextHistoryStore(_directory);
        a.Promote("A", "x");
        b.Promote("A", "y");
        a.Promote("B", "other");
        Assert.Equal(["x", "y"], b.Promote("A", "x"));
        Assert.Equal(["x", "y"], a.Load("A"));
        Assert.Equal(["other"], b.Load("B"));
    }

    [Fact]
    public void Registry_promotes_first_local_value_against_external_changes()
    {
        var a = new SingleLineTextHistoryRegistry(new JsonSingleLineTextHistoryStore(_directory));
        var b = new SingleLineTextHistoryRegistry(new JsonSingleLineTextHistoryStore(_directory));
        var id = new TextHistoryId("Shared");
        TextHistory first = a.Get(id);
        first.Add("x");
        b.Get(id).Add("y");
        first.Add("x");

        Assert.Equal(["x", "y"], new JsonSingleLineTextHistoryStore(_directory).Load("Shared"));
        Assert.Equal(["x", "y"], first.Items);
    }

    [Fact]
    public void Opening_history_refreshes_cached_registry_even_when_initially_empty()
    {
        var a = new SingleLineTextHistoryRegistry(new JsonSingleLineTextHistoryStore(_directory));
        var b = new SingleLineTextHistoryRegistry(new JsonSingleLineTextHistoryStore(_directory));
        var id = new TextHistoryId("Shared");
        var state = new SingleLineTextHistoryState(b.Get(id));
        Assert.False(state.History.HasItems);

        a.Get(id).Add("value");

        Assert.True(state.OpenAll(5));
        Assert.Equal("value", state.Matches[1]);
        Assert.True(state.History.HasItems);
        state.Close();
        a.Get(id).Add("newer");
        Assert.True(state.OpenAll(5));
        Assert.Equal("newer", state.Matches[1]);
    }

    [Fact]
    public void PreserveWhitespace_keeps_whitespace_only_value()
    {
        var registry = new SingleLineTextHistoryRegistry(new JsonSingleLineTextHistoryStore(_directory));
        var history = registry.Get(new TextHistoryId("A"));
        history.Add(" ");
        Assert.Empty(history.Items);
        history.AddPreservingWhitespace(" ");
        Assert.Equal([" "], history.Items);
        Assert.Equal([" "], new JsonSingleLineTextHistoryStore(_directory).Load("A"));
    }

    [Fact]
    public void Concurrent_instances_do_not_lose_values()
    {
        var stores = Enumerable.Range(0, 4).Select(_ => new JsonSingleLineTextHistoryStore(_directory)).ToArray();
        Parallel.For(0, 80, index =>
        {
            var result = stores[index % stores.Length].Promote("A", $"v{index}");
            Assert.NotNull(result);
        });

        var items = new JsonSingleLineTextHistoryStore(_directory).Load("A");
        Assert.Equal(80, items.Count);
        Assert.Equal(80, items.Distinct(StringComparer.Ordinal).Count());
        for (int i = 0; i < 80; i++)
            Assert.Contains($"v{i}", items);
    }

    [Fact]
    public void Limit_is_100()
    {
        var store = new JsonSingleLineTextHistoryStore(_directory);
        for (int i = 0; i < 102; i++)
            store.Promote("A", i.ToString());
        Assert.Equal(100, store.Load("A").Count);
        Assert.Equal("101", store.Load("A")[0]);
    }

    [Fact]
    public void Corrupt_document_is_ignored_for_read_and_not_overwritten()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(HistoryPath, "not json");
        var diagnostics = new RecordingDiagnostics();
        var store = new JsonSingleLineTextHistoryStore(_directory, diagnostics);
        Assert.Empty(store.Load("A"));

        Assert.Null(store.Promote("A", "value"));
        Assert.Equal("not json", File.ReadAllText(HistoryPath));
        Assert.Contains(diagnostics.Failures, item => item.Operation == TextHistoryPersistenceOperation.Load);
        Assert.Contains(diagnostics.Failures, item => item.Operation == TextHistoryPersistenceOperation.Save);
    }

    [Fact]
    public void Unsupported_version_is_not_overwritten()
    {
        Directory.CreateDirectory(_directory);
        const string original = "{\"version\":2,\"fields\":{}}";
        File.WriteAllText(HistoryPath, original);
        var store = new JsonSingleLineTextHistoryStore(_directory);
        Assert.Null(store.Promote("A", "value"));
        Assert.Equal(original, File.ReadAllText(HistoryPath));
    }

    [Fact]
    public void Failure_does_not_erase_other_store_or_change_confirmed_snapshot()
    {
        var a = new JsonSingleLineTextHistoryStore(_directory);
        a.Promote("A", "old");
        var diagnostics = new RecordingDiagnostics();
        var failing = new JsonSingleLineTextHistoryStore(_directory, diagnostics,
            static (_, _, _) => throw new IOException("Simulated replace failure."));
        Assert.Null(failing.Promote("A", "new"));
        Assert.Equal(["old"], failing.Load("A"));
        Assert.Equal(["old"], new JsonSingleLineTextHistoryStore(_directory).Load("A"));
        Assert.Empty(Directory.GetFiles(_directory, ".field-history.json.*.tmp"));
        Assert.Contains(diagnostics.Failures, item => item.Operation == TextHistoryPersistenceOperation.Save);
        a.Promote("B", "other");
        Assert.Equal(["other"], failing.Load("B"));
    }

    [Fact]
    public void Lock_timeout_is_diagnosed_and_not_committed()
    {
        Directory.CreateDirectory(_directory);
        using var occupied = new FileStream(HistoryPath + ".lock",
            FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var diagnostics = new RecordingDiagnostics();
        var store = new JsonSingleLineTextHistoryStore(_directory, diagnostics);
        Assert.Null(store.Promote("A", "value"));
        Assert.False(File.Exists(HistoryPath));
        Assert.Contains(diagnostics.Failures, item => item.Operation == TextHistoryPersistenceOperation.Save);
    }

    [Fact]
    public void Registry_saves_additions_and_limits_items()
    {
        var store = new InMemorySingleLineTextHistoryStore();
        var registry = new SingleLineTextHistoryRegistry(store);
        var id = new TextHistoryId("A");
        var history = registry.Get(id);
        for (int i = 0; i < 101; i++) history.Add(i.ToString());
        history.Add("50");

        Assert.Same(history, registry.Get(id));
        Assert.Equal("50", history.Items[0]);
        Assert.Equal(100, history.Items.Count);
        Assert.True(history.HasItems);
        Assert.Equal(history.Items, store.Load("A"));
    }

    [Fact]
    public void Registry_rejects_default_history_identifier()
    {
        var registry = new SingleLineTextHistoryRegistry(new InMemorySingleLineTextHistoryStore());
        Assert.Throws<ArgumentException>(() => registry.Get(default));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private sealed class RecordingDiagnostics : ITextFieldHistoryDiagnostics
    {
        public List<(TextHistoryPersistenceOperation Operation, Exception Exception)> Failures { get; } = [];
        public void ReportPersistenceFailure(TextHistoryPersistenceOperation operation, Exception exception) =>
            Failures.Add((operation, exception));
    }
}

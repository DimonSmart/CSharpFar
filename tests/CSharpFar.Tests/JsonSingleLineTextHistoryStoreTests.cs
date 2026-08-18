using CSharpFar.App.History;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

public sealed class JsonSingleLineTextHistoryStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "CSharpFar.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Missing_file_loads_empty_history_and_save_restores_it()
    {
        var store = new JsonSingleLineTextHistoryStore(_directory);
        Assert.Empty(store.Load("SearchDialog.Mask"));

        store.Save("SearchDialog.Mask", ["*.cs"]);

        Assert.True(File.Exists(Path.Combine(_directory, "field-history.json")));
        Assert.Equal(["*.cs"], new JsonSingleLineTextHistoryStore(_directory).Load("SearchDialog.Mask"));
    }

    [Fact]
    public void Fields_are_isolated_and_normalized()
    {
        var store = new JsonSingleLineTextHistoryStore(_directory);
        store.Save("A", ["new", "", "old", "new", " "]);
        store.Save("B", ["other"]);

        Assert.Equal(["new", "old"], store.Load("A"));
        Assert.Equal(["other"], store.Load("B"));
    }

    [Fact]
    public void Corrupt_document_is_ignored_without_touching_command_history()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "field-history.json"), "not json");
        File.WriteAllText(Path.Combine(_directory, "history.json"), "commands remain separate");

        var store = new JsonSingleLineTextHistoryStore(_directory);

        Assert.Empty(store.Load("A"));
        Assert.Equal("commands remain separate", File.ReadAllText(Path.Combine(_directory, "history.json")));
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
    public void Corrupt_document_reports_load_failure_without_throwing()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "field-history.json"), "not json");
        var diagnostics = new RecordingDiagnostics();

        var store = new JsonSingleLineTextHistoryStore(_directory, diagnostics);

        Assert.Empty(store.Load("A"));
        Assert.Equal(TextHistoryPersistenceOperation.Load, Assert.Single(diagnostics.Failures).Operation);
    }

    [Fact]
    public void Save_failure_is_reported_without_throwing()
    {
        string filePath = Path.Combine(_directory, "not-a-directory");
        Directory.CreateDirectory(_directory);
        File.WriteAllText(filePath, string.Empty);
        var diagnostics = new RecordingDiagnostics();
        var store = new JsonSingleLineTextHistoryStore(filePath, diagnostics);

        store.Save("A", ["value"]);

        Assert.Equal(TextHistoryPersistenceOperation.Save, Assert.Single(diagnostics.Failures).Operation);
    }

    [Fact]
    public void Move_failure_removes_temporary_file()
    {
        Directory.CreateDirectory(_directory);
        string historyPath = Path.Combine(_directory, "field-history.json");
        File.WriteAllText(historyPath, "{}");
        var diagnostics = new RecordingDiagnostics();
        var store = new JsonSingleLineTextHistoryStore(
            _directory,
            diagnostics,
            static (_, _, _) => { throw new IOException("Simulated move failure."); });

        store.Save("A", ["value"]);

        Assert.Equal(TextHistoryPersistenceOperation.Save, Assert.Single(diagnostics.Failures).Operation);
        Assert.Empty(Directory.GetFiles(_directory, ".field-history.json.*.tmp"));
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

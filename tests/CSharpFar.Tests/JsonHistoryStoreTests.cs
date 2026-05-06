using CSharpFar.App.History;
using CSharpFar.Core.Models;

namespace CSharpFar.Tests;

/// <summary>
/// Verifies Stage 10: JsonHistoryStore persists command and directory history to JSON.
/// </summary>
public class JsonHistoryStoreTests : IDisposable
{
    private readonly string _tempFile;

    public JsonHistoryStoreTests()
    {
        _tempFile = Path.Combine(Path.GetTempPath(), $"CSharpFarHistTest_{Guid.NewGuid():N}.json");
    }

    public void Dispose()
    {
        if (File.Exists(_tempFile)) File.Delete(_tempFile);
    }

    // ── Command history ───────────────────────────────────────────────────────

    [Fact]
    public void AddCommand_PersistsToDisk()
    {
        var store = new JsonHistoryStore(_tempFile);
        store.AddCommand(new CommandHistoryItem { Command = "dir", WorkingDirectory = @"C:\" });

        var store2 = new JsonHistoryStore(_tempFile);
        Assert.Single(store2.GetCommandHistory());
        Assert.Equal("dir", store2.GetCommandHistory()[0].Command);
    }

    [Fact]
    public void AddCommand_MultipleCommandsPersistInOrder()
    {
        var store = new JsonHistoryStore(_tempFile);
        store.AddCommand(new CommandHistoryItem { Command = "dir",  WorkingDirectory = @"C:\" });
        store.AddCommand(new CommandHistoryItem { Command = "cls",  WorkingDirectory = @"C:\" });
        store.AddCommand(new CommandHistoryItem { Command = "echo", WorkingDirectory = @"C:\" });

        var store2 = new JsonHistoryStore(_tempFile);
        var history = store2.GetCommandHistory();
        Assert.Equal(3, history.Count);
        Assert.Equal("dir",  history[0].Command);
        Assert.Equal("echo", history[2].Command);
    }

    // ── Directory history ─────────────────────────────────────────────────────

    [Fact]
    public void AddDirectory_PersistsToDisk()
    {
        var store = new JsonHistoryStore(_tempFile);
        store.AddDirectory(new DirectoryHistoryItem { Path = @"C:\Projects" });

        var store2 = new JsonHistoryStore(_tempFile);
        Assert.Single(store2.GetDirectoryHistory());
        Assert.Equal(@"C:\Projects", store2.GetDirectoryHistory()[0].Path);
    }

    // ── Load edge cases ───────────────────────────────────────────────────────

    [Fact]
    public void Load_StartsFreshWhenFileDoesNotExist()
    {
        var store = new JsonHistoryStore(_tempFile + ".notexist");
        Assert.Empty(store.GetCommandHistory());
        Assert.Empty(store.GetDirectoryHistory());
    }

    [Fact]
    public void Load_StartsFreshWhenFileIsCorrupt()
    {
        File.WriteAllText(_tempFile, "{ not valid json {{{{");
        var store = new JsonHistoryStore(_tempFile);
        Assert.Empty(store.GetCommandHistory());
    }
}

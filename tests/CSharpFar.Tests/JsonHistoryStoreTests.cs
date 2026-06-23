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

    [Fact]
    public void AddCommand_RepeatedCommandMovesToMostRecentInFile()
    {
        var store = new JsonHistoryStore(_tempFile);
        store.AddCommand(new CommandHistoryItem { Command = "dir", WorkingDirectory = @"C:\A" });
        store.AddCommand(new CommandHistoryItem { Command = "cls", WorkingDirectory = @"C:\B" });
        store.AddCommand(new CommandHistoryItem { Command = "dir", WorkingDirectory = @"C:\C" });

        var store2 = new JsonHistoryStore(_tempFile);
        var history = store2.GetCommandHistory();
        Assert.Equal(2, history.Count);
        Assert.Equal("cls", history[0].Command);
        Assert.Equal("dir", history[1].Command);
        Assert.Equal(@"C:\C", history[1].WorkingDirectory);
    }

    [Fact]
    public void Load_NormalizesDuplicateCommands()
    {
        File.WriteAllText(_tempFile,
            @"{ ""Commands"": [
                { ""Command"": ""dir"", ""WorkingDirectory"": ""C:\\A"" },
                { ""Command"": ""cls"", ""WorkingDirectory"": ""C:\\B"" },
                { ""Command"": ""dir"", ""WorkingDirectory"": ""C:\\C"" }
            ] }");

        var store = new JsonHistoryStore(_tempFile);
        var history = store.GetCommandHistory();
        Assert.Equal(2, history.Count);
        Assert.Equal("cls", history[0].Command);
        Assert.Equal("dir", history[1].Command);
        Assert.Equal(@"C:\C", history[1].WorkingDirectory);
    }

    [Fact]
    public void RemoveCommand_RemovesCommandFromFile()
    {
        var store = new JsonHistoryStore(_tempFile);
        store.AddCommand(new CommandHistoryItem { Command = "dir", WorkingDirectory = @"C:\" });
        store.AddCommand(new CommandHistoryItem { Command = "cls", WorkingDirectory = @"C:\" });

        bool removed = store.RemoveCommand("dir");

        Assert.True(removed);
        var store2 = new JsonHistoryStore(_tempFile);
        var item = Assert.Single(store2.GetCommandHistory());
        Assert.Equal("cls", item.Command);
    }

    [Fact]
    public void RemoveCommand_ReturnsFalseForMissingCommand()
    {
        var store = new JsonHistoryStore(_tempFile);
        store.AddCommand(new CommandHistoryItem { Command = "dir", WorkingDirectory = @"C:\" });

        bool removed = store.RemoveCommand("missing");

        Assert.False(removed);
        var store2 = new JsonHistoryStore(_tempFile);
        var item = Assert.Single(store2.GetCommandHistory());
        Assert.Equal("dir", item.Command);
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

    [Fact]
    public void AddDirectory_ConsecutiveDuplicateNotStoredInFile()
    {
        var store = new JsonHistoryStore(_tempFile);
        store.AddDirectory(new DirectoryHistoryItem { Path = @"C:\Projects" });
        store.AddDirectory(new DirectoryHistoryItem { Path = @"C:\Projects" }); // duplicate

        var store2 = new JsonHistoryStore(_tempFile);
        Assert.Single(store2.GetDirectoryHistory());
    }

    [Fact]
    public void AddDirectory_MultiplePathsPersistInOrder()
    {
        var store = new JsonHistoryStore(_tempFile);
        store.AddDirectory(new DirectoryHistoryItem { Path = @"C:\A" });
        store.AddDirectory(new DirectoryHistoryItem { Path = @"C:\B" });
        store.AddDirectory(new DirectoryHistoryItem { Path = @"C:\C" });

        var store2 = new JsonHistoryStore(_tempFile);
        var history = store2.GetDirectoryHistory();
        Assert.Equal(3, history.Count);
        Assert.Equal(@"C:\A", history[0].Path);
        Assert.Equal(@"C:\C", history[2].Path);
    }

    // ── File history ──────────────────────────────────────────────────────────

    [Fact]
    public void AddFile_PersistsToDisk()
    {
        var store = new JsonHistoryStore(_tempFile);
        store.AddFile(new FileHistoryItem { Path = @"C:\docs\readme.txt" });

        var store2 = new JsonHistoryStore(_tempFile);
        Assert.Single(store2.GetFileHistory());
        Assert.Equal(@"C:\docs\readme.txt", store2.GetFileHistory()[0].Path);
    }

    [Fact]
    public void AddFile_ConsecutiveDuplicateNotStored()
    {
        var store = new JsonHistoryStore(_tempFile);
        store.AddFile(new FileHistoryItem { Path = @"C:\docs\readme.txt" });
        store.AddFile(new FileHistoryItem { Path = @"C:\docs\readme.txt" }); // duplicate

        var store2 = new JsonHistoryStore(_tempFile);
        Assert.Single(store2.GetFileHistory());
    }

    [Fact]
    public void AddFile_MultipleFilesPersistInOrder()
    {
        var store = new JsonHistoryStore(_tempFile);
        store.AddFile(new FileHistoryItem { Path = @"C:\a.txt" });
        store.AddFile(new FileHistoryItem { Path = @"C:\b.txt" });
        store.AddFile(new FileHistoryItem { Path = @"C:\c.txt" });

        var store2 = new JsonHistoryStore(_tempFile);
        var history = store2.GetFileHistory();
        Assert.Equal(3, history.Count);
        Assert.Equal(@"C:\a.txt", history[0].Path);
        Assert.Equal(@"C:\c.txt", history[2].Path);
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
    public void Load_CorruptJsonThrows()
    {
        File.WriteAllText(_tempFile, "{ not valid json {{{{");

        var ex = Assert.Throws<InvalidDataException>(() => new JsonHistoryStore(_tempFile));

        Assert.Contains(_tempFile, ex.Message, StringComparison.Ordinal);
    }
}

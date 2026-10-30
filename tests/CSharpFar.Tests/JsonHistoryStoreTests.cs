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
        if (File.Exists(_tempFile + ".lock")) File.Delete(_tempFile + ".lock");
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
        store.AddCommand(new CommandHistoryItem { Command = "dir", WorkingDirectory = @"C:\" });
        store.AddCommand(new CommandHistoryItem { Command = "cls", WorkingDirectory = @"C:\" });
        store.AddCommand(new CommandHistoryItem { Command = "echo", WorkingDirectory = @"C:\" });

        var store2 = new JsonHistoryStore(_tempFile);
        var history = store2.GetCommandHistory();
        Assert.Equal(3, history.Count);
        Assert.Equal("dir", history[0].Command);
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
    [Fact]
    public void Two_instances_merge_command_operations_in_commit_order()
    {
        var a = new JsonHistoryStore(_tempFile);
        var b = new JsonHistoryStore(_tempFile);
        var timestamp = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        a.AddCommand(new CommandHistoryItem { Command = "dir", WorkingDirectory = "first", TimestampUtc = timestamp });
        b.AddCommand(new CommandHistoryItem { Command = "git status", WorkingDirectory = "second" });
        a.AddCommand(new CommandHistoryItem { Command = "dir", WorkingDirectory = "new", TimestampUtc = timestamp });
        b.AddCommand(new CommandHistoryItem { Command = "echo a\\nb", WorkingDirectory = "third" });

        var actual = new JsonHistoryStore(_tempFile).GetCommandHistory();
        Assert.Equal(["git status", "dir", "echo a\\nb"], actual.Select(item => item.Command));
        Assert.Equal("new", actual[1].WorkingDirectory);
        Assert.Equal(timestamp, actual[1].TimestampUtc);
        Assert.Equal(["git status", "dir", "echo a\\nb"], a.GetCommandHistory().Select(item => item.Command));
    }

    [Fact]
    public void Stale_instance_cannot_resurrect_removed_command()
    {
        var a = new JsonHistoryStore(_tempFile);
        a.AddCommand(new CommandHistoryItem { Command = "to-remove", WorkingDirectory = "/" });
        var b = new JsonHistoryStore(_tempFile);
        Assert.True(a.RemoveCommand("to-remove"));
        b.AddCommand(new CommandHistoryItem { Command = "survives", WorkingDirectory = "/" });
        Assert.False(b.RemoveCommand("to-remove"));

        Assert.Equal("survives", Assert.Single(a.GetCommandHistory()).Command);
        Assert.Equal("survives", Assert.Single(new JsonHistoryStore(_tempFile).GetCommandHistory()).Command);
    }

    [Fact]
    public void Separate_arrays_merge_and_snapshots_are_independent()
    {
        var a = new JsonHistoryStore(_tempFile);
        var b = new JsonHistoryStore(_tempFile);
        var c = new JsonHistoryStore(_tempFile);
        var old = a.GetCommandHistory();
        a.AddDirectory(new DirectoryHistoryItem { Path = "/tmp/a" });
        b.AddCommand(new CommandHistoryItem { Command = "ls", WorkingDirectory = "/" });
        c.AddFile(new FileHistoryItem { Path = "/tmp/a.txt" });
        a.AddDirectory(new DirectoryHistoryItem { Path = "/tmp/A" });
        c.AddFile(new FileHistoryItem { Path = "/tmp/A.TXT" });

        Assert.Empty(old);
        Assert.Equal(["/tmp/a"], b.GetDirectoryHistory().Select(item => item.Path));
        Assert.Equal(["ls"], c.GetCommandHistory().Select(item => item.Command));
        Assert.Equal(["/tmp/a.txt"], a.GetFileHistory().Select(item => item.Path));
    }

    [Fact]
    public void Concurrent_writers_preserve_all_operations_under_limit()
    {
        var stores = Enumerable.Range(0, 4).Select(_ => new JsonHistoryStore(_tempFile)).ToArray();
        Parallel.For(0, 80, index =>
        {
            stores[index % stores.Length].AddCommand(new CommandHistoryItem
            {
                Command = $"command-{index}",
                WorkingDirectory = "/",
            });
        });
        Assert.Equal(80, new JsonHistoryStore(_tempFile).GetCommandHistory().Count);
    }

    [Fact]
    public void Runtime_corruption_does_not_get_overwritten()
    {
        var store = new JsonHistoryStore(_tempFile);
        store.AddCommand(new CommandHistoryItem { Command = "initial", WorkingDirectory = "/" });
        File.WriteAllText(_tempFile, "{ broken");
        store.AddCommand(new CommandHistoryItem { Command = "new", WorkingDirectory = "/" });
        Assert.Equal("{ broken", File.ReadAllText(_tempFile));
        Assert.Equal("initial", Assert.Single(store.GetCommandHistory()).Command);
        Assert.False(store.RemoveCommand("initial"));
    }

    [Fact]
    public void Locked_history_does_not_report_successful_removal()
    {
        var store = new JsonHistoryStore(_tempFile);
        store.AddCommand(new CommandHistoryItem { Command = "initial", WorkingDirectory = "/" });
        using var occupied = new FileStream(_tempFile + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        Assert.False(store.RemoveCommand("initial"));
        Assert.Equal("initial", Assert.Single(store.GetCommandHistory()).Command);
    }

    [Fact]
    public void Navigator_keeps_a_stable_snapshot_until_reset()
    {
        var a = new JsonHistoryStore(_tempFile);
        var b = new JsonHistoryStore(_tempFile);
        a.AddCommand(new CommandHistoryItem { Command = "old", WorkingDirectory = "/" });
        a.AddCommand(new CommandHistoryItem { Command = "new", WorkingDirectory = "/" });
        var nav = new CSharpFar.App.CommandLine.CommandHistoryNavigator(b);
        var line = new CommandLineState();

        nav.Browse(line, -1, CSharpFar.App.CommandLine.CommandHistoryNavigationStart.Newest);
        Assert.Equal("new", line.Text);
        a.AddCommand(new CommandHistoryItem { Command = "newest", WorkingDirectory = "/" });
        nav.Browse(line, -1, CSharpFar.App.CommandLine.CommandHistoryNavigationStart.Newest);
        Assert.Equal("old", line.Text);
        nav.Reset();
        nav.Browse(line, -1, CSharpFar.App.CommandLine.CommandHistoryNavigationStart.Newest);
        Assert.Equal("newest", line.Text);
    }

}

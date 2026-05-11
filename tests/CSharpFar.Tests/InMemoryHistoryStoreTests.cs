using CSharpFar.Core.History;
using CSharpFar.Core.Models;

namespace CSharpFar.Tests;

public class InMemoryHistoryStoreTests
{
    [Fact]
    public void AddCommand_AppearsInHistory()
    {
        var store = new InMemoryHistoryStore();
        store.AddCommand(new CommandHistoryItem { Command = "dir", WorkingDirectory = @"C:\" });

        var history = store.GetCommandHistory();
        Assert.Single(history);
        Assert.Equal("dir", history[0].Command);
    }

    [Fact]
    public void AddCommand_TrimsToMaxItems()
    {
        var store = new InMemoryHistoryStore(maxCommands: 3);
        store.AddCommand(new CommandHistoryItem { Command = "cmd1", WorkingDirectory = @"C:\" });
        store.AddCommand(new CommandHistoryItem { Command = "cmd2", WorkingDirectory = @"C:\" });
        store.AddCommand(new CommandHistoryItem { Command = "cmd3", WorkingDirectory = @"C:\" });
        store.AddCommand(new CommandHistoryItem { Command = "cmd4", WorkingDirectory = @"C:\" });

        var history = store.GetCommandHistory();
        Assert.Equal(3, history.Count);
        Assert.Equal("cmd2", history[0].Command); // oldest trimmed
        Assert.Equal("cmd4", history[2].Command);
    }

    [Fact]
    public void AddCommand_RepeatedCommandMovesToMostRecent()
    {
        var store = new InMemoryHistoryStore();
        store.AddCommand(new CommandHistoryItem { Command = "dir", WorkingDirectory = @"C:\A" });
        store.AddCommand(new CommandHistoryItem { Command = "cls", WorkingDirectory = @"C:\B" });
        store.AddCommand(new CommandHistoryItem { Command = "dir", WorkingDirectory = @"C:\C" });

        var history = store.GetCommandHistory();
        Assert.Equal(2, history.Count);
        Assert.Equal("cls", history[0].Command);
        Assert.Equal("dir", history[1].Command);
        Assert.Equal(@"C:\C", history[1].WorkingDirectory);
    }

    [Fact]
    public void AddCommand_IgnoresWhitespaceOnlyCommand()
    {
        var store = new InMemoryHistoryStore();
        store.AddCommand(new CommandHistoryItem { Command = "   ", WorkingDirectory = @"C:\" });

        Assert.Empty(store.GetCommandHistory());
    }

    [Fact]
    public void AddDirectory_AppearsInHistory()
    {
        var store = new InMemoryHistoryStore();
        store.AddDirectory(new DirectoryHistoryItem { Path = @"C:\Projects" });

        var history = store.GetDirectoryHistory();
        Assert.Single(history);
        Assert.Equal(@"C:\Projects", history[0].Path);
    }

    [Fact]
    public void AddDirectory_IgnoresConsecutiveDuplicates()
    {
        var store = new InMemoryHistoryStore();
        store.AddDirectory(new DirectoryHistoryItem { Path = @"C:\Projects" });
        store.AddDirectory(new DirectoryHistoryItem { Path = @"C:\Projects" });
        store.AddDirectory(new DirectoryHistoryItem { Path = @"C:\Projects" });

        Assert.Single(store.GetDirectoryHistory());
    }

    [Fact]
    public void AddDirectory_AllowsNonConsecutiveDuplicates()
    {
        var store = new InMemoryHistoryStore();
        store.AddDirectory(new DirectoryHistoryItem { Path = @"C:\A" });
        store.AddDirectory(new DirectoryHistoryItem { Path = @"C:\B" });
        store.AddDirectory(new DirectoryHistoryItem { Path = @"C:\A" }); // non-consecutive

        Assert.Equal(3, store.GetDirectoryHistory().Count);
    }

    [Fact]
    public void AddDirectory_TrimsToMaxItems()
    {
        var store = new InMemoryHistoryStore(maxDirectories: 2);
        store.AddDirectory(new DirectoryHistoryItem { Path = @"C:\A" });
        store.AddDirectory(new DirectoryHistoryItem { Path = @"C:\B" });
        store.AddDirectory(new DirectoryHistoryItem { Path = @"C:\C" });

        var history = store.GetDirectoryHistory();
        Assert.Equal(2, history.Count);
        Assert.Equal(@"C:\B", history[0].Path);
    }
}

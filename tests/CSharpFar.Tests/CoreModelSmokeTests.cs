using CSharpFar.Core.Models;

namespace CSharpFar.Tests;

public class CoreModelSmokeTests
{
    [Fact]
    public void FilePanelItem_CanBeConstructed()
    {
        var item = new FilePanelItem
        {
            Name = "test.txt",
            FullPath = @"C:\test.txt",
            IsDirectory = false,
            Size = 42,
            LastWriteTime = DateTime.UtcNow,
            Attributes = FileAttributes.Normal,
        };

        Assert.Equal("test.txt", item.Name);
        Assert.False(item.IsDirectory);
        Assert.Equal(42, item.Size);
    }

    [Fact]
    public void FilePanelState_DefaultsAreCorrect()
    {
        var state = new FilePanelState { CurrentDirectory = @"C:\" };

        Assert.Equal(@"C:\", state.CurrentDirectory);
        Assert.Empty(state.Items);
        Assert.Empty(state.SelectedPaths);
        Assert.Equal(0, state.CursorIndex);
        Assert.Equal(SortMode.Name, state.SortMode);
    }

    [Fact]
    public void CommandHistoryItem_TimestampIsSet()
    {
        var before = DateTime.UtcNow;
        var item = new CommandHistoryItem
        {
            Command = "dir",
            WorkingDirectory = @"C:\",
        };
        var after = DateTime.UtcNow;

        Assert.InRange(item.TimestampUtc, before, after);
    }
}

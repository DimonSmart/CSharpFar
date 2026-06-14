using CSharpFar.App.DirectoryShortcuts;
using CSharpFar.Core.Models;

namespace CSharpFar.Tests;

public sealed class DirectoryShortcutNormalizerTests
{
    [Fact]
    public void Normalize_UsesDisplayOrderAndLastValidItemPerNumber()
    {
        var settings = new AppSettings.DirectoryShortcutSettings
        {
            Items =
            [
                Item(0, "Temp", @"C:\Temp"),
                Item(2, "Old", @"C:\Old"),
                Item(12, "Bad", @"C:\Bad"),
                Item(1, "Project", @"C:\Project"),
                Item(2, "SourceFiles", @"D:\src"),
            ],
        };

        var items = DirectoryShortcutNormalizer.Normalize(settings);

        Assert.Equal([1, 2, 0], items.Select(item => item.Number));
        Assert.Equal("SourceFi", items[1].Name);
        Assert.Equal(@"D:\src", items[1].Path);
    }

    [Fact]
    public void Normalize_EmptyPathClearsEarlierDuplicate()
    {
        var settings = new AppSettings.DirectoryShortcutSettings
        {
            Items =
            [
                Item(3, "Configured", @"C:\Configured"),
                Item(3, "Cleared", "  "),
            ],
        };

        Assert.Empty(DirectoryShortcutNormalizer.Normalize(settings));
    }

    [Fact]
    public void Normalize_NullSettings_ReturnsEmptyList()
    {
        Assert.Empty(DirectoryShortcutNormalizer.Normalize(null));
    }

    [Fact]
    public void NormalizeName_AllowsEmptyNameAndFitsEightCharacters()
    {
        Assert.Equal(string.Empty, DirectoryShortcutNormalizer.NormalizeName("  "));
        Assert.Equal("CSharpFa", DirectoryShortcutNormalizer.NormalizeName(" CSharpFar "));
    }

    [Fact]
    public void GetDefaultNameFromPath_UsesLastDirectoryName()
    {
        Assert.Equal("CSharpFa", DirectoryShortcutNormalizer.GetDefaultNameFromPath(@"C:\Projects\CSharpFar"));
        Assert.Equal("CSharpFa", DirectoryShortcutNormalizer.GetDefaultNameFromPath("/home/user/CSharpFar"));
    }

    private static AppSettings.DirectoryShortcutItem Item(int number, string name, string path) =>
        new()
        {
            Number = number,
            Name = name,
            Path = path,
        };
}

using CSharpFar.App.Dialogs;
using CSharpFar.App.UserMenu;
using CSharpFar.Core.Models;

namespace CSharpFar.Tests;

public sealed class UserMenuPlatformTests : IDisposable
{
    private readonly string _tempDir;

    public UserMenuPlatformTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"CSharpFarUserMenuPlatform_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Theory]
    [InlineData(PlatformKind.Windows)]
    [InlineData(PlatformKind.MacOs)]
    [InlineData(PlatformKind.Linux)]
    public void CommonItemIsAvailableOnEveryPlatform(PlatformKind platform)
    {
        var item = new UserMenuItem { Title = "Common", Command = "echo common" };

        Assert.True(UserMenuAvailability.IsAvailableOn(item, platform));
    }

    [Theory]
    [InlineData(PlatformKind.Windows, PlatformKind.Windows, true)]
    [InlineData(PlatformKind.Windows, PlatformKind.MacOs, false)]
    [InlineData(PlatformKind.Windows, PlatformKind.Linux, false)]
    [InlineData(PlatformKind.MacOs, PlatformKind.Windows, false)]
    [InlineData(PlatformKind.MacOs, PlatformKind.MacOs, true)]
    [InlineData(PlatformKind.MacOs, PlatformKind.Linux, false)]
    [InlineData(PlatformKind.Linux, PlatformKind.Windows, false)]
    [InlineData(PlatformKind.Linux, PlatformKind.MacOs, false)]
    [InlineData(PlatformKind.Linux, PlatformKind.Linux, true)]
    public void PlatformSpecificItemIsAvailableOnlyOnMatchingPlatform(
        PlatformKind itemPlatform,
        PlatformKind currentPlatform,
        bool expected)
    {
        var item = new UserMenuItem
        {
            Title = "Specific",
            Command = "command",
            Platform = itemPlatform,
        };

        Assert.Equal(expected, UserMenuAvailability.IsAvailableOn(item, currentPlatform));
    }

    [Fact]
    public void FilteringPreservesOrderAndDoesNotChangeSourceCollection()
    {
        var allFirst = new UserMenuItem { Title = "All first", Command = "one" };
        var linux = new UserMenuItem { Title = "Linux", Command = "two", Platform = PlatformKind.Linux };
        var windows = new UserMenuItem { Title = "Windows", Command = "three", Platform = PlatformKind.Windows };
        var allLast = new UserMenuItem { Title = "All last", Command = "four" };
        var items = new List<UserMenuItem> { allFirst, linux, windows, allLast };
        UserMenuItem[] original = items.ToArray();

        UserMenuItem[] filtered = UserMenuAvailability.FilterForPlatform(items, PlatformKind.Windows);

        Assert.Equal(["All first", "Windows", "All last"], filtered.Select(item => item.Title));
        Assert.Equal(original, items);
    }

    [Fact]
    public void OldJsonWithoutPlatformLoadsAsAllPlatforms()
    {
        File.WriteAllText(
            Path.Combine(_tempDir, "user-menu.json"),
            "[{\"title\":\"Legacy\",\"command\":\"echo legacy\"}]");

        var store = new UserMenuStore(_tempDir);

        Assert.Single(store.Items);
        Assert.Null(store.Items[0].Platform);
    }

    [Fact]
    public void SaveLoadPreservesAllPlatformsWithoutRuntimeFiltering()
    {
        var store = new UserMenuStore(_tempDir);
        UserMenuItem[] items =
        [
            new() { Title = "All", Command = "all" },
            new() { Title = "Windows", Command = "win", Platform = PlatformKind.Windows },
            new() { Title = "macOS", Command = "mac", Platform = PlatformKind.MacOs },
            new() { Title = "Linux", Command = "linux", Platform = PlatformKind.Linux },
        ];
        store.RuntimePlatform = PlatformKind.Windows;

        store.Save(items);
        var reloaded = new UserMenuStore(_tempDir);

        Assert.Equal(4, reloaded.Items.Count);
        Assert.Equal(
            new PlatformKind?[] { null, PlatformKind.Windows, PlatformKind.MacOs, PlatformKind.Linux },
            reloaded.Items.Select(item => item.Platform));

        string json = File.ReadAllText(Path.Combine(_tempDir, "user-menu.json"));
        Assert.Contains("\"platform\": \"windows\"", json, StringComparison.Ordinal);
        Assert.Contains("\"platform\": \"macOs\"", json, StringComparison.Ordinal);
        Assert.Contains("\"platform\": \"linux\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void DefaultsContainFileManagerEntryForEverySupportedPlatform()
    {
        var store = new UserMenuStore(_tempDir);

        Assert.Contains(store.Items, item =>
            item.Title == "Open Explorer here" &&
            item.Command == "explorer \"{panelDir}\"" &&
            item.Platform == PlatformKind.Windows);
        Assert.Contains(store.Items, item =>
            item.Title == "Open Finder here" &&
            item.Command == "open \"{panelDir}\"" &&
            item.Platform == PlatformKind.MacOs);
        Assert.Contains(store.Items, item =>
            item.Title == "Open in file manager here" &&
            item.Command == "xdg-open \"{panelDir}\"" &&
            item.Platform == PlatformKind.Linux);
    }

    [Theory]
    [InlineData(PlatformKind.Windows, "Open Explorer here")]
    [InlineData(PlatformKind.MacOs, "Open Finder here")]
    [InlineData(PlatformKind.Linux, "Open in file manager here")]
    public void RuntimeFilteringShowsOnlyMatchingDefaultFileManagerEntry(
        PlatformKind platform,
        string expectedTitle)
    {
        var store = new UserMenuStore(_tempDir);

        UserMenuItem[] filtered = UserMenuAvailability.FilterForPlatform(store.Items, platform);

        UserMenuItem item = Assert.Single(filtered);
        Assert.Equal(expectedTitle, item.Title);
    }

    [Fact]
    public void EditorCloneKeepsPlatformAndPlatformOnlyChangeIsDetected()
    {
        var original = new UserMenuItem
        {
            Title = "Open",
            Command = "open",
            Platform = PlatformKind.MacOs,
        };

        UserMenuItem clone = Assert.Single(UserMenuEditorDialog.CloneItems([original]));
        var changedPlatform = new UserMenuItem
        {
            Title = original.Title,
            Command = original.Command,
            Platform = PlatformKind.Linux,
        };

        Assert.Equal(PlatformKind.MacOs, clone.Platform);
        Assert.True(UserMenuEditorDialog.SameItem(original, clone));
        Assert.False(UserMenuEditorDialog.SameItem(original, changedPlatform));
    }
}

using CSharpFar.App.UserMenu;
using CSharpFar.Core.Models;

namespace CSharpFar.Tests;

/// <summary>
/// Verifies UserMenuStore defaults, loading, safe persistence, and runtime snapshot semantics.
/// </summary>
public class UserMenuStoreTests : IDisposable
{
    private readonly string _tempDir;

    public UserMenuStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"CSharpFarMenuTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void CreatesDefaultMenuWhenFileDoesNotExist()
    {
        var store = new UserMenuStore(_tempDir);

        Assert.NotEmpty(store.Items);
        Assert.True(File.Exists(Path.Combine(_tempDir, "user-menu.json")));
    }

    [Fact]
    public void LoadsExistingMenu()
    {
        string json = """
            [
              { "title": "Run tests", "command": "dotnet test" },
              { "title": "Build",     "command": "dotnet build" }
            ]
            """;
        File.WriteAllText(Path.Combine(_tempDir, "user-menu.json"), json);

        var store = new UserMenuStore(_tempDir);

        Assert.Equal(2, store.Items.Count);
        Assert.Equal("Run tests", store.Items[0].Title);
        Assert.Equal("dotnet test", store.Items[0].Command);
    }

    [Fact]
    public void SavePersistsOrderedCamelCaseJsonAndUpdatesRuntimeItems()
    {
        var store = new UserMenuStore(_tempDir);
        UserMenuItem[] items =
        [
            new() { Title = "Second", Command = "echo second" },
            new() { Title = "First", Command = " echo first " },
        ];

        store.Save(items);

        Assert.Equal(["Second", "First"], store.Items.Select(item => item.Title));
        Assert.Equal(" echo first ", store.Items[1].Command);

        string json = File.ReadAllText(Path.Combine(_tempDir, "user-menu.json"));
        Assert.Contains("\"title\"", json, StringComparison.Ordinal);
        Assert.Contains("\"command\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Title\"", json, StringComparison.Ordinal);

        var reloaded = new UserMenuStore(_tempDir);
        Assert.Equal(["Second", "First"], reloaded.Items.Select(item => item.Title));
        Assert.Equal(" echo first ", reloaded.Items[1].Command);
    }

    [Fact]
    public void SaveKeepsIndependentSnapshotOfCallerCollection()
    {
        var store = new UserMenuStore(_tempDir);
        var items = new List<UserMenuItem>
        {
            new() { Title = "Original", Command = "one" },
        };

        store.Save(items);
        items[0] = new UserMenuItem { Title = "Changed", Command = "two" };
        items.Add(new UserMenuItem { Title = "Extra", Command = "three" });

        Assert.Single(store.Items);
        Assert.Equal("Original", store.Items[0].Title);
        Assert.Equal("one", store.Items[0].Command);
    }

    [Fact]
    public void SaveSupportsEmptyMenu()
    {
        var store = new UserMenuStore(_tempDir);

        store.Save([]);

        Assert.Empty(store.Items);
        Assert.Equal("[]", File.ReadAllText(Path.Combine(_tempDir, "user-menu.json")).Trim());
        Assert.Empty(new UserMenuStore(_tempDir).Items);
    }

    [Fact]
    public void FailedSavePreservesRuntimeItemsAndExistingJson()
    {
        string filePath = Path.Combine(_tempDir, "user-menu.json");
        const string originalJson = "[{\"title\":\"Original\",\"command\":\"echo original\"}]";
        File.WriteAllText(filePath, originalJson);
        var store = new UserMenuStore(_tempDir);
        UserMenuItem[] replacement = [new() { Title = "Replacement", Command = "echo replacement" }];

        if (OperatingSystem.IsWindows())
        {
            using var lockStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            Assert.ThrowsAny<IOException>(() => store.Save(replacement));
        }
        else
        {
            UnixFileMode originalMode = File.GetUnixFileMode(_tempDir);
            try
            {
                File.SetUnixFileMode(_tempDir, UnixFileMode.UserRead | UnixFileMode.UserExecute);
                Assert.ThrowsAny<Exception>(() => store.Save(replacement));
            }
            finally
            {
                File.SetUnixFileMode(_tempDir, originalMode);
            }
        }

        Assert.Single(store.Items);
        Assert.Equal("Original", store.Items[0].Title);
        Assert.Equal(originalJson, File.ReadAllText(filePath));
    }

    [Fact]
    public void CorruptJsonThrows()
    {
        string filePath = Path.Combine(_tempDir, "user-menu.json");
        File.WriteAllText(filePath, "{ not valid !!!}}}");

        var ex = Assert.Throws<InvalidDataException>(() => new UserMenuStore(_tempDir));

        Assert.Contains(filePath, ex.Message, StringComparison.Ordinal);
    }
}

using CSharpFar.App.UserMenu;

namespace CSharpFar.Tests;

/// <summary>
/// Verifies Stage 18: UserMenuStore creates defaults and loads user-menu.json.
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
    public void CorruptJsonReturnsEmptyList()
    {
        File.WriteAllText(Path.Combine(_tempDir, "user-menu.json"), "{ not valid !!!}}}");

        var store = new UserMenuStore(_tempDir);

        Assert.Empty(store.Items);
    }
}

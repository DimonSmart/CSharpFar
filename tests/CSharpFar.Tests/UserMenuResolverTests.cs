using CSharpFar.App.UserMenu;
using CSharpFar.Core.Models;

namespace CSharpFar.Tests;

public sealed class UserMenuResolverTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "csharpfar-menu-resolver-" + Guid.NewGuid().ToString("N"));

    public UserMenuResolverTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void MergesClosestFirst_PreservesAllCommandsIncludingLegacyIdCollisions()
    {
        string project = Path.Combine(_root, "Workspace", "Project");
        string src = Path.Combine(project, "src");
        Directory.CreateDirectory(src);
        WriteMenu(Path.Combine(_root, "Workspace"), """
            [
              {"id":"build","title":"Shared build","command":"dotnet build"},
              {"id":"lint","title":"Lint","command":"dotnet format"},
              {"title":"Files","command":"ls"}
            ]
            """);
        WriteMenu(project, """
            [
              {"id":"build","title":"Project build","command":"dotnet build X"},
              {"title":"Files","command":"ls -la"}
            ]
            """);
        WriteMenu(src, "[]");

        var global = Global(
            new() { Title = "Global build" },
            new() { Title = "Global command" });

        var resolved = UserMenuResolver.Resolve(global, PlatformKind.Linux, src);

        Assert.True(resolved.HasLocalMenus);
        Assert.Equal(["Project", "Workspace", "Global"], resolved.Sections.Select(x => x.Label));
        Assert.Equal(
            ["Project build", "Files", "Shared build", "Lint", "Files", "Global build", "Global command"],
            resolved.Sections.SelectMany(x => x.Items).Select(x => x.Item.Title));
        Assert.Equal(project, resolved.Sections[0].Items[0].MenuDirectory);
        Assert.Equal(Path.Combine(_root, "Workspace"), resolved.Sections[1].Items[0].MenuDirectory);
        Assert.Equal(Path.Combine(_root, "global"), resolved.Sections[2].Items[0].MenuDirectory);
    }

    [Fact]
    public void PlatformFilteringKeepsDistinctCommandsWithLegacyIds()
    {
        string src = Path.Combine(_root, "A");
        WriteMenu(src, """
            [
              {"id":"build","title":"Windows build","platform":"windows"},
              {"id":"build","title":"Linux build","platform":"linux"}
            ]
            """);
        var global = Global(
            new() { Title = "Fallback" },
            new() { Title = "Other" });

        var mac = UserMenuResolver.Resolve(global, PlatformKind.MacOs, src);
        Assert.Equal(["Fallback", "Other"], mac.Sections.SelectMany(x => x.Items).Select(x => x.Item.Title));

        var linux = UserMenuResolver.Resolve(global, PlatformKind.Linux, src);
        Assert.Equal(["Linux build", "Fallback", "Other"], linux.Sections.SelectMany(x => x.Items).Select(x => x.Item.Title));
    }

    [Fact]
    public void InvalidLocalFileBlocksPartialMenuButExplicitGlobalModeSkipsIt()
    {
        string src = Path.Combine(_root, "A");
        WriteMenu(src, """
            [{"title":" "}, {"title":"Two"}]
            """);
        var global = Global(new UserMenuItem { Title = "Global" });

        var error = Assert.Throws<InvalidDataException>(() =>
            UserMenuResolver.Resolve(global, PlatformKind.Windows, src));
        Assert.Contains(Path.Combine(src, UserMenuResolver.LocalFileName), error.Message, StringComparison.Ordinal);

        var globalOnly = UserMenuResolver.Resolve(global, PlatformKind.Windows, null);
        Assert.False(globalOnly.HasLocalMenus);
        Assert.Equal("Global", Assert.Single(Assert.Single(globalOnly.Sections).Items).Item.Title);
    }

    [Fact]
    public void InvalidJsonAndInvalidTitleReportExactFileAndDoNotDamageExistingContent()
    {
        string src = Path.Combine(_root, "A");
        WriteMenu(src, "{\"items\":[]}");
        var global = Global(new UserMenuItem { Title = "Global" });
        var error = Assert.Throws<InvalidDataException>(() =>
            UserMenuResolver.Resolve(global, PlatformKind.Windows, src));
        Assert.Contains(Path.Combine(src, UserMenuResolver.LocalFileName), error.Message, StringComparison.Ordinal);

        string path = Path.Combine(src, UserMenuResolver.LocalFileName);
        string original = File.ReadAllText(path);
        var invalid = new UserMenuItem[] { new() { Title = " " } };
        Assert.Throws<InvalidDataException>(() => UserMenuFile.WriteSafely(path, invalid));
        Assert.Equal(original, File.ReadAllText(path));
    }

    [Fact]
    public void NoLocalMenuKeepsGlobalOnly()
    {
        string src = Path.Combine(_root, "NoLocal");
        Directory.CreateDirectory(src);
        var global = Global(new UserMenuItem { Title = "Run" });

        var resolved = UserMenuResolver.Resolve(global, PlatformKind.Windows, src);
        Assert.False(resolved.HasLocalMenus);
        Assert.Equal("Global", Assert.Single(resolved.Sections).Label);
        Assert.Equal("Run", Assert.Single(new UserMenuStore(Path.Combine(_root, "global")).Items).Title);
    }


    [Fact]
    public void DuplicateLegacyIdsWithinOneFileRemainDistinct()
    {
        string src = Path.Combine(_root, "DuplicateIds");
        WriteMenu(src, """
            [
              {"id":"build","title":"First build","command":"dotnet build First.csproj"},
              {"id":"build","title":"Second build","command":"dotnet build Second.csproj"}
            ]
            """);
        var global = Global(new() { Title = "Global build" });

        var resolved = UserMenuResolver.Resolve(global, PlatformKind.Windows, src);

        Assert.Equal(
            ["First build", "Second build", "Global build"],
            resolved.Sections.SelectMany(x => x.Items).Select(x => x.Item.Title));
    }

    private UserMenuStore Global(params UserMenuItem[] items)
    {
        string directory = Path.Combine(_root, "global");
        var store = new UserMenuStore(directory);
        store.Save(items);
        return store;
    }

    private static void WriteMenu(string directory, string contents)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, UserMenuResolver.LocalFileName), contents);
    }
}

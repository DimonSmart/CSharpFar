using CSharpFar.App.Editor;
using CSharpFar.Core.Models;
using TextMateSharp.Grammars;

namespace CSharpFar.Tests;

public sealed class EditorSyntaxCatalogTests
{
    [Fact]
    public void LanguageCatalog_UsesBundledTextMateLanguagesAndResolvesCanonicalValues()
    {
        var catalog = new TextMateEditorSyntaxCatalog(new AppSettings.EditorSettings());

        foreach (string id in new[] { "csharp", "json", "markdown" })
        {
            var entry = Assert.Single(
                catalog.Languages,
                candidate => string.Equals(candidate.Language.Id, id, StringComparison.OrdinalIgnoreCase));
            Assert.False(string.IsNullOrWhiteSpace(entry.Language.Id));
            Assert.False(string.IsNullOrWhiteSpace(entry.Language.DisplayName));
            Assert.False(string.IsNullOrWhiteSpace(entry.Language.ScopeName));
        }

        var csharp = Assert.Single(
            catalog.Languages,
            candidate => string.Equals(candidate.Language.Id, "csharp", StringComparison.OrdinalIgnoreCase));
        string alias = Assert.Single(csharp.Aliases.Take(1));

        Assert.Equal("csharp", catalog.ResolveLanguage(alias)?.Id);
        Assert.Equal("csharp", catalog.ResolveLanguage(csharp.Language.ScopeName)?.Id);

        var custom = catalog.ResolveLanguage("source.custom-language");
        Assert.NotNull(custom);
        Assert.Equal("source.custom-language", custom.Id);
        Assert.Equal("source.custom-language", custom.ScopeName);

        Assert.Contains(csharp.Language.DisplayName, csharp.SearchText, StringComparison.Ordinal);
        Assert.Contains(csharp.Language.Id, csharp.SearchText, StringComparison.Ordinal);
        Assert.Contains(csharp.Language.ScopeName, csharp.SearchText, StringComparison.Ordinal);
        Assert.Contains(alias, csharp.SearchText, StringComparison.Ordinal);
    }

    [Fact]
    public void ThemeCatalog_ContainsEveryBundledTheme()
    {
        var catalog = new TextMateEditorSyntaxCatalog(new AppSettings.EditorSettings());

        Assert.Equal(Enum.GetValues<ThemeName>().Length, catalog.Themes.Count);
        Assert.Contains(catalog.Themes, theme => theme.Name == "Dark+");
        Assert.Contains(catalog.Themes, theme => theme.Name == "Solarized Dark");
        Assert.Contains(catalog.Themes, theme => theme.Name == "Visual Studio Light");
    }

    [Theory]
    [InlineData("DarkPlus")]
    [InlineData("Dark+")]
    [InlineData("dark plus")]
    [InlineData("MissingTheme")]
    public void ThemeCatalog_UsesExistingThemeNormalizationAndFallback(string requestedTheme)
    {
        var catalog = new TextMateEditorSyntaxCatalog(new AppSettings.EditorSettings());

        Assert.Equal("Dark+", catalog.ResolveTheme(requestedTheme).Name);
    }
}

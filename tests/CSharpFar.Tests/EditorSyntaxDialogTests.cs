using CSharpFar.App.Editor;
using CSharpFar.Console;
using CSharpFar.Tests.Fakes;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

public sealed class EditorSyntaxDialogTests
{
    [Fact]
    public void ShowLanguage_AutoIsInitiallySelected()
    {
        var driver = Driver(Key(ConsoleKey.Enter));

        Assert.Equal("auto", Dialog(driver).ShowLanguage("auto"));
    }

    [Fact]
    public void ShowLanguage_KnownLanguagesAreSortedByDisplayName()
    {
        var driver = Driver(Key(ConsoleKey.DownArrow), Key(ConsoleKey.Enter));

        Assert.Equal("shellscript", Dialog(driver).ShowLanguage("auto"));
    }

    [Theory]
    [InlineData("csharp")]
    [InlineData("CSharp")]
    [InlineData("source.cs")]
    public void ShowLanguage_KnownIdAliasAndScopeReturnCanonicalId(string currentLanguage)
    {
        var driver = Driver(Key(ConsoleKey.Enter));

        Assert.Equal("csharp", Dialog(driver).ShowLanguage(currentLanguage));
    }

    [Fact]
    public void ShowLanguage_UnknownLegacyValueFallsBackVisuallyToAuto()
    {
        var driver = Driver(Key(ConsoleKey.Enter));

        Assert.Equal("auto", Dialog(driver).ShowLanguage("legacy-missing-language"));
    }

    [Fact]
    public void ShowLanguage_UnknownCustomScopeSelectsCustomAndPrefillsInput()
    {
        var driver = Driver(Key(ConsoleKey.Enter), Key(ConsoleKey.Enter));

        Assert.Equal("source.custom", Dialog(driver).ShowLanguage("source.custom"));
    }

    [Theory]
    [InlineData(ConsoleKey.Escape)]
    [InlineData(ConsoleKey.F10)]
    public void ShowLanguage_CancellingCustomInputReturnsToLanguageList(ConsoleKey cancelKey)
    {
        var driver = Driver(
            Key(ConsoleKey.Enter),
            Key(cancelKey),
            Key(ConsoleKey.UpArrow),
            Key(ConsoleKey.Enter));

        Assert.Equal("javascript", Dialog(driver).ShowLanguage("source.custom"));
    }

    [Theory]
    [InlineData(ConsoleKey.Escape)]
    [InlineData(ConsoleKey.F10)]
    public void ShowLanguage_CancellingMainListReturnsNull(ConsoleKey cancelKey)
    {
        var driver = Driver(Key(cancelKey));

        Assert.Null(Dialog(driver).ShowLanguage("csharp"));
    }

    [Theory]
    [InlineData("source.test", true)]
    [InlineData("text.test", true)]
    [InlineData(" source.test ", true)]
    [InlineData("something.test", false)]
    [InlineData("source", false)]
    [InlineData("text", false)]
    [InlineData("   ", false)]
    public void ValidateCustomScope_AcceptsOnlyTextMateScopePrefixes(string scope, bool valid)
    {
        Assert.Equal(valid, EditorSyntaxDialog.ValidateCustomScope(scope) is null);
    }

    [Fact]
    public void ShowTheme_UnknownThemeSelectsExistingFallbackWithoutInventingItem()
    {
        var driver = Driver(Key(ConsoleKey.Enter));

        Assert.Equal("Dark+", Dialog(driver).ShowTheme("MissingTheme"));
    }

    [Fact]
    public void ShowTheme_CurrentThemeIsInitiallySelectedAndReturnedCanonically()
    {
        var driver = Driver(Key(ConsoleKey.Enter));

        Assert.Equal("Visual Studio Light", Dialog(driver).ShowTheme("Visual Studio Light"));
    }

    [Theory]
    [InlineData(ConsoleKey.Escape)]
    [InlineData(ConsoleKey.F10)]
    public void ShowTheme_CancellingReturnsNull(ConsoleKey cancelKey)
    {
        var driver = Driver(Key(cancelKey));

        Assert.Null(Dialog(driver).ShowTheme("Dark+"));
    }

    private static EditorSyntaxDialog Dialog(FakeConsoleDriver driver)
    {
        var fields = new FormFieldFactory(
            new SingleLineTextHistoryRegistry(new InMemorySingleLineTextHistoryStore()));
        return new EditorSyntaxDialog(
            new DialogService(ModalTestHost.Create(driver), fields),
            new FakeSyntaxCatalog());
    }

    private static FakeConsoleDriver Driver(params ConsoleKeyInfo[] keys)
    {
        var driver = new FakeConsoleDriver(width: 80, height: 25);
        foreach (var key in keys)
            driver.EnqueueKey(key);
        return driver;
    }

    private static ConsoleKeyInfo Key(ConsoleKey key) =>
        new('\0', key, shift: false, alt: false, control: false);

    private sealed class FakeSyntaxCatalog : IEditorSyntaxCatalog
    {
        public FakeSyntaxCatalog()
        {
            Languages =
            [
                Entry("javascript", "source.js", "JavaScript", "JS"),
                Entry("csharp", "source.cs", "C#", "CSharp"),
                Entry("shellscript", "source.shell", "Bash", "Shell"),
            ];
            Themes =
            [
                new EditorSyntaxTheme("Visual Studio Light"),
                new EditorSyntaxTheme("Dark+"),
                new EditorSyntaxTheme("Atom One Dark"),
            ];
        }

        public IReadOnlyList<EditorSyntaxLanguageCatalogEntry> Languages { get; }

        public IReadOnlyList<EditorSyntaxTheme> Themes { get; }

        public EditorSyntaxLanguage? ResolveLanguage(string requestedLanguage)
        {
            string value = requestedLanguage.Trim();
            foreach (var entry in Languages)
            {
                if (string.Equals(entry.Language.Id, value, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(entry.Language.ScopeName, value, StringComparison.Ordinal) ||
                    entry.Aliases.Any(alias => string.Equals(alias, value, StringComparison.OrdinalIgnoreCase)))
                {
                    return entry.Language;
                }
            }

            return value.StartsWith("source.", StringComparison.Ordinal) ||
                   value.StartsWith("text.", StringComparison.Ordinal)
                ? new EditorSyntaxLanguage(value, value, value)
                : null;
        }

        public EditorSyntaxTheme ResolveTheme(string requestedTheme)
        {
            foreach (var theme in Themes)
            {
                if (string.Equals(theme.Name, requestedTheme, StringComparison.OrdinalIgnoreCase))
                    return theme;
            }

            return new EditorSyntaxTheme("Dark+");
        }

        private static EditorSyntaxLanguageCatalogEntry Entry(
            string id,
            string scope,
            string displayName,
            params string[] aliases) =>
            new(new EditorSyntaxLanguage(id, scope, displayName), aliases);
    }
}

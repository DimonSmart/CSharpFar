using CSharpFar.App.Editor;
using CSharpFar.App.Settings;
using CSharpFar.Console.Input;
using CSharpFar.Core.Models;
using CSharpFar.Tests.Fakes;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

public sealed class SettingsDialogTests
{
    [Fact]
    public void Show_F10ReturnsExistingValues()
    {
        var driver = Driver(Key(ConsoleKey.F10));
        var panels = new CSharpFarPanelSettings(
            PanelViewMode.BriefTwoColumns,
            PanelViewMode.Full,
            ShowHiddenAndSystemFiles: false,
            SelectFolders: true,
            RightClickSelectsFiles: false,
            SortFoldersByExtension: true,
            RememberLastDirectories: true,
            FileHighlightingEnabled: false,
            ShowStatusLine: true,
            ShowFilesTotalInformation: false,
            ShowFreeSize: true,
            ShowSortModeLetter: false,
            ShowParentDirectoryInRootFolders: true);

        CSharpFarSettingsDialogResult? result = new CSharpFarSettingsDialog(
            new DialogService(ModalTestHost.Create(driver), new FormFieldFactory(TextFieldHistoryTestProvider.Create()))).Show(
                panels,
                "FarClassic",
                editorSyntaxHighlightingEnabled: true,
                editorSyntaxTheme: "DarkPlus",
                syntaxCatalog: SyntaxCatalog());

        Assert.NotNull(result);
        Assert.Equal(panels, result.Panels);
        Assert.Equal("FarClassic", result.PaletteName);
        Assert.True(result.EditorSyntaxHighlightingEnabled);
        Assert.Equal("Dark+", result.EditorSyntaxTheme);
    }

    [Fact]
    public void Show_EscapeReturnsNullAndRestoresTheme()
    {
        using var theme = UiTheme.UseTemporary(PaletteRegistry.Default);
        var driver = Driver(
            Key(ConsoleKey.DownArrow),
            Key(ConsoleKey.RightArrow),
            Key(ConsoleKey.RightArrow),
            Key(ConsoleKey.Escape));

        CSharpFarSettingsDialogResult? result = new CSharpFarSettingsDialog(
            new DialogService(ModalTestHost.Create(driver), new FormFieldFactory(TextFieldHistoryTestProvider.Create()))).Show(
                DefaultPanels(),
                "Default",
                editorSyntaxHighlightingEnabled: true,
                editorSyntaxTheme: "DarkPlus",
                syntaxCatalog: SyntaxCatalog());

        Assert.Null(result);
        Assert.Same(PaletteRegistry.Default, UiTheme.Current);
    }

    [Theory]
    [InlineData("Dark+", "Dark+")]
    [InlineData("DarkPlus", "Dark+")]
    [InlineData("dark plus", "Dark+")]
    [InlineData("MissingLegacyTheme", "Dark+")]
    [InlineData("Monokai", "Monokai")]
    public void Show_F10ResolvesInitialThemeToCanonicalCatalogItem(string configuredTheme, string expectedTheme)
    {
        var driver = Driver(Key(ConsoleKey.F10));

        var result = new CSharpFarSettingsDialog(
            new DialogService(ModalTestHost.Create(driver), new FormFieldFactory(TextFieldHistoryTestProvider.Create()))).Show(
                DefaultPanels(),
                "Default",
                editorSyntaxHighlightingEnabled: true,
                editorSyntaxTheme: configuredTheme,
                syntaxCatalog: SyntaxCatalog());

        Assert.NotNull(result);
        Assert.Equal(expectedTheme, result.EditorSyntaxTheme);
    }

    [Fact]
    public void Show_ThemeDropdownRemainsEditableWhenSyntaxHighlightingIsDisabled()
    {
        var driver = Driver(
            Key(ConsoleKey.DownArrow),
            Key(ConsoleKey.DownArrow),
            Key(ConsoleKey.RightArrow),
            Key(ConsoleKey.Tab),
            Key(ConsoleKey.F4),
            Key(ConsoleKey.DownArrow),
            Key(ConsoleKey.Enter),
            Key(ConsoleKey.F10));

        var result = new CSharpFarSettingsDialog(
            new DialogService(ModalTestHost.Create(driver), new FormFieldFactory(TextFieldHistoryTestProvider.Create()))).Show(
                DefaultPanels(),
                "Default",
                editorSyntaxHighlightingEnabled: false,
                editorSyntaxTheme: "DarkPlus",
                syntaxCatalog: SyntaxCatalog());

        Assert.NotNull(result);
        Assert.False(result.EditorSyntaxHighlightingEnabled);
        Assert.Equal("Monokai", result.EditorSyntaxTheme);
    }

    private static CSharpFarPanelSettings DefaultPanels() =>
        new(
            PanelViewMode.Full,
            PanelViewMode.Full,
            ShowHiddenAndSystemFiles: true,
            SelectFolders: true,
            RightClickSelectsFiles: true,
            SortFoldersByExtension: true,
            RememberLastDirectories: false,
            FileHighlightingEnabled: true,
            ShowStatusLine: true,
            ShowFilesTotalInformation: true,
            ShowFreeSize: false,
            ShowSortModeLetter: true,
            ShowParentDirectoryInRootFolders: false);

    private static IEditorSyntaxCatalog SyntaxCatalog() =>
        new FakeEditorSyntaxCatalog(
            ["Monokai", "Dark+", "Solarized Dark"],
            "Dark+",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["DarkPlus"] = "Dark+",
                ["dark plus"] = "Dark+",
            });

    private sealed class FakeEditorSyntaxCatalog : IEditorSyntaxCatalog
    {
        private readonly string _fallbackTheme;
        private readonly IReadOnlyDictionary<string, string> _aliases;

        public FakeEditorSyntaxCatalog(
            IReadOnlyList<string> themes,
            string fallbackTheme,
            IReadOnlyDictionary<string, string> aliases)
        {
            Themes = themes.Select(name => new EditorSyntaxTheme(name)).ToArray();
            _fallbackTheme = fallbackTheme;
            _aliases = aliases;
        }

        public IReadOnlyList<EditorSyntaxLanguageCatalogEntry> Languages => [];

        public EditorSyntaxLanguage? ResolveLanguage(string requestedLanguage) => null;

        public IReadOnlyList<EditorSyntaxTheme> Themes { get; }

        public EditorSyntaxTheme ResolveTheme(string requestedTheme)
        {
            var canonicalTheme = Themes.FirstOrDefault(theme =>
                string.Equals(theme.Name, requestedTheme, StringComparison.OrdinalIgnoreCase))?.Name;

            if (canonicalTheme is null)
                _aliases.TryGetValue(requestedTheme, out canonicalTheme);

            return new EditorSyntaxTheme(canonicalTheme ?? _fallbackTheme);
        }
    }

    private static FakeConsoleDriver Driver(params ConsoleInputEvent[] inputs)
    {
        var driver = new FakeConsoleDriver(width: 80, height: 25);
        foreach (ConsoleInputEvent input in inputs)
            driver.EnqueueInput(input);
        return driver;
    }

    private static KeyConsoleInputEvent Key(ConsoleKey key) =>
        new(new ConsoleKeyInfo('\0', key, shift: false, alt: false, control: false));
}

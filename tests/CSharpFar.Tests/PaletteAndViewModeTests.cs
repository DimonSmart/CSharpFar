using CSharpFar.App.Rendering;
using CSharpFar.App.Settings;
using CSharpFar.Console;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;
using CSharpFar.Tests.Fakes;

namespace CSharpFar.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// PaletteRegistry tests
// ─────────────────────────────────────────────────────────────────────────────

public class PaletteRegistryTests
{
    [Theory]
    [InlineData("Default", "Default")]
    [InlineData("FarClassic", "FarClassic")]
    [InlineData("farclassic", "FarClassic")]
    public void Resolve_KnownPaletteName_ReturnsBuiltInPalette(string name, string expected)
    {
        var palette = PaletteRegistry.Resolve(name);

        Assert.Equal(expected, palette.Name);
    }

    [Fact]
    public void Names_ContainsBuiltInPalettes()
    {
        Assert.Contains("Default", PaletteRegistry.Names);
        Assert.Contains("FarClassic", PaletteRegistry.Names);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("NoSuchPalette")]
    public void Resolve_MissingPaletteName_FallsBackToDefault(string? name)
    {
        var palette = PaletteRegistry.Resolve(name);

        Assert.Equal("Default", palette.Name);
    }

    [Fact]
    public void FarClassic_HasDistinctSelectedColors()
    {
        var fc = PaletteRegistry.FarClassic;
        // FarClassic selection = Black on Green (like Far Manager)
        Assert.Equal(ConsoleColor.Black, fc.SelectedFg);
        Assert.Equal(ConsoleColor.Green, fc.SelectedBg);
        Assert.Equal(ConsoleColor.Black, fc.CursorActiveFg);
        Assert.Equal(ConsoleColor.Green, fc.CursorActiveBg);
        Assert.Equal(ConsoleColor.DarkCyan, fc.PanelPathActiveBg);
    }

    [Fact]
    public void BuiltInPalettes_HaveFarLikeMenuWindowColors()
    {
        foreach (var palette in PaletteRegistry.All)
        {
            Assert.Equal(ConsoleColor.DarkCyan, palette.MenuBarNormalBg);
            Assert.Equal(ConsoleColor.Black, palette.MenuBarActiveBg);
            Assert.Equal(ConsoleColor.White, palette.MenuNormalFg);
            Assert.Equal(ConsoleColor.DarkCyan, palette.MenuNormalBg);
            Assert.Equal(ConsoleColor.White, palette.MenuActiveFg);
            Assert.Equal(ConsoleColor.Black, palette.MenuActiveBg);
            Assert.Equal(ConsoleColor.Yellow, palette.MenuHighlightFg);
            Assert.Equal(ConsoleColor.DarkCyan, palette.MenuHighlightBg);
            Assert.Equal(ConsoleColor.Yellow, palette.MenuActiveHighlightFg);
            Assert.Equal(ConsoleColor.Black, palette.MenuActiveHighlightBg);
            Assert.Equal(ConsoleColor.DarkGray, palette.MenuDisabledFg);
            Assert.Equal(ConsoleColor.DarkCyan, palette.MenuDisabledBg);
            Assert.Equal(ConsoleColor.White, palette.MenuBorderFg);
            Assert.Equal(ConsoleColor.DarkCyan, palette.MenuBorderBg);
        }
    }

    [Fact]
    public void BuiltInPalettes_HaveFarLikeFunctionKeyColors()
    {
        foreach (var palette in PaletteRegistry.All)
        {
            Assert.Equal(ConsoleColor.White, palette.FunctionKeyNumFg);
            Assert.Equal(ConsoleColor.Black, palette.FunctionKeyNumBg);
            Assert.Equal(ConsoleColor.Black, palette.FunctionKeyTextFg);
            Assert.Equal(palette.CursorActiveBg, palette.FunctionKeyBarBg);
        }
    }

    [Fact]
    public void BuiltInPalettes_HaveDirectoryShortcutNumberColors()
    {
        foreach (var palette in PaletteRegistry.All)
        {
            Assert.Equal(ConsoleColor.White, palette.DirectoryShortcutBarNumberFg);
            Assert.Equal(ConsoleColor.Black, palette.DirectoryShortcutBarNumberBg);
        }
    }

    [Fact]
    public void PaletteStyles_UseFarLikeDriveMenuColors()
    {
        var palette = PaletteRegistry.Default;

        Assert.Equal(ConsoleColor.White, PaletteStyles.DialogFill(palette).Foreground);
        Assert.Equal(ConsoleColor.DarkCyan, PaletteStyles.DialogFill(palette).Background);
        Assert.Equal(ConsoleColor.White, PaletteStyles.DialogBorder(palette).Foreground);
        Assert.Equal(ConsoleColor.DarkCyan, PaletteStyles.DialogBorder(palette).Background);
        Assert.Equal(ConsoleColor.White, PaletteStyles.DialogTitle(palette).Foreground);
        Assert.Equal(ConsoleColor.DarkCyan, PaletteStyles.DialogTitle(palette).Background);
        Assert.Equal(ConsoleColor.Yellow, PaletteStyles.DialogHighlight(palette).Foreground);
        Assert.Equal(ConsoleColor.DarkCyan, PaletteStyles.DialogHighlight(palette).Background);
        Assert.Equal(ConsoleColor.White, PaletteStyles.InputField(palette).Foreground);
        Assert.Equal(ConsoleColor.Black, PaletteStyles.InputField(palette).Background);
        Assert.Equal(ConsoleColor.Yellow, PaletteStyles.InputHighlight(palette).Foreground);
        Assert.Equal(ConsoleColor.Black, PaletteStyles.InputHighlight(palette).Background);
        Assert.Equal(ConsoleColor.Yellow, PaletteStyles.DialogError(palette).Foreground);
        Assert.Equal(ConsoleColor.DarkCyan, PaletteStyles.DialogError(palette).Background);
    }

}

// ─────────────────────────────────────────────────────────────────────────────
// BriefTwoColumnsPanelRenderer tests
// ─────────────────────────────────────────────────────────────────────────────

public class BriefTwoColumnsPanelRendererTests
{
    private static (ScreenRenderer screen, FakeConsoleDriver driver) CreateScreen(int w = 40, int h = 12)
    {
        var drv = new FakeConsoleDriver(w, h);
        return (new ScreenRenderer(drv), drv);
    }

    private static FilePanelState MakeState(params string[] names)
    {
        var state = new FilePanelState { CurrentDirectory = @"C:\Test" };
        foreach (var n in names)
        {
            state.Items.Add(new FilePanelItem
            {
                Name = n,
                FullPath = @"C:\Test\" + n,
                IsDirectory = false,
            });
        }
        return state;
    }

    // bounds.Height - 5 rows per column × 2 columns
    [Theory]
    [InlineData(12, 12)]   // 12 - 6 = 6 per col × 2 = 12
    [InlineData(7, 2)]   // 7  - 6 = 1 per col × 2 = 2
    [InlineData(5, 0)]   // 5  - 6 <= 0
    public void VisibleRows_ReturnsDoubleRowsMinusBorders(int height, int expected)
    {
        int result = BriefTwoColumnsPanelRenderer.VisibleRows(new Rect(0, 0, 40, height));
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Render_ShowsHeaderRowWithName()
    {
        var (screen, driver) = CreateScreen(40, 12);
        var state = MakeState("alpha.txt");
        var renderer = new BriefTwoColumnsPanelRenderer(screen, PaletteRegistry.Default);

        renderer.Render(new Rect(0, 0, 40, 12), state, isActive: true);

        // Header is at row 1 (row 0 = top border)
        string headerRow = driver.GetRow(1);
        Assert.Equal('n', headerRow[1]);
        Assert.Contains("Name", headerRow);
    }

    [Fact]
    public void Render_UsesFarSortIndicatorLetters()
    {
        var (screen, driver) = CreateScreen(40, 12);
        var state = MakeState("alpha.txt");
        state.SortMode = SortMode.LastWriteTime;
        state.SortDescending = true;
        var renderer = new BriefTwoColumnsPanelRenderer(screen, PaletteRegistry.Default);

        renderer.Render(new Rect(0, 0, 40, 12), state, isActive: true);

        Assert.Equal('W', driver.GetRow(1)[1]);
    }

    [Fact]
    public void Render_CentersPathTitleAndHighlightsFullPath()
    {
        var (screen, driver) = CreateScreen(40, 12);
        var state = MakeState("alpha.txt");
        state.CurrentDirectory = @"C:\Test";
        var renderer = new BriefTwoColumnsPanelRenderer(screen, PaletteRegistry.Default);

        renderer.Render(new Rect(0, 0, 40, 12), state, isActive: true);

        int titleX = driver.GetRow(0).IndexOf(@" C:\Test ", StringComparison.Ordinal);
        Assert.True(titleX > 1);
        for (int x = titleX; x < titleX + @" C:\Test ".Length; x++)
            Assert.Equal(PaletteRegistry.Default.PanelPathActiveBg, driver.GetCell(x, 0).Background);
    }

    [Fact]
    public void Render_UsesSamePanelColorsForActiveAndInactivePanels()
    {
        var (screen, driver) = CreateScreen(80, 12);
        var activeState = MakeState("alpha.txt", "beta.txt");
        var inactiveState = MakeState("alpha.txt", "beta.txt");
        var renderer = new BriefTwoColumnsPanelRenderer(screen, PaletteRegistry.Default);

        renderer.Render(new Rect(0, 0, 40, 12), activeState, isActive: true);
        renderer.Render(new Rect(40, 0, 40, 12), inactiveState, isActive: false);

        Assert.Equal(driver.GetCell(0, 0).Foreground, driver.GetCell(40, 0).Foreground);
        Assert.Equal(driver.GetCell(1, 3).Foreground, driver.GetCell(41, 3).Foreground);
    }

    [Fact]
    public void Render_DoesNotShowCursorHighlightOnInactivePanel()
    {
        var (screen, driver) = CreateScreen(40, 12);
        var state = MakeState("alpha.txt");
        state.CursorIndex = 0;
        var renderer = new BriefTwoColumnsPanelRenderer(screen, PaletteRegistry.Default);

        renderer.Render(new Rect(0, 0, 40, 12), state, isActive: false);

        Assert.Equal(PaletteRegistry.Default.PanelBackground, driver.GetCell(1, 2).Background);
    }

    [Fact]
    public void Render_ShowsItemInFirstColumn()
    {
        var (screen, driver) = CreateScreen(40, 12);
        var state = MakeState("alpha.txt", "beta.txt");
        var renderer = new BriefTwoColumnsPanelRenderer(screen, PaletteRegistry.Default);

        renderer.Render(new Rect(0, 0, 40, 12), state, isActive: true);

        // Content starts at row 2; first item should be in row 2
        string row2 = driver.GetRow(2);
        Assert.Contains("alpha.txt", row2);
    }

    [Fact]
    public void Render_SecondColumnStartsAtRowsPerColOffset()
    {
        int height = 12;
        int rowsPerCol = BriefTwoColumnsPanelRenderer.VisibleRows(new Rect(0, 0, 40, height)) / 2;
        var (screen, driver) = CreateScreen(40, height);

        // Fill more items than one column can hold
        var names = Enumerable.Range(1, rowsPerCol + 2)
            .Select(i => $"file{i:D2}.txt")
            .ToArray();
        var state = MakeState(names);
        var renderer = new BriefTwoColumnsPanelRenderer(screen, PaletteRegistry.Default);

        renderer.Render(new Rect(0, 0, 40, height), state, isActive: true);

        // Item at index rowsPerCol should appear in second column at row 2
        string row = driver.GetRow(2);
        Assert.Contains(names[rowsPerCol], row);
    }

    [Fact]
    public void Render_CursorInFirstColumn_HighlightedWithCursorStyle()
    {
        var (screen, driver) = CreateScreen(40, 12);
        var state = MakeState("alpha.txt", "beta.txt");
        state.CursorIndex = 0; // cursor on first item
        var renderer = new BriefTwoColumnsPanelRenderer(screen, PaletteRegistry.Default);

        renderer.Render(new Rect(0, 0, 40, 12), state, isActive: true);

        // Cursor row (row 2, col1) should use cursor background
        var cell = driver.GetCell(1, 2);
        Assert.Equal(PaletteRegistry.Default.CursorActiveBg, cell.Background);
    }

    [Fact]
    public void Render_CursorInSecondColumn_OnlySecondColumnHighlighted()
    {
        int height = 12;
        int rowsPerCol = BriefTwoColumnsPanelRenderer.VisibleRows(new Rect(0, 0, 40, height)) / 2;
        var names = Enumerable.Range(0, rowsPerCol + 1)
            .Select(i => $"f{i:D2}.txt")
            .ToArray();
        var (screen, driver) = CreateScreen(40, height);
        var state = MakeState(names);
        state.CursorIndex = rowsPerCol; // first item of second column
        var renderer = new BriefTwoColumnsPanelRenderer(screen, PaletteRegistry.Default);

        renderer.Render(new Rect(0, 0, 40, height), state, isActive: true);

        // Row 2 (content row 0), second column: cursor background
        // The separator is at innerWidth/2 = (40-2)/2 = 19, so col2 starts at x=1+19+1=21
        int innerWidth = 40 - 2;
        int sepOffset = innerWidth / 2;
        int col2X = 1 + sepOffset + 1;
        var cellCol2 = driver.GetCell(col2X, 2);
        Assert.Equal(PaletteRegistry.Default.CursorActiveBg, cellCol2.Background);

        // First column at row 2 should NOT have cursor background
        var cellCol1 = driver.GetCell(1, 2);
        Assert.NotEqual(PaletteRegistry.Default.CursorActiveBg, cellCol1.Background);
    }

    [Fact]
    public void Render_LongNameTruncated()
    {
        var (screen, driver) = CreateScreen(20, 8);
        var longName = new string('A', 50);
        var state = MakeState(longName);
        var renderer = new BriefTwoColumnsPanelRenderer(screen, PaletteRegistry.Default);

        renderer.Render(new Rect(0, 0, 20, 8), state, isActive: true);

        // No cell should contain 'A' beyond column boundary; brief mode truncates without '~'.
        int innerWidth = 20 - 2;
        int sepOffset = innerWidth / 2;  // = 9
        int col1Width = sepOffset;       // = 9
        // col1 occupies x = 1..9, so cell at x=9 (0-indexed from col start = 1+8=9) is last char
        var lastCell = driver.GetCell(1 + col1Width - 1, 2);
        Assert.Equal('A', lastCell.Character);
        Assert.DoesNotContain("~", driver.GetRow(2));
    }

    [Fact]
    public void Render_ShowsFarLikeStatusRows()
    {
        var (screen, driver) = CreateScreen(44, 12);
        var state = new FilePanelState { CurrentDirectory = @"C:\Test" };
        state.Items.Add(new FilePanelItem
        {
            Name = "readme.md",
            FullPath = @"C:\Test\readme.md",
            IsDirectory = false,
            Size = 18_900,
            LastWriteTime = new DateTime(2026, 5, 7, 9, 47, 0),
        });
        state.Items.Add(new FilePanelItem
        {
            Name = "src",
            FullPath = @"C:\Test\src",
            IsDirectory = true,
        });
        var renderer = new BriefTwoColumnsPanelRenderer(screen, PaletteRegistry.Default);

        renderer.Render(new Rect(0, 0, 44, 12), state, isActive: true);

        Assert.Contains("────────", driver.GetRow(8));
        Assert.Equal('╟', driver.GetCell(0, 8).Character);
        Assert.Equal('┴', driver.GetCell(22, 8).Character);
        Assert.Equal('╢', driver.GetCell(43, 8).Character);
        Assert.Contains("readme.md", driver.GetRow(9));
        Assert.Contains("18,9 K", driver.GetRow(9));
        Assert.Contains("07.05.26 09:47", driver.GetRow(9));
        Assert.Contains("Bytes: 18,9 K, files: 1, folders: 1", driver.GetRow(10));
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// AppSettings persistence for palette and view mode
// ─────────────────────────────────────────────────────────────────────────────

public class PaletteSettingsPersistenceTests : IDisposable
{
    private readonly string _tempDir;

    public PaletteSettingsPersistenceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"CSharpFarPaletteTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void JsonSettingsStore_SavesAndLoadsPalette()
    {
        var store = new JsonSettingsStore(_tempDir);
        store.Settings.Ui.Palette = "FarClassic";
        store.Save();

        var store2 = new JsonSettingsStore(_tempDir);
        Assert.Equal("FarClassic", store2.Settings.Ui.Palette);
    }

    [Fact]
    public void JsonSettingsStore_SavesAndLoadsViewModes()
    {
        var store = new JsonSettingsStore(_tempDir);
        store.Settings.Panels.LeftViewMode = "BriefTwoColumns";
        store.Settings.Panels.RightViewMode = "Full";
        store.Save();

        var store2 = new JsonSettingsStore(_tempDir);
        Assert.Equal("BriefTwoColumns", store2.Settings.Panels.LeftViewMode);
        Assert.Equal("Full", store2.Settings.Panels.RightViewMode);
    }
}

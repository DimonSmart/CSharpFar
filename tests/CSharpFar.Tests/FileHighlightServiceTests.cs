using CSharpFar.Core.Highlighting;
using CSharpFar.Core.Models;

namespace CSharpFar.Tests;

/// <summary>
/// Tests for FileHighlightService covering attribute matching, color resolution,
/// FarDefault rules, and settings modes.
/// </summary>
public class FileHighlightServiceTests
{
    // ── base row colors used throughout color tests ───────────────────────────
    // These match the spec test table (independent of actual palette).

    private static readonly ConsoleColorPair NormalBase = new(15, 1);
    private static readonly ConsoleColorPair CursorBase = new(0, 3);
    private static readonly ConsoleColorPair SelectedBase = new(14, 1);
    private static readonly ConsoleColorPair SelCursorBase = new(0, 3);

    // ── factory ───────────────────────────────────────────────────────────────

    private static FileHighlightService FarDefault(string? pathExt = "")
        => new(FarDefaultHighlightPreset.Rules,
               FarDefaultHighlightPreset.GroupsByName,
               pathExt);

    private static FilePanelItem MakeFile(string name, FileAttributes attrs = default) =>
        new() { Name = name, FullPath = @"C:\test\" + name, IsDirectory = false, Attributes = attrs };

    private static FilePanelItem MakeDir(string name, FileAttributes attrs = FileAttributes.Directory) =>
        new() { Name = name, FullPath = @"C:\test\" + name, IsDirectory = true, Attributes = attrs };

    private static ConsoleColorPair Resolve(ConsoleColorPair baseColor, FileHighlightColor? hl)
    {
        if (hl is null) return baseColor;
        return new ConsoleColorPair(
            hl.Foreground ?? baseColor.Foreground,
            hl.Background ?? baseColor.Background);
    }

    private static ConsoleColorPair Resolve(ConsoleColorPair baseColor, HighlightResult result)
        => Resolve(baseColor, result.ColorOverride);

    // ── Attribute tests ───────────────────────────────────────────────────────

    [Fact]
    public void Hidden_File_Matches_FarHidden()
    {
        var svc = FarDefault();
        var item = MakeFile("secret.txt", FileAttributes.Hidden);
        var r = svc.GetHighlight(item, FileRowState.Normal);
        Assert.Contains("far.hidden", r.MatchedRuleIds);
    }

    [Fact]
    public void System_File_Matches_FarSystem()
    {
        var svc = FarDefault();
        var item = MakeFile("pagefile.sys", FileAttributes.System);
        var r = svc.GetHighlight(item, FileRowState.Normal);
        Assert.Contains("far.system", r.MatchedRuleIds);
    }

    [Fact]
    public void Directory_Matches_FarDirectory()
    {
        var svc = FarDefault();
        var item = MakeDir("src");
        var r = svc.GetHighlight(item, FileRowState.Normal);
        Assert.Contains("far.directory", r.MatchedRuleIds);
    }

    [Fact]
    public void ParentDirectory_Has_Directory_Attribute()
    {
        var item = new FilePanelItem
        {
            Name = "..",
            FullPath = @"C:\",
            IsDirectory = true,
            IsParentDirectory = true,
            Attributes = FileAttributes.Directory,
        };
        var svc = FarDefault();
        var r = svc.GetHighlight(item, FileRowState.Normal);
        Assert.Contains("far.directory", r.MatchedRuleIds);
    }

    [Fact]
    public void Directory_Named_TestZip_Matches_FarDirectory_Not_Archive()
    {
        var svc = FarDefault();
        var item = MakeDir("test.zip");
        var r = svc.GetHighlight(item, FileRowState.Normal);
        Assert.Contains("far.directory", r.MatchedRuleIds);
        Assert.DoesNotContain("far.archive", r.MatchedRuleIds);
    }

    [Fact]
    public void Regular_File_TestZip_Matches_FarArchive()
    {
        var svc = FarDefault();
        var item = MakeFile("test.zip");
        var r = svc.GetHighlight(item, FileRowState.Normal);
        Assert.Contains("far.archive", r.MatchedRuleIds);
    }

    [Fact]
    public void Regular_File_AppExe_Matches_FarExec()
    {
        var svc = FarDefault();
        var item = MakeFile("app.exe");
        var r = svc.GetHighlight(item, FileRowState.Normal);
        Assert.Contains("far.exec", r.MatchedRuleIds);
    }

    [Fact]
    public void Regular_File_TempTmp_Matches_FarTemp()
    {
        var svc = FarDefault();
        var item = MakeFile("temp.tmp");
        var r = svc.GetHighlight(item, FileRowState.Normal);
        Assert.Contains("far.temp", r.MatchedRuleIds);
    }

    // ── Color tests ───────────────────────────────────────────────────────────

    [Fact]
    public void Exec_Normal_Fg10_Bg_Inherited()
    {
        var svc = FarDefault();
        var item = MakeFile("app.exe");
        var r = svc.GetHighlight(item, FileRowState.Normal);
        var col = Resolve(NormalBase, r);
        Assert.Equal(10, col.Foreground);
        Assert.Equal(1, col.Background); // inherited
    }

    [Fact]
    public void Exec_Cursor_Fg10_CursorBg_Inherited()
    {
        var svc = FarDefault();
        var item = MakeFile("app.exe");
        var r = svc.GetHighlight(item, FileRowState.Cursor);
        var col = Resolve(CursorBase, r);
        Assert.Equal(10, col.Foreground);
        Assert.Equal(3, col.Background); // cursor base bg
    }

    [Fact]
    public void Archive_Normal_Fg13()
    {
        var svc = FarDefault();
        var item = MakeFile("archive.zip");
        var r = svc.GetHighlight(item, FileRowState.Normal);
        var col = Resolve(NormalBase, r);
        Assert.Equal(13, col.Foreground);
        Assert.Equal(1, col.Background);
    }

    [Fact]
    public void Temp_Normal_Fg6()
    {
        var svc = FarDefault();
        var item = MakeFile("temp.tmp");
        var r = svc.GetHighlight(item, FileRowState.Normal);
        var col = Resolve(NormalBase, r);
        Assert.Equal(6, col.Foreground);
        Assert.Equal(1, col.Background);
    }

    [Fact]
    public void Directory_Normal_Fg15()
    {
        var svc = FarDefault();
        var item = MakeDir("src");
        var r = svc.GetHighlight(item, FileRowState.Normal);
        var col = Resolve(NormalBase, r);
        Assert.Equal(15, col.Foreground);
        Assert.Equal(1, col.Background);
    }

    [Fact]
    public void Hidden_Normal_Fg3()
    {
        var svc = FarDefault();
        var item = MakeFile("hidden.txt", FileAttributes.Hidden);
        var r = svc.GetHighlight(item, FileRowState.Normal);
        var col = Resolve(NormalBase, r);
        Assert.Equal(3, col.Foreground);
        Assert.Equal(1, col.Background);
    }

    [Fact]
    public void Hidden_Cursor_Fg8_BgInherited()
    {
        var svc = FarDefault();
        var item = MakeFile("hidden.txt", FileAttributes.Hidden);
        var r = svc.GetHighlight(item, FileRowState.Cursor);
        var col = Resolve(CursorBase, r);
        Assert.Equal(8, col.Foreground);
        Assert.Equal(3, col.Background); // cursor base bg
    }

    [Fact]
    public void System_Normal_Fg3()
    {
        var svc = FarDefault();
        var item = MakeFile("sys.bin", FileAttributes.System);
        var r = svc.GetHighlight(item, FileRowState.Normal);
        var col = Resolve(NormalBase, r);
        Assert.Equal(3, col.Foreground);
    }

    [Fact]
    public void Selected_Exec_Returns_Selected_Base_Color()
    {
        // FarDefault: exec Selected = null → no override
        var svc = FarDefault();
        var item = MakeFile("app.exe");
        var r = svc.GetHighlight(item, FileRowState.Selected);
        Assert.Null(r.ColorOverride); // no override for Selected state
        var col = Resolve(SelectedBase, r);
        Assert.Equal(14, col.Foreground);
        Assert.Equal(1, col.Background);
    }

    [Fact]
    public void No_Rule_Match_Returns_Empty_Result()
    {
        var svc = FarDefault();
        var item = MakeFile("plain.txt"); // not exec/arc/temp/hidden/system/dir
        var r = svc.GetHighlight(item, FileRowState.Normal);
        Assert.Null(r.ColorOverride);
        Assert.Empty(r.MatchedRuleIds);
    }

    // ── Settings mode tests ───────────────────────────────────────────────────

    [Fact]
    public void PresetOnly_Ignores_User_Rules()
    {
        var userRule = new FileHighlightRule
        {
            Id = "user.txt",
            DisplayName = "txt",
            Order = 1,
            MaskExpression = "*.txt",
            Colors = new FileHighlightColors { Normal = new FileHighlightColor(9, null) },
        };
        var settings = new AppSettings();
        settings.Panels.FileHighlighting.Mode = "PresetOnly";
        settings.Panels.FileHighlighting.Rules = [userRule];
        settings.Panels.FileHighlighting.MaskGroups = [];

        var rules = FarDefaultHighlightPreset.Rules;
        var groups = FarDefaultHighlightPreset.GroupsByName;
        var svc = new FileHighlightService(rules, groups);

        var item = MakeFile("readme.txt");
        var r = svc.GetHighlight(item, FileRowState.Normal);
        Assert.DoesNotContain("user.txt", r.MatchedRuleIds);
    }

    [Fact]
    public void UserRulesOnly_Does_Not_Load_FarDefault_Rules()
    {
        var userRule = new FileHighlightRule
        {
            Id = "user.exe",
            DisplayName = "exe",
            Order = 1,
            MaskExpression = "*.exe",
            Colors = new FileHighlightColors { Normal = new FileHighlightColor(9, null) },
        };
        var svc = new FileHighlightService([userRule], FarDefaultHighlightPreset.GroupsByName);

        var exe = MakeFile("app.exe");
        var r = svc.GetHighlight(exe, FileRowState.Normal);
        Assert.Contains("user.exe", r.MatchedRuleIds);
        Assert.DoesNotContain("far.exec", r.MatchedRuleIds);
    }

    [Fact]
    public void PresetPlusUserRules_UserGroup_Replaces_Preset_Group()
    {
        // Replace "temp" group so that *.log matches
        var userGroup = new MaskGroup { Name = "temp", MaskExpression = "*.log,*.bak,*.tmp" };
        var groups = new Dictionary<string, MaskGroup>(FarDefaultHighlightPreset.GroupsByName,
            StringComparer.OrdinalIgnoreCase)
        {
            ["temp"] = userGroup,
        };
        var svc = new FileHighlightService(FarDefaultHighlightPreset.Rules, groups);
        var item = MakeFile("app.log");
        var r = svc.GetHighlight(item, FileRowState.Normal);
        Assert.Contains("far.temp", r.MatchedRuleIds);
    }

    // ── Settings enabled/disabled ─────────────────────────────────────────────

    [Fact]
    public void DefaultSettings_FileHighlighting_Enabled()
    {
        var s = new AppSettings();
        Assert.True(s.Panels.FileHighlighting.Enabled);
    }

    [Fact]
    public void DefaultSettings_FileHighlighting_Preset_Is_FarDefault()
    {
        var s = new AppSettings();
        Assert.Equal("FarDefault", s.Panels.FileHighlighting.Preset);
    }
}

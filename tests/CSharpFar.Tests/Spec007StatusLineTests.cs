using CSharpFar.App.Rendering;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

namespace CSharpFar.Tests;

/// <summary>
/// Stage 7 – Status line: GetStatusRowCount and VisibleRows behavior.
/// </summary>
public sealed class Spec007StatusLineTests
{
    // ── GetStatusRowCount ─────────────────────────────────────────────────────

    [Fact]
    public void GetStatusRowCount_NullOptions_ReturnsThree()
    {
        Assert.Equal(3, PanelStatusRenderer.GetStatusRowCount(null));
    }

    [Fact]
    public void GetStatusRowCount_DefaultOptions_ReturnsThree()
    {
        var opts = new AppSettings.PanelOptionsSettings();
        Assert.Equal(3, PanelStatusRenderer.GetStatusRowCount(opts));
    }

    [Fact]
    public void GetStatusRowCount_ShowStatusLine_False_ReturnsZero()
    {
        var opts = new AppSettings.PanelOptionsSettings { ShowStatusLine = false };
        Assert.Equal(0, PanelStatusRenderer.GetStatusRowCount(opts));
    }

    [Fact]
    public void GetStatusRowCount_ShowTotal_False_ShowFree_False_ReturnsTwo()
    {
        var opts = new AppSettings.PanelOptionsSettings
        {
            ShowStatusLine = true,
            ShowFilesTotalInformation = false,
            ShowFreeSize = false,
        };
        Assert.Equal(2, PanelStatusRenderer.GetStatusRowCount(opts));
    }

    [Fact]
    public void GetStatusRowCount_ShowTotal_False_ShowFree_True_ReturnsThree()
    {
        var opts = new AppSettings.PanelOptionsSettings
        {
            ShowStatusLine = true,
            ShowFilesTotalInformation = false,
            ShowFreeSize = true,
        };
        Assert.Equal(3, PanelStatusRenderer.GetStatusRowCount(opts));
    }

    [Fact]
    public void GetStatusRowCount_ShowTotal_True_ShowFree_False_ReturnsThree()
    {
        var opts = new AppSettings.PanelOptionsSettings
        {
            ShowStatusLine = true,
            ShowFilesTotalInformation = true,
            ShowFreeSize = false,
        };
        Assert.Equal(3, PanelStatusRenderer.GetStatusRowCount(opts));
    }

    // ── PanelRenderer.VisibleRows ─────────────────────────────────────────────

    [Fact]
    public void VisibleRows_StatusHidden_MoreRows()
    {
        var bounds = new Rect(0, 0, 40, 20);
        var withStatus = new AppSettings.PanelOptionsSettings { ShowStatusLine = true };
        var noStatus = new AppSettings.PanelOptionsSettings { ShowStatusLine = false };

        int rowsWith = PanelRenderer.VisibleRows(bounds, withStatus);
        int rowsNo = PanelRenderer.VisibleRows(bounds, noStatus);

        Assert.True(rowsNo > rowsWith,
            $"Rows without status ({rowsNo}) should exceed rows with status ({rowsWith})");
    }

    [Fact]
    public void VisibleRows_NullOptions_EqualToDefaultOptions()
    {
        var bounds = new Rect(0, 0, 40, 20);

        int rowsNull = PanelRenderer.VisibleRows(bounds, null);
        int rowsDefault = PanelRenderer.VisibleRows(bounds, new AppSettings.PanelOptionsSettings());

        Assert.Equal(rowsDefault, rowsNull);
    }
}

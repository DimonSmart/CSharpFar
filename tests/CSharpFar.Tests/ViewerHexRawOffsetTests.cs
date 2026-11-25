using System.Text;
using CSharpFar.Console;
using CSharpFar.Tests.Fakes;

namespace CSharpFar.Tests;

public sealed class ViewerHexRawOffsetTests : IDisposable
{
    private readonly string _tempDir;

    public ViewerHexRawOffsetTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"CSharpFarViewerHex_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void Show_Utf8BomToggleToHexStartsAtPhysicalOffsetZero()
    {
        string path = Path.Combine(_tempDir, "utf8-bom.txt");
        File.WriteAllText(path, "123", new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        var driver = new FakeConsoleDriver(width: 80, height: 10);
        int stage = 0;
        Action<FakeConsoleDriver>? advance = null;
        advance = current =>
        {
            string content = current.GetRegionText(new CSharpFar.Console.Models.Rect(0, 1, 80, 8));
            if (stage == 0 && content.Contains("123", StringComparison.Ordinal))
            {
                current.EnqueueKey(Key(ConsoleKey.F4));
                stage = 1;
            }
            else if (stage == 1 &&
                     current.GetRow(1).Contains("00000000", StringComparison.Ordinal) &&
                     current.GetRow(1).Contains("EF BB BF 31 32 33", StringComparison.Ordinal))
            {
                current.EnqueueKey(Key(ConsoleKey.F10));
                stage = 2;
                return;
            }

            Rearm(current, advance);
        };
        Rearm(driver, advance);

        UiTestCanvas.FileViewerFor(new ScreenRenderer(driver)).Show(path);

        string firstHexRow = driver.GetRow(1);
        Assert.Equal(2, stage);
        Assert.Contains("00000000", firstHexRow);
        Assert.Contains("EF BB BF 31 32 33", firstHexRow);
    }

    [Fact]
    public void Show_HomeInHexReturnsToPhysicalOffsetZero()
    {
        string path = Path.Combine(_tempDir, "utf8-bom-long.txt");
        File.WriteAllText(path, new string('x', 512), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        var driver = new FakeConsoleDriver(width: 80, height: 10);
        int stage = 0;
        Action<FakeConsoleDriver>? advance = null;
        advance = current =>
        {
            string content = current.GetRegionText(new CSharpFar.Console.Models.Rect(0, 1, 80, 8));
            string firstRow = current.GetRow(1);
            if (stage == 0 && content.Contains("xxx", StringComparison.Ordinal))
            {
                current.EnqueueKey(Key(ConsoleKey.F4));
                stage = 1;
            }
            else if (stage == 1 && firstRow.Contains("00000000", StringComparison.Ordinal))
            {
                current.EnqueueKey(Key(ConsoleKey.PageDown));
                stage = 2;
            }
            else if (stage == 2 &&
                     firstRow.Contains("00000080", StringComparison.Ordinal))
            {
                current.EnqueueKey(Key(ConsoleKey.Home));
                stage = 3;
            }
            else if (stage == 3 &&
                     firstRow.Contains("00000000", StringComparison.Ordinal) &&
                     firstRow.Contains("EF BB BF", StringComparison.Ordinal))
            {
                current.EnqueueKey(Key(ConsoleKey.F10));
                stage = 4;
                return;
            }

            Rearm(current, advance);
        };
        Rearm(driver, advance);

        UiTestCanvas.FileViewerFor(new ScreenRenderer(driver)).Show(path);

        string firstHexRow = driver.GetRow(1);
        Assert.Equal(4, stage);
        Assert.Contains("00000000", firstHexRow);
        Assert.Contains("EF BB BF", firstHexRow);
    }

    private static void Rearm(FakeConsoleDriver driver, Action<FakeConsoleDriver>? callback)
    {
        driver.BeforeTryReadInput = callback;
        driver.BeforeReadInput = callback;
    }

    private static ConsoleKeyInfo Key(ConsoleKey key) =>
        new('\0', key, shift: false, alt: false, control: false);
}

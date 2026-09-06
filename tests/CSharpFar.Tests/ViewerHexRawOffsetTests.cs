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
        driver.EnqueueKey(Key(ConsoleKey.F4));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        UiTestCanvas.FileViewerFor(new ScreenRenderer(driver)).Show(path);

        string firstHexRow = driver.GetRow(1);
        Assert.Contains("00000000", firstHexRow);
        Assert.Contains("EF BB BF 31 32 33", firstHexRow);
    }

    [Fact]
    public void Show_HomeInHexReturnsToPhysicalOffsetZero()
    {
        string path = Path.Combine(_tempDir, "utf8-bom-long.txt");
        File.WriteAllText(path, new string('x', 512), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        var driver = new FakeConsoleDriver(width: 80, height: 10);
        driver.EnqueueKey(Key(ConsoleKey.F4));
        driver.EnqueueKey(Key(ConsoleKey.PageDown));
        driver.EnqueueKey(Key(ConsoleKey.Home));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        UiTestCanvas.FileViewerFor(new ScreenRenderer(driver)).Show(path);

        string firstHexRow = driver.GetRow(1);
        Assert.Contains("00000000", firstHexRow);
        Assert.Contains("EF BB BF", firstHexRow);
    }

    private static ConsoleKeyInfo Key(ConsoleKey key) =>
        new('\0', key, shift: false, alt: false, control: false);
}

using System.Reflection;
using CSharpFar.App.Viewer;
using CSharpFar.Console;
using CSharpFar.Tests.Fakes;

namespace CSharpFar.Tests;

public sealed class FileEditorTests : IDisposable
{
    private readonly string _tempDir;

    public FileEditorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"CSharpFarEditorTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            foreach (string path in Directory.GetFiles(_tempDir, "*", SearchOption.AllDirectories))
                File.SetAttributes(path, FileAttributes.Normal);
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public void Show_StaysOpenWhenSaveOnExitFails()
    {
        string filePath = Path.Combine(_tempDir, "readonly.txt");
        File.WriteAllText(filePath, "original");
        File.SetAttributes(filePath, FileAttributes.ReadOnly);

        var driver = new FakeConsoleDriver(80, 25);
        driver.EnqueueKey(new ConsoleKeyInfo('X', ConsoleKey.X, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('S', ConsoleKey.S, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.Enter, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('D', ConsoleKey.D, shift: false, alt: false, control: false));

        ShowFileEditor(new ScreenRenderer(driver), filePath);

        Assert.Throws<InvalidOperationException>(() => driver.ReadKey(intercept: true));
        Assert.Equal("original", File.ReadAllText(filePath));
    }

    private static void ShowFileEditor(ScreenRenderer renderer, string filePath)
    {
        var editorType = typeof(TextFileReader).Assembly.GetType("CSharpFar.App.Editor.FileEditor", throwOnError: true)!;
        var editor = Activator.CreateInstance(
            editorType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: [renderer],
            culture: null)!;

        editorType.GetMethod("Show", BindingFlags.Instance | BindingFlags.Public)!
            .Invoke(editor, [filePath]);
    }
}

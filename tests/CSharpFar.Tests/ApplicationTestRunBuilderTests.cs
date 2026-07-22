using CSharpFar.App;
using CSharpFar.Console;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.History;
using CSharpFar.Core.Models;
using CSharpFar.Tests.Fakes;

namespace CSharpFar.Tests;

public sealed class ApplicationTestRunBuilderTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("CSharpFarBuilder_").FullName;

    [Fact]
    public void Run_DoesNotActivateBarrierOnInitialInputRequestWhileInputIsPending()
    {
        var driver = new FakeConsoleDriver(width: 80, height: 25);
        var app = CreateApp(driver);

        var builder = ApplicationTestRunBuilder
            .For(app, driver)
            .Press(ConsoleKey.F10)
            .ExitWhenApplicationReady();

        var exception = Assert.Throws<InvalidOperationException>(builder.Run);
        Assert.Contains("completed before all scripted steps were activated", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Remaining step: ApplicationReadyBarrierStep", exception.Message, StringComparison.Ordinal);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private Application CreateApp(FakeConsoleDriver driver)
    {
        var fs = new FakeFileSystemService();
        fs.AddDirectory(_root);
        var settings = new AppSettings();
        settings.Panels.LeftStartDirectory = _root;
        settings.Panels.RightStartDirectory = _root;

        return new Application(
            new ScreenRenderer(driver),
            fs,
            new NoOpShellService(),
            new NoOpFileOperationService(),
            new InMemoryHistoryStore(),
            settings);
    }
}

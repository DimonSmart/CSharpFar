using CSharpFar.App;
using CSharpFar.App.Bootstrap;
using CSharpFar.App.Rendering;
using CSharpFar.App.State;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Core.History;
using CSharpFar.Core.Models;
using CSharpFar.Tests.Fakes;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

public sealed class ApplicationUiLayerScopeTests
{
    [Fact]
    public void Dispose_RemovesApplicationOverlaysAndLeavesRootSurfaceRoutable()
    {
        var fixture = Fixture.Create();
        fixture.Services.Composition.Render();

        fixture.Services.ApplicationUiLayers.Dispose();
        fixture.Services.ApplicationUiLayers.Dispose();
        fixture.Services.Composition.Render();

        var input = new KeyConsoleInputEvent(new ConsoleKeyInfo('\0', ConsoleKey.F9, false, false, false));
        UiInputResult result = fixture.Services.Composition.DispatchInput(input);

        Assert.True(result.Handled);
        Assert.True(fixture.Services.ApplicationSurface.TryTakeInput(out var packet));
        Assert.Same(input, packet.Input);
    }

    private sealed record Fixture(FakeConsoleDriver Driver, ApplicationServices Services)
    {
        public static Fixture Create()
        {
            var driver = new FakeConsoleDriver(80, 25);
            var fileSystem = new FakeFileSystemService();
            const string root = @"C:\Root";
            fileSystem.AddDirectory(root);
            var settings = new AppSettings();
            settings.Panels.LeftStartDirectory = root;
            settings.Panels.RightStartDirectory = root;
            var services = ApplicationServicesBuilder.Create(
                new ScreenRenderer(driver), fileSystem, new NoOpShellService(),
                new NoOpFileOperationService(), new InMemoryHistoryStore(), settings,
                enableBuiltInNetworkModules: false);
            _ = new Application(services);
            return new Fixture(driver, services);
        }
    }
}

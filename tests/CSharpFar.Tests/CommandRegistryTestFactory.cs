using CSharpFar.App.Bootstrap;
using CSharpFar.App.Commands;
using CSharpFar.Console;
using CSharpFar.Core.History;
using CSharpFar.Core.Models;
using CSharpFar.Tests.Fakes;

namespace CSharpFar.Tests;

internal static class CommandRegistryTestFactory
{
    public static ApplicationCommandRegistry Create()
    {
        string directory = Path.GetTempPath();
        var fileSystem = new FakeFileSystemService();
        fileSystem.AddDirectory(directory);
        var settings = new AppSettings();
        settings.Panels.LeftStartDirectory = directory;
        settings.Panels.RightStartDirectory = directory;
        return ApplicationServicesBuilder.Create(
            new ScreenRenderer(new FakeConsoleDriver()), fileSystem,
            new NoOpShellService(), new NoOpFileOperationService(),
            new InMemoryHistoryStore(), settings).CommandRegistry;
    }
}

using System.Reflection;
using CSharpFar.App.Bootstrap;
using CSharpFar.App.Commands;
using CSharpFar.App.FunctionKeys;
using CSharpFar.App.DirectoryShortcuts;
using CSharpFar.App.Rendering;
using CSharpFar.Console;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.History;
using CSharpFar.Core.Menu;
using CSharpFar.Core.Models;
using CSharpFar.Tests.Fakes;

namespace CSharpFar.Tests;

public sealed class ApplicationCommandRegistryTests : IDisposable
{
    private readonly string _tempDir;

    public ApplicationCommandRegistryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"CSharpFarCommandRegistry_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void CreateDefault_ContainsHandlersForEveryFunctionKeyAndMenuCommand()
    {
        var registry = ApplicationCommandRegistry.CreateDefault();
        var commandIds = registry.CommandIds.ToHashSet(StringComparer.Ordinal);

        foreach (string commandId in ConstStringValues(typeof(FunctionKeyCommandIds)))
            Assert.Contains(commandId, commandIds);

        foreach (string commandId in ConstStringValues(typeof(MenuCommandIds)))
            Assert.Contains(commandId, commandIds);

        Assert.Contains(ApplicationCommandIds.OpenCurrentItem, commandIds);
        Assert.Contains(DirectoryShortcutCommandIds.Navigate, commandIds);
    }

    [Fact]
    public void CreateDefault_ContainsTerminalDiagnosticsCommand()
    {
        var registry = ApplicationCommandRegistry.CreateDefault();

        Assert.Contains(MenuCommandIds.DiagnosticsPrintTerminalInfo, registry.CommandIds);
    }

    [Theory]
    [InlineData(MenuCommandIds.PanelSetViewMode)]
    [InlineData(MenuCommandIds.PanelSetSortMode)]
    [InlineData(MenuCommandIds.PanelToggleReverseSort)]
    [InlineData(MenuCommandIds.PanelRefresh)]
    public void Execute_MenuCommandWithMissingArgs_ReturnsFailedMenuResult(string commandId)
    {
        var registry = ApplicationCommandRegistry.CreateDefault();
        var context = CreateContext();

        var result = registry.Execute(commandId, context).ToMenuCommandResult();

        Assert.False(result.Success);
        Assert.False(string.IsNullOrWhiteSpace(result.ErrorMessage));
    }

    [Fact]
    public void Execute_UnknownCommand_ReturnsFailedMenuResult()
    {
        var registry = ApplicationCommandRegistry.CreateDefault();
        var context = CreateContext();

        var result = registry.Execute("missing.command", context).ToMenuCommandResult();

        Assert.False(result.Success);
        Assert.Contains("Unsupported", result.ErrorMessage, StringComparison.Ordinal);
    }

    private ApplicationCommandContext CreateContext()
    {
        var fs = new FakeFileSystemService();
        fs.AddDirectory(_tempDir);

        var settings = new AppSettings();
        settings.Panels.LeftStartDirectory = _tempDir;
        settings.Panels.RightStartDirectory = _tempDir;

        var services = ApplicationServicesBuilder.Create(
            new ScreenRenderer(new FakeConsoleDriver()),
            fs,
            new NoOpShellService(),
            new NoOpFileOperationService(),
            new InMemoryHistoryStore(),
            settings);

        return services.CommandContext;
    }

    private static IEnumerable<string> ConstStringValues(Type type) =>
        type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(field => field.IsLiteral && !field.IsInitOnly && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!);
}

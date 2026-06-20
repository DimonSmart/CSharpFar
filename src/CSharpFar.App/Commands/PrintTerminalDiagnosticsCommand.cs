using System.Runtime.InteropServices;
using CSharpFar.App.Rendering;
using CSharpFar.Core.Menu;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Commands;

internal sealed class PrintTerminalDiagnosticsCommand : IApplicationCommand
{
    public string CommandId => MenuCommandIds.DiagnosticsPrintTerminalInfo;

    public bool CanExecute(ApplicationCommandContext context, object? args = null) => true;

    public ApplicationCommandResult Execute(ApplicationCommandContext context, object? args = null)
    {
        var viewport = context.Screen.GetViewport();
        var size = context.Screen.GetSize();
        var terminal = context.GetTerminalDiagnostics();
        string activeDirectory = context.ActiveState.CurrentDirectory;
        string processCurrentDirectory = Environment.CurrentDirectory;
        bool hasVisiblePanels = context.HasVisiblePanels;
        var activeSide = context.ActiveSide;

        context.ExecuteInCurrentConsole(activeDirectory, "diagnostics", () =>
        {
            global::System.Console.WriteLine("CSharpFar diagnostics");
            global::System.Console.WriteLine($"Timestamp: {DateTimeOffset.Now:O}");
            global::System.Console.WriteLine("Process:");
            global::System.Console.WriteLine($"  OS: {RuntimeInformation.OSDescription}");
            global::System.Console.WriteLine($"  Framework: {RuntimeInformation.FrameworkDescription}");
            global::System.Console.WriteLine($"  Process architecture: {RuntimeInformation.ProcessArchitecture}");
            global::System.Console.WriteLine($"  Current directory: {processCurrentDirectory}");
            global::System.Console.WriteLine("Console:");
            global::System.Console.WriteLine(
                $"  Viewport: Left={viewport.Left}, Top={viewport.Top}, Width={viewport.Width}, Height={viewport.Height}");
            global::System.Console.WriteLine($"  Size: Width={size.Width}, Height={size.Height}");
            global::System.Console.WriteLine(
                $"  Cursor: Left={ConsoleValue(() => global::System.Console.CursorLeft)}, Top={ConsoleValue(() => global::System.Console.CursorTop)}");
            global::System.Console.WriteLine(
                $"  Window: Left={ConsoleValue(() => global::System.Console.WindowLeft)}, Top={ConsoleValue(() => global::System.Console.WindowTop)}, Width={ConsoleValue(() => global::System.Console.WindowWidth)}, Height={ConsoleValue(() => global::System.Console.WindowHeight)}");
            global::System.Console.WriteLine(
                $"  Buffer: Width={ConsoleValue(() => global::System.Console.BufferWidth)}, Height={ConsoleValue(() => global::System.Console.BufferHeight)}");
            global::System.Console.WriteLine("CSharpFar:");
            global::System.Console.WriteLine($"  Has visible panels: {hasVisiblePanels}");
            global::System.Console.WriteLine($"  Active side: {activeSide}");
            global::System.Console.WriteLine($"  Command line row: {ApplicationLayoutService.CommandLineRow(size)}");
            global::System.Console.WriteLine($"  Panel height: {ApplicationLayoutService.PanelHeight(size)}");
            WritePanelDiagnostics("Left panel", context.LeftPanel);
            WritePanelDiagnostics("Right panel", context.RightPanel);
            global::System.Console.WriteLine("Terminal mode:");
            global::System.Console.WriteLine($"  Uses terminal screen mode: {terminal.UsesTerminalScreenMode}");
            global::System.Console.WriteLine($"  Terminal screen mode supported: {Value(terminal.IsTerminalScreenModeSupported)}");
            global::System.Console.WriteLine($"  Application screen active: {Value(terminal.IsApplicationScreenActive)}");
            global::System.Console.WriteLine($"  Uses legacy console mode: {terminal.UsesLegacyConsoleMode}");
        });

        return ApplicationCommandResult.Rendered();
    }

    private static string ConsoleValue(Func<int> read)
    {
        try
        {
            return read().ToString();
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or
                                   PlatformNotSupportedException or ArgumentOutOfRangeException)
        {
            return "unavailable";
        }
    }

    private static string Value(bool? value) =>
        value?.ToString() ?? "unavailable";

    private static void WritePanelDiagnostics(string heading, FilePanelState panel)
    {
        global::System.Console.WriteLine($"  {heading}:");
        global::System.Console.WriteLine($"    Source id: {panel.SourceId}");
        global::System.Console.WriteLine($"    Source path: {panel.SourcePath}");
        global::System.Console.WriteLine($"    Current directory: {panel.CurrentDirectory}");
        global::System.Console.WriteLine($"    Items count: {panel.Items.Count}");
        global::System.Console.WriteLine($"    Cursor index: {panel.CursorIndex}");
        global::System.Console.WriteLine($"    Scroll offset: {panel.ScrollOffset}");
        global::System.Console.WriteLine($"    Has load error: {panel.LoadError is not null}");

        if (panel.LoadError is not { } loadError)
            return;

        global::System.Console.WriteLine($"    Load error: {loadError.Message}");
        global::System.Console.WriteLine($"    Retry source id: {loadError.RetryLocation.SourceId}");
        global::System.Console.WriteLine($"    Retry source path: {loadError.RetryLocation.SourcePath}");
    }
}

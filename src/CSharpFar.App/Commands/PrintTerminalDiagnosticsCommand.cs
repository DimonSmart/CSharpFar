using System.Runtime.InteropServices;
using CSharpFar.App.Rendering;
using CSharpFar.App.State;
using CSharpFar.Console.Input;
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
        var workspaceMode = context.IsPanelsMode
            ? ApplicationWorkspaceMode.Panels
            : ApplicationWorkspaceMode.HiddenCommandLine;
        var activeSide = context.ActiveSide;

        context.ExecuteInCurrentConsole(activeDirectory, "diagnostics", () =>
        {
            global::System.Console.WriteLine("CSharpFar diagnostics");
            global::System.Console.WriteLine($"Timestamp: {DateTimeOffset.Now:O}");
            WriteEnvironmentDiagnostics(global::System.Console.Out);
            WritePrivilegeDiagnostics(global::System.Console.Out);
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
            global::System.Console.WriteLine($"  Workspace mode: {workspaceMode}");
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
            WriteConsoleInputDiagnostics(global::System.Console.Out, terminal);
            WriteModifierTrackingDiagnostics(global::System.Console.Out, terminal.ModifierKeyTracking);
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

    internal static void WriteEnvironmentDiagnostics(TextWriter writer)
    {
        writer.WriteLine("Environment:");
        writer.WriteLine($"  OS description: {RuntimeInformation.OSDescription}");
        writer.WriteLine($"  OS architecture: {RuntimeInformation.OSArchitecture}");
        writer.WriteLine($"  Process architecture: {RuntimeInformation.ProcessArchitecture}");
        writer.WriteLine($"  Framework: {RuntimeInformation.FrameworkDescription}");
        writer.WriteLine($"  Is Windows: {OperatingSystem.IsWindows()}");
        writer.WriteLine($"  Is Linux: {OperatingSystem.IsLinux()}");
        writer.WriteLine($"  Is macOS: {OperatingSystem.IsMacOS()}");
    }

    internal static void WritePrivilegeDiagnostics(TextWriter writer)
    {
        writer.WriteLine("Privilege:");
        if (OperatingSystem.IsWindows())
        {
            writer.WriteLine("  Unix effective uid: not applicable");
            writer.WriteLine("  Unix is root: not applicable");
            writer.WriteLine($"  SUDO_USER: {EnvironmentValue("SUDO_USER")}");
            writer.WriteLine($"  SUDO_UID: {EnvironmentValue("SUDO_UID")}");
            writer.WriteLine($"  SUDO_GID: {EnvironmentValue("SUDO_GID")}");
            writer.WriteLine("  Running via sudo: not applicable");
            writer.WriteLine("  Windows elevated/admin: not checked");
            return;
        }

        uint? effectiveUid = TryGetEffectiveUserId();
        bool isRoot = effectiveUid == 0;
        string? sudoUser = Environment.GetEnvironmentVariable("SUDO_USER");
        string? sudoUid = Environment.GetEnvironmentVariable("SUDO_UID");

        writer.WriteLine($"  Unix effective uid: {effectiveUid?.ToString() ?? "unavailable"}");
        writer.WriteLine($"  Unix is root: {(effectiveUid.HasValue ? isRoot.ToString() : "unavailable")}");
        writer.WriteLine($"  SUDO_USER: {EnvironmentValue("SUDO_USER")}");
        writer.WriteLine($"  SUDO_UID: {EnvironmentValue("SUDO_UID")}");
        writer.WriteLine($"  SUDO_GID: {EnvironmentValue("SUDO_GID")}");
        writer.WriteLine($"  Running via sudo: {isRoot && !string.IsNullOrEmpty(sudoUser) && !string.IsNullOrEmpty(sudoUid)}");
        writer.WriteLine("  Windows elevated/admin: not applicable");
    }

    internal static void WriteConsoleInputDiagnostics(TextWriter writer, TerminalSurfaceDiagnostics terminal)
    {
        writer.WriteLine("Console input:");
        writer.WriteLine($"  Console driver: {terminal.ConsoleDriver}");
        writer.WriteLine($"  Input backend: {terminal.InputBackend}");
        writer.WriteLine($"  Mouse tracking enabled: {Value(terminal.MouseTrackingEnabled)}");
    }

    internal static void WriteModifierTrackingDiagnostics(TextWriter writer, ModifierKeyTrackingSnapshot snapshot)
    {
        writer.WriteLine("Modifier tracking:");
        writer.WriteLine($"  Backend: {snapshot.BackendName}");
        writer.WriteLine($"  Platform supported: {snapshot.IsPlatformSupported}");
        writer.WriteLine($"  Enabled: {snapshot.IsEnabled}");
        writer.WriteLine($"  Can track Shift-only: {snapshot.CanTrackShiftOnly}");
        writer.WriteLine($"  Status: {snapshot.Status}");
        writer.WriteLine($"  Failure reason: {snapshot.FailureReason ?? "none"}");
        writer.WriteLine($"  Devices scanned: {snapshot.Devices.Count}");
        writer.WriteLine($"  Readable devices: {snapshot.Devices.Count(static device => device.IsReadable)}");
        writer.WriteLine($"  Shift-capable devices: {snapshot.Devices.Count(static device => device.IsReadable && device.HasShiftCapability)}");
        writer.WriteLine("  Selected devices:");

        foreach (var device in snapshot.Devices
                     .Where(static device => device.IsReadable && device.HasShiftCapability)
                     .OrderBy(static device => device.Path, StringComparer.Ordinal))
        {
            writer.WriteLine($"    {device.Path}  {device.Name ?? "unknown"}  shift={device.HasShiftCapability}");
        }

        if (!snapshot.CanTrackShiftOnly)
        {
            writer.WriteLine(
                "  Hint: The app continues without Shift-only tracking. Do not run CSharpFar with sudo just for normal usage unless you intentionally want elevated file-manager permissions.");
        }
    }

    private static string EnvironmentValue(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : "not set";

    private static uint? TryGetEffectiveUserId()
    {
        if (OperatingSystem.IsWindows())
            return null;

        try
        {
            return geteuid();
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    [DllImport("libc")]
    private static extern uint geteuid();

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

using CSharpFar.App.Commands;
using CSharpFar.App.Rendering;
using CSharpFar.Console.Input;

namespace CSharpFar.Tests;

public sealed class TerminalDiagnosticsTests
{
    [Fact]
    public void DiagnosticDump_IncludesOsRuntimeInformation()
    {
        using var writer = new StringWriter();

        PrintTerminalDiagnosticsCommand.WriteEnvironmentDiagnostics(writer);

        string output = writer.ToString();
        Assert.Contains("OS description:", output, StringComparison.Ordinal);
        Assert.Contains("OS architecture:", output, StringComparison.Ordinal);
        Assert.Contains("Process architecture:", output, StringComparison.Ordinal);
        Assert.Contains("Framework:", output, StringComparison.Ordinal);
    }

    [Fact]
    public void DiagnosticDump_IncludesUnixPrivilegeInformation()
    {
        using var writer = new StringWriter();

        PrintTerminalDiagnosticsCommand.WritePrivilegeDiagnostics(writer);

        string output = writer.ToString();
        Assert.Contains("Unix effective uid:", output, StringComparison.Ordinal);
        Assert.Contains("Unix is root:", output, StringComparison.Ordinal);
        Assert.Contains("Windows elevated/admin:", output, StringComparison.Ordinal);
    }

    [Fact]
    public void DiagnosticDump_IncludesSudoEnvironmentInformation()
    {
        using var writer = new StringWriter();

        PrintTerminalDiagnosticsCommand.WritePrivilegeDiagnostics(writer);

        string output = writer.ToString();
        Assert.Contains("SUDO_USER:", output, StringComparison.Ordinal);
        Assert.Contains("SUDO_UID:", output, StringComparison.Ordinal);
        Assert.Contains("SUDO_GID:", output, StringComparison.Ordinal);
        Assert.Contains("Running via sudo:", output, StringComparison.Ordinal);
    }

    [Fact]
    public void DiagnosticDump_IncludesShiftTrackingStatusEnabled()
    {
        using var writer = new StringWriter();

        PrintTerminalDiagnosticsCommand.WriteModifierTrackingDiagnostics(
            writer,
            new ModifierKeyTrackingSnapshot(
                "linux-evdev",
                IsPlatformSupported: true,
                IsEnabled: true,
                CanTrackShiftOnly: true,
                Status: ModifierKeyTrackingStatus.Enabled,
                FailureReason: null,
                Devices:
                [
                    new ModifierKeyDeviceSnapshot("/dev/input/event3", "AT keyboard", true, true, null),
                ]));

        string output = writer.ToString();
        Assert.Contains("Can track Shift-only: True", output, StringComparison.Ordinal);
        Assert.Contains("/dev/input/event3", output, StringComparison.Ordinal);
    }

    [Fact]
    public void DiagnosticDump_IncludesShiftTrackingPermissionDenied()
    {
        using var writer = new StringWriter();

        PrintTerminalDiagnosticsCommand.WriteModifierTrackingDiagnostics(
            writer,
            new ModifierKeyTrackingSnapshot(
                "linux-evdev",
                IsPlatformSupported: true,
                IsEnabled: false,
                CanTrackShiftOnly: false,
                Status: ModifierKeyTrackingStatus.PermissionDenied,
                FailureReason: "/dev/input/event3: permission denied",
                Devices:
                [
                    new ModifierKeyDeviceSnapshot("/dev/input/event3", null, false, false, "permission denied"),
                ]));

        string output = writer.ToString();
        Assert.Contains("Status: PermissionDenied", output, StringComparison.Ordinal);
        Assert.Contains("permission denied", output, StringComparison.Ordinal);
        Assert.Contains("continues without Shift-only tracking", output, StringComparison.Ordinal);
    }

    [Fact]
    public void DiagnosticDump_IncludesShiftTrackingUnavailableWhenPlatformNotSupported()
    {
        using var writer = new StringWriter();

        PrintTerminalDiagnosticsCommand.WriteModifierTrackingDiagnostics(
            writer,
            new ModifierKeyTrackingSnapshot(
                "none",
                IsPlatformSupported: false,
                IsEnabled: false,
                CanTrackShiftOnly: false,
                Status: ModifierKeyTrackingStatus.PlatformNotSupported,
                FailureReason: null,
                Devices: []));

        string output = writer.ToString();
        Assert.Contains("Platform supported: False", output, StringComparison.Ordinal);
        Assert.Contains("Status: PlatformNotSupported", output, StringComparison.Ordinal);
    }

    [Fact]
    public void DiagnosticDump_IncludesConsoleInputInformation()
    {
        using var writer = new StringWriter();

        PrintTerminalDiagnosticsCommand.WriteConsoleInputDiagnostics(
            writer,
            new TerminalSurfaceDiagnostics(
                UsesTerminalScreenMode: true,
                IsTerminalScreenModeSupported: true,
                IsApplicationScreenActive: true,
                UsesLegacyConsoleMode: false,
                ConsoleDriver: "AnsiTerminalConsoleDriver",
                InputBackend: "raw-vt",
                MouseTrackingEnabled: true,
                ModifierKeyTracking: new ModifierKeyTrackingSnapshot(
                    "linux-evdev",
                    true,
                    true,
                    true,
                    ModifierKeyTrackingStatus.Enabled,
                    null,
                    [])));

        string output = writer.ToString();
        Assert.Contains("Console driver: AnsiTerminalConsoleDriver", output, StringComparison.Ordinal);
        Assert.Contains("Input backend: raw-vt", output, StringComparison.Ordinal);
        Assert.Contains("Mouse tracking enabled: True", output, StringComparison.Ordinal);
    }
}

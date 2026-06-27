using CSharpFar.Console.Input;

namespace CSharpFar.App.Rendering;

internal sealed record TerminalSurfaceDiagnostics(
    bool UsesTerminalScreenMode,
    bool? IsTerminalScreenModeSupported,
    bool? IsApplicationScreenActive,
    bool UsesLegacyConsoleMode,
    string ConsoleDriver,
    string InputBackend,
    bool? MouseTrackingEnabled,
    ModifierKeyTrackingSnapshot ModifierKeyTracking);

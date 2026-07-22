using CSharpFar.Console.Input;
using CSharpFar.Console.Models;

namespace CSharpFar.App.Rendering;

internal sealed record TerminalSurfaceSnapshot(ConsoleViewport Viewport, ConsoleSize Size);

internal sealed record TerminalSurfaceDiagnostics(
    bool UsesTerminalScreenMode,
    bool? IsTerminalScreenModeSupported,
    bool? IsApplicationScreenActive,
    bool UsesLegacyConsoleMode,
    string ConsoleDriver,
    string InputBackend,
    bool? MouseTrackingEnabled,
    ModifierKeyTrackingSnapshot ModifierKeyTracking);

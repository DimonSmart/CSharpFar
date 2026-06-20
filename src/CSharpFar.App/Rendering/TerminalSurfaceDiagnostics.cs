namespace CSharpFar.App.Rendering;

internal sealed record TerminalSurfaceDiagnostics(
    bool UsesTerminalScreenMode,
    bool? IsTerminalScreenModeSupported,
    bool? IsApplicationScreenActive,
    bool UsesLegacyConsoleMode);

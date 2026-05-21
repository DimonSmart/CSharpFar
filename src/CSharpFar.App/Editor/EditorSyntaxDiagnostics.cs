namespace CSharpFar.App.Editor;

public sealed class EditorSyntaxDiagnostics
{
    public static EditorSyntaxDiagnostics Disabled(string reason) =>
        new()
        {
            IsEnabled = false,
            IsFallback = true,
            StatusText = reason,
        };

    public static EditorSyntaxDiagnostics Plain(string reason) =>
        new()
        {
            IsEnabled = true,
            IsFallback = true,
            StatusText = reason,
        };

    public static EditorSyntaxDiagnostics Active(
        string grammar,
        string theme,
        string colorMode,
        string? lastError = null) =>
        new()
        {
            IsEnabled = true,
            IsFallback = false,
            SelectedGrammar = grammar,
            SelectedTheme = theme,
            ColorMode = colorMode,
            LastError = lastError,
            StatusText = $"Syn:{grammar} {theme}",
        };

    public bool IsEnabled { get; init; }
    public bool IsFallback { get; init; }
    public string? SelectedGrammar { get; init; }
    public string? SelectedTheme { get; init; }
    public string? ColorMode { get; init; }
    public string? LastError { get; init; }
    public string StatusText { get; init; } = "Syn:plain";
}

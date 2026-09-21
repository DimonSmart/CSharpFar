namespace CSharpFar.App.Diagnostics;

internal sealed class DisabledDiagnosticLog : IDiagnosticLog
{
    public static DisabledDiagnosticLog Instance { get; } = new();

    private DisabledDiagnosticLog()
    {
    }

    public bool IsEnabled => false;

    public void Write(DiagnosticCategory category, string message)
    {
    }

    public void WriteException(
        DiagnosticCategory category,
        Exception exception,
        string? context = null)
    {
    }

    public IReadOnlyList<DiagnosticEntry> GetSnapshot() => [];

    public void Clear()
    {
    }
}

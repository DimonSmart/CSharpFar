namespace CSharpFar.App.Diagnostics;

internal interface IDiagnosticLog
{
    bool IsEnabled { get; }

    void Write(DiagnosticCategory category, string message);

    void WriteException(
        DiagnosticCategory category,
        Exception exception,
        string? context = null);

    IReadOnlyList<DiagnosticEntry> GetSnapshot();

    void Clear();
}

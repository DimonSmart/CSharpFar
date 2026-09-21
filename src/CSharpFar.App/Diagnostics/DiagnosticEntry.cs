namespace CSharpFar.App.Diagnostics;

internal sealed record DiagnosticExceptionInfo(
    string Type,
    string Message,
    string? StackTrace,
    IReadOnlyList<DiagnosticExceptionInfo> InnerExceptions);

internal sealed record DiagnosticEntry(
    DateTimeOffset Timestamp,
    DiagnosticCategory Category,
    string Message,
    DiagnosticExceptionInfo? ExceptionInfo = null);

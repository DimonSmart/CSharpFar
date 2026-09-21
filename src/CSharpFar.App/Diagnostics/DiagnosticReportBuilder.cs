using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using CSharpFar.App.Rendering;
using CSharpFar.App.Updates;

namespace CSharpFar.App.Diagnostics;

internal sealed class DiagnosticReportBuilder
{
    private readonly Func<DateTimeOffset> _clock;

    public DiagnosticReportBuilder(Func<DateTimeOffset>? clock = null) =>
        _clock = clock ?? (() => DateTimeOffset.Now);

    public string Build(
        bool diagnosticsEnabled,
        IReadOnlyList<DiagnosticEntry> entries,
        ApplicationVersionInfo versionInfo,
        TerminalSurfaceDiagnostics? terminal)
    {
        try
        {
            return BuildCore(
                diagnosticsEnabled,
                entries ?? [],
                versionInfo ?? throw new ArgumentNullException(nameof(versionInfo)),
                terminal);
        }
        catch (Exception)
        {
            return string.Join(
                Environment.NewLine,
                "CSharpFar Diagnostic Report",
                $"Diagnostics mode: {(diagnosticsEnabled ? "ON" : "OFF")}",
                "Report generation failed safely.");
        }
    }

    private string BuildCore(
        bool diagnosticsEnabled,
        IReadOnlyList<DiagnosticEntry> entries,
        ApplicationVersionInfo versionInfo,
        TerminalSurfaceDiagnostics? terminal)
    {
        DateTimeOffset generated = _clock();
        var builder = new StringBuilder();

        builder.AppendLine("CSharpFar Diagnostic Report");
        builder.AppendLine($"Generated: {generated:yyyy-MM-dd HH:mm:ss.fff zzz}");
        builder.AppendLine($"Diagnostics mode: {(diagnosticsEnabled ? "ON" : "OFF")}");
        builder.AppendLine();

        builder.AppendLine("Application");
        builder.AppendLine("-----------");
        builder.AppendLine($"Version: {Safe(versionInfo.DisplayVersion)}");
        builder.AppendLine($"Informational version: {Safe(versionInfo.InformationalVersion)}");
        builder.AppendLine($"Assembly version: {versionInfo.AssemblyVersion?.ToString() ?? "unknown"}");
        builder.AppendLine($"Comparable version: {versionInfo.ComparableVersion?.ToString() ?? "unavailable"}");
        builder.AppendLine();

        builder.AppendLine("Environment");
        builder.AppendLine("-----------");
        builder.AppendLine($"OS: {Safe(RuntimeInformation.OSDescription)}");
        builder.AppendLine($"OS architecture: {RuntimeInformation.OSArchitecture}");
        builder.AppendLine($"Process architecture: {RuntimeInformation.ProcessArchitecture}");
        builder.AppendLine($"Runtime: {Safe(RuntimeInformation.FrameworkDescription)}");
        builder.AppendLine($"Process ID: {Environment.ProcessId}");
        builder.AppendLine($"Process start time: {ProcessStartTime()}");
        builder.AppendLine($"Current directory: {CurrentDirectory()}");
        builder.AppendLine();

        builder.AppendLine("Terminal");
        builder.AppendLine("--------");
        if (terminal is null)
        {
            builder.AppendLine("Terminal information: unavailable");
        }
        else
        {
            builder.AppendLine($"Console driver: {Safe(terminal.ConsoleDriver)}");
            builder.AppendLine($"Input backend: {Safe(terminal.InputBackend)}");
            builder.AppendLine($"Uses terminal screen mode: {terminal.UsesTerminalScreenMode}");
            builder.AppendLine($"Terminal screen mode supported: {Value(terminal.IsTerminalScreenModeSupported)}");
            builder.AppendLine($"Application screen active: {Value(terminal.IsApplicationScreenActive)}");
            builder.AppendLine($"Mouse tracking enabled: {Value(terminal.MouseTrackingEnabled)}");
            builder.AppendLine($"Modifier tracking backend: {Safe(terminal.ModifierKeyTracking.BackendName)}");
            builder.AppendLine($"Modifier tracking status: {terminal.ModifierKeyTracking.Status}");
        }
        builder.AppendLine();

        builder.AppendLine("Recent events");
        builder.AppendLine("-------------");
        if (!diagnosticsEnabled)
        {
            builder.AppendLine("Runtime event tracing is disabled.");
            builder.AppendLine();
            builder.AppendLine("Restart CSharpFar with:");
            builder.AppendLine("csharpfar --diagnostics");
        }
        else if (entries.Count == 0)
        {
            builder.AppendLine("(no diagnostic events)");
        }
        else
        {
            foreach (DiagnosticEntry entry in entries)
            {
                builder.AppendLine(
                    $"{entry.Timestamp:HH:mm:ss.fff} [{entry.Category}] {Safe(entry.Message)}");
                if (entry.ExceptionInfo is not null)
                    AppendException(builder, entry.ExceptionInfo, "  ");
            }
        }

        return builder.ToString().TrimEnd();
    }

    private static void AppendException(
        StringBuilder builder,
        DiagnosticExceptionInfo exception,
        string indent)
    {
        builder.AppendLine($"{indent}{Safe(exception.Type)}: {Safe(exception.Message)}");
        if (!string.IsNullOrWhiteSpace(exception.StackTrace))
        {
            foreach (string line in exception.StackTrace.Split(
                         ['\r', '\n'],
                         StringSplitOptions.RemoveEmptyEntries))
            {
                builder.AppendLine($"{indent}{Safe(line)}");
            }
        }

        foreach (DiagnosticExceptionInfo inner in exception.InnerExceptions)
        {
            builder.AppendLine($"{indent}Inner exception:");
            AppendException(builder, inner, indent + "  ");
        }
    }

    private static string CurrentDirectory()
    {
        try
        {
            return Safe(DiagnosticSanitizer.RedactHomePath(Environment.CurrentDirectory));
        }
        catch (Exception)
        {
            return "unavailable";
        }
    }

    private static string ProcessStartTime()
    {
        try
        {
            using Process process = Process.GetCurrentProcess();
            return new DateTimeOffset(process.StartTime).ToString("yyyy-MM-dd HH:mm:ss.fff zzz");
        }
        catch (Exception)
        {
            return "unavailable";
        }
    }

    private static string Safe(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? "unavailable"
            : DiagnosticSanitizer.SanitizeText(value);

    private static string Value(bool? value) => value?.ToString() ?? "unavailable";
}

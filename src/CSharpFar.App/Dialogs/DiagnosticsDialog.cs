using CSharpFar.App.Diagnostics;
using CSharpFar.App.Rendering;
using CSharpFar.App.Updates;
using CSharpFar.Ui;

namespace CSharpFar.App.Dialogs;

internal sealed class DiagnosticsDialog
{
    private readonly DialogService _dialogs;
    private readonly ITextClipboard _clipboard;
    private readonly IDiagnosticLog _diagnosticLog;
    private readonly DiagnosticReportBuilder _reportBuilder;
    private readonly ApplicationVersionInfo _versionInfo;
    private readonly Func<TerminalSurfaceDiagnostics?> _terminalDiagnostics;

    public DiagnosticsDialog(
        DialogService dialogs,
        ITextClipboard clipboard,
        IDiagnosticLog diagnosticLog,
        DiagnosticReportBuilder reportBuilder,
        ApplicationVersionInfo versionInfo,
        Func<TerminalSurfaceDiagnostics?> terminalDiagnostics)
    {
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _clipboard = clipboard ?? throw new ArgumentNullException(nameof(clipboard));
        _diagnosticLog = diagnosticLog ?? throw new ArgumentNullException(nameof(diagnosticLog));
        _reportBuilder = reportBuilder ?? throw new ArgumentNullException(nameof(reportBuilder));
        _versionInfo = versionInfo ?? throw new ArgumentNullException(nameof(versionInfo));
        _terminalDiagnostics = terminalDiagnostics ?? throw new ArgumentNullException(nameof(terminalDiagnostics));
    }

    public void Show()
    {
        string report = BuildReport();
        IReadOnlyList<string> lines = ReportLines(report);

        _ = _dialogs.List(new ListDialogOptions<string, bool>
        {
            Title = "Diagnostics",
            Items = () => lines,
            ItemText = static line => line,
            Actions =
            [
                DialogButton.Action("copy", "Copy", 'C'),
                DialogButton.Action("clear", "Clear", 'L'),
                DialogButton.Action("close", "Close", 'O'),
            ],
            DialogWidth = 100,
            MinDialogWidth = 60,
            MaxVisibleRows = 22,
            DefaultItemActionId = "copy",
            Cancel = () => false,
            HandleAction = action =>
            {
                switch (action.ActionId)
                {
                    case "copy":
                        bool copied = TryCopyReport(_clipboard, report);
                        _dialogs.Message(
                            "Diagnostics",
                            copied
                                ? "Diagnostic report copied to clipboard."
                                : "Unable to copy diagnostic report.");
                        return DialogOutcome<bool>.ContinueOpen();

                    case "clear":
                        _diagnosticLog.Clear();
                        if (_diagnosticLog.IsEnabled)
                        {
                            _diagnosticLog.Write(
                                DiagnosticCategory.Application,
                                "Diagnostics log cleared.");
                        }

                        report = BuildReport();
                        lines = ReportLines(report);
                        return DialogOutcome<bool>.RefreshOpen();

                    case "close":
                        return DialogOutcome<bool>.Complete(false);

                    default:
                        return DialogOutcome<bool>.ContinueOpen();
                }
            },
        });
    }

    internal static bool TryCopyReport(ITextClipboard clipboard, string report)
    {
        try
        {
            return clipboard.TrySetText(report);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private string BuildReport()
    {
        TerminalSurfaceDiagnostics? terminal = null;
        try
        {
            terminal = _terminalDiagnostics();
        }
        catch (Exception)
        {
        }

        IReadOnlyList<DiagnosticEntry> entries;
        try
        {
            entries = _diagnosticLog.GetSnapshot();
        }
        catch (Exception)
        {
            entries = [];
        }

        return _reportBuilder.Build(
            _diagnosticLog.IsEnabled,
            entries,
            _versionInfo,
            terminal);
    }

    private static IReadOnlyList<string> ReportLines(string report) =>
        report.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n');
}

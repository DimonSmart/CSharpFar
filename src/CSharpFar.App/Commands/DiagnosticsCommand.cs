using CSharpFar.App.Dialogs;
using CSharpFar.App.Diagnostics;
using CSharpFar.App.Rendering;
using CSharpFar.App.Updates;
using CSharpFar.Ui;
namespace CSharpFar.App.Commands;

internal sealed class DiagnosticsCommand(DialogService dialogs, ITextClipboard clipboard, IDiagnosticLog diagnosticLog, TerminalSurfaceController terminalSurface) : IApplicationCommand
{
    public string CommandId => ApplicationCommandIds.Diagnostics;

    public bool CanExecute(object? args = null) => true;

    public ApplicationCommandResult Execute(object? args = null)
    {
        new DiagnosticsDialog(dialogs, clipboard, diagnosticLog, new DiagnosticReportBuilder(),
            ApplicationVersionProvider.GetVersionInfo(), () => terminalSurface.GetDiagnostics()).Show();
        return ApplicationCommandResult.Rendered();
    }
}

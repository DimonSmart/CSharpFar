using CSharpFar.App.Updates;
using CSharpFar.Ui;

namespace CSharpFar.App.Dialogs;

internal sealed class ApplicationUpdateDialogModel
{
    private readonly object _sync = new();
    private string _message = "Preparing update...";
    private bool _canCancel = true;
    private ApplicationUpdateInstallResult? _result;

    public void Report(ApplicationUpdateProgress progress)
    {
        lock (_sync)
        {
            _message = progress.Message;
            _canCancel = progress.CanCancel;
        }
    }

    public void Complete(ApplicationUpdateInstallResult result)
    {
        lock (_sync)
        {
            _result = result;
            _message = result.Message;
            _canCancel = false;
        }
    }

    public bool CanCancel
    {
        get
        {
            lock (_sync)
                return _canCancel && _result is null;
        }
    }

    public OperationDialogState<string> Snapshot()
    {
        lock (_sync)
        {
            var rows = new List<FormRow>
            {
                FormControls.Label(_message),
            };

            if (_result?.ManualCommand is { Length: > 0 } manualCommand)
            {
                rows.Add(FormControls.Label(string.Empty));
                rows.Add(FormControls.Label("Run manually:"));
                rows.Add(FormControls.Label(manualCommand));
            }

            IReadOnlyList<DialogButton> buttons = _result is null
                ?
                [
                    new DialogButton(
                        "cancel",
                        "Cancel",
                        'C',
                        IsEnabled: _canCancel,
                        Role: DialogButtonRole.Cancel),
                ]
                :
                [
                    new DialogButton(
                        "close",
                        "Close",
                        'C',
                        IsDefault: true,
                        Role: DialogButtonRole.Cancel),
                ];

            return new OperationDialogState<string>(rows: rows, buttons: buttons);
        }
    }
}

internal sealed class ApplicationUpdateDialog
{
    private readonly DialogService _dialogs;
    private readonly IApplicationUpdateInstaller _installer;

    public ApplicationUpdateDialog(
        DialogService dialogs,
        IApplicationUpdateInstaller installer)
    {
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _installer = installer ?? throw new ArgumentNullException(nameof(installer));
    }

    public bool Show(ReleaseVersion expectedVersion)
    {
        var model = new ApplicationUpdateDialogModel();
        var progress = new InlineProgress<ApplicationUpdateProgress>(model.Report);

        return _dialogs.Operation(new OperationDialogDefinition<string, ApplicationUpdateInstallResult, bool>
        {
            Title = "Updating CSharpFar",
            PreferredWidth = 82,
            PreferredHeight = 11,
            MinWidth = 46,
            MinHeight = 8,
            RefreshInterval = TimeSpan.FromMilliseconds(100),
            Operation = cancellationToken =>
                _installer.InstallAsync(expectedVersion, progress, cancellationToken),
            Synchronize = model.Snapshot,
            HandleOperationCompleted = result =>
            {
                if (result.ShouldExitCurrentProcess)
                    return OperationDialogOutcome<bool>.Complete(true);

                model.Complete(result);
                return OperationDialogOutcome<bool>.ContinueChanged;
            },
            HandleCommand = action =>
                action.ActionId == "close"
                    ? OperationDialogOutcome<bool>.Complete(false)
                    : OperationDialogOutcome<bool>.ContinueNoChange,
            HandleCancel = () =>
                model.CanCancel
                    ? OperationDialogOutcome<bool>.RequestCancellation
                    : OperationDialogOutcome<bool>.ContinueNoChange,
            Complete = result => result.ShouldExitCurrentProcess,
        });
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        private readonly Action<T> _report = report ?? throw new ArgumentNullException(nameof(report));

        public void Report(T value) => _report(value);
    }
}

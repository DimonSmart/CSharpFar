using CSharpFar.App.Updates;
using CSharpFar.Core.Abstractions;
using CSharpFar.Ui;

namespace CSharpFar.App.Dialogs;

internal enum AboutUpdateState
{
    Checking,
    UpToDate,
    UpdateAvailable,
    Unavailable,
}

internal sealed class AboutDialogModel
{
    public AboutDialogModel(string currentVersion) =>
        CurrentVersion = currentVersion;

    public string CurrentVersion { get; }
    public AboutUpdateState State { get; private set; } = AboutUpdateState.Checking;
    public ReleaseVersion? LatestVersion { get; private set; }
    public Uri? ReleaseUri { get; private set; }

    public void Apply(UpdateCheckResult result)
    {
        State = result.Status switch
        {
            UpdateCheckStatus.UpToDate => AboutUpdateState.UpToDate,
            UpdateCheckStatus.UpdateAvailable => AboutUpdateState.UpdateAvailable,
            _ => AboutUpdateState.Unavailable,
        };
        LatestVersion = result.LatestVersion;
        ReleaseUri = State == AboutUpdateState.UpdateAvailable ? result.ReleaseUri : null;
    }

    public OperationDialogState<string> Snapshot()
    {
        var rows = new List<FormRow>
        {
            FormControls.Label("CSharpFar"),
            FormControls.Label(string.Empty),
            FormControls.Label($"Version: {CurrentVersion}"),
            FormControls.Label(string.Empty),
        };

        switch (State)
        {
            case AboutUpdateState.Checking:
                rows.Add(FormControls.Label("Checking for updates..."));
                break;
            case AboutUpdateState.UpToDate:
                rows.Add(FormControls.Label("You are up to date."));
                rows.Add(FormControls.Label(
                    $"Latest version: {LatestVersion?.ToString() ?? CurrentVersion}"));
                break;
            case AboutUpdateState.UpdateAvailable:
                rows.Add(FormControls.Label($"New version {LatestVersion} is available."));
                break;
            default:
                rows.Add(FormControls.Label("Unable to check for updates."));
                break;
        }

        IReadOnlyList<DialogButton> buttons = State == AboutUpdateState.UpdateAvailable &&
                                              ReleaseUri is not null
            ?
            [
                DialogButton.Action("open-release", "Open release page", 'O'),
                new DialogButton("close", "Close", 'C', Role: DialogButtonRole.Cancel),
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

internal sealed class AboutDialog
{
    private readonly DialogService _dialogs;
    private readonly IUriLauncher _uriLauncher;
    private readonly UpdateCheckService _updateCheckService;
    private readonly ApplicationVersionInfo _versionInfo;

    public AboutDialog(
        DialogService dialogs,
        IUriLauncher uriLauncher,
        UpdateCheckService updateCheckService,
        ApplicationVersionInfo versionInfo)
    {
        _dialogs = dialogs;
        _uriLauncher = uriLauncher;
        _updateCheckService = updateCheckService;
        _versionInfo = versionInfo;
    }

    public void Show()
    {
        var model = new AboutDialogModel(_versionInfo.AboutVersion);

        _ = _dialogs.Operation(new OperationDialogDefinition<string, UpdateCheckResult, bool>
        {
            Title = "About",
            PreferredWidth = 58,
            PreferredHeight = 11,
            MinWidth = 40,
            MinHeight = 8,
            RefreshInterval = TimeSpan.FromDays(1),
            Operation = cancellationToken =>
                _updateCheckService.CheckAsync(_versionInfo.ComparableVersion, cancellationToken),
            Synchronize = model.Snapshot,
            HandleOperationCompleted = result =>
            {
                model.Apply(result);
                return OperationDialogOutcome<bool>.ContinueChanged;
            },
            HandleCommand = action => HandleCommand(action.ActionId, model),
            HandleCancel = () => OperationDialogOutcome<bool>.Complete(false),
            Complete = _ => false,
        });
    }

    private OperationDialogOutcome<bool> HandleCommand(string command, AboutDialogModel model)
    {
        if (command == "close")
            return OperationDialogOutcome<bool>.Complete(false);

        if (command != "open-release" || model.ReleaseUri is not { } releaseUri)
            return OperationDialogOutcome<bool>.ContinueNoChange;

        try
        {
            _uriLauncher.Open(releaseUri);
        }
        catch (Exception)
        {
            _dialogs.Message("About", "Unable to open release page.");
        }

        return OperationDialogOutcome<bool>.ContinueNoChange;
    }
}

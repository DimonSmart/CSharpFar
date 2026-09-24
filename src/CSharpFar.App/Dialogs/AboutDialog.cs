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

internal sealed record AboutCheckResult(
    UpdateCheckResult Update,
    ApplicationUpdateAvailability? InstallerAvailability);

internal sealed class AboutDialogModel
{
    public AboutDialogModel(string currentVersion) =>
        CurrentVersion = currentVersion;

    public string CurrentVersion { get; }
    public AboutUpdateState State { get; private set; } = AboutUpdateState.Checking;
    public ReleaseVersion? LatestVersion { get; private set; }
    public Uri? ReleaseUri { get; private set; }
    public bool CanSelfUpdate { get; private set; }

    public void Apply(
        UpdateCheckResult result,
        ApplicationUpdateAvailability? installerAvailability = null)
    {
        State = result.Status switch
        {
            UpdateCheckStatus.UpToDate => AboutUpdateState.UpToDate,
            UpdateCheckStatus.UpdateAvailable => AboutUpdateState.UpdateAvailable,
            _ => AboutUpdateState.Unavailable,
        };
        LatestVersion = result.LatestVersion;
        ReleaseUri = State == AboutUpdateState.UpdateAvailable ? result.ReleaseUri : null;
        CanSelfUpdate =
            State == AboutUpdateState.UpdateAvailable &&
            installerAvailability?.IsAvailable == true;
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

        IReadOnlyList<DialogButton> buttons;
        if (State == AboutUpdateState.UpdateAvailable && ReleaseUri is not null)
        {
            var updateButtons = new List<DialogButton>();
            if (CanSelfUpdate)
            {
                updateButtons.Add(new DialogButton(
                    "update-now",
                    "Update now",
                    'U',
                    IsDefault: true));
            }

            updateButtons.Add(DialogButton.Action("open-release", "Open release page", 'O'));
            updateButtons.Add(new DialogButton(
                "close",
                "Close",
                'C',
                Role: DialogButtonRole.Cancel));
            buttons = updateButtons;
        }
        else
        {
            buttons =
            [
                new DialogButton(
                    "close",
                    "Close",
                    'C',
                    IsDefault: true,
                    Role: DialogButtonRole.Cancel),
            ];
        }

        return new OperationDialogState<string>(rows: rows, buttons: buttons);
    }
}

internal sealed class AboutDialog
{
    private readonly DialogService _dialogs;
    private readonly IUriLauncher _uriLauncher;
    private readonly UpdateCheckService _updateCheckService;
    private readonly IApplicationUpdateInstaller _updateInstaller;
    private readonly ApplicationVersionInfo _versionInfo;
    private readonly Action _requestExit;

    public AboutDialog(
        DialogService dialogs,
        IUriLauncher uriLauncher,
        UpdateCheckService updateCheckService,
        IApplicationUpdateInstaller updateInstaller,
        ApplicationVersionInfo versionInfo,
        Action requestExit)
    {
        _dialogs = dialogs;
        _uriLauncher = uriLauncher;
        _updateCheckService = updateCheckService;
        _updateInstaller = updateInstaller;
        _versionInfo = versionInfo;
        _requestExit = requestExit;
    }

    public void Show()
    {
        var model = new AboutDialogModel(_versionInfo.AboutVersion);

        bool updateRequested = _dialogs.Operation(
            new OperationDialogDefinition<string, AboutCheckResult, bool>
            {
                Title = "About",
                PreferredWidth = 58,
                PreferredHeight = 11,
                MinWidth = 40,
                MinHeight = 8,
                RefreshInterval = TimeSpan.FromDays(1),
                Operation = async cancellationToken =>
                {
                    UpdateCheckResult update = await _updateCheckService.CheckAsync(
                        _versionInfo.ComparableVersion,
                        cancellationToken);
                    ApplicationUpdateAvailability? availability = null;
                    if (update.Status == UpdateCheckStatus.UpdateAvailable)
                        availability = await _updateInstaller.GetAvailabilityAsync(cancellationToken);
                    return new AboutCheckResult(update, availability);
                },
                Synchronize = model.Snapshot,
                HandleOperationCompleted = result =>
                {
                    model.Apply(result.Update, result.InstallerAvailability);
                    return OperationDialogOutcome<bool>.ContinueChanged;
                },
                HandleCommand = action => HandleCommand(action.ActionId, model),
                HandleCancel = () => OperationDialogOutcome<bool>.Complete(false),
                Complete = _ => false,
            });

        if (!updateRequested || model.LatestVersion is not { } expectedVersion)
            return;

        bool exitCurrentProcess =
            new ApplicationUpdateDialog(_dialogs, _updateInstaller).Show(expectedVersion);
        if (exitCurrentProcess)
            _requestExit();
    }

    private OperationDialogOutcome<bool> HandleCommand(
        string command,
        AboutDialogModel model)
    {
        if (command == "close")
            return OperationDialogOutcome<bool>.Complete(false);

        if (command == "update-now" && model.CanSelfUpdate)
            return OperationDialogOutcome<bool>.Complete(true);

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

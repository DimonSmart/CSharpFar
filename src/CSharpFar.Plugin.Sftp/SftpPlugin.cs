using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;
using CSharpFar.Plugin.Abstractions;

namespace CSharpFar.Plugin.Sftp;

public sealed class SftpPlugin : ICSharpFarPlugin
{
    private PluginStartupInfo? _startupInfo;
    private ISftpConnectionStore? _connectionStore;

    public PluginGlobalInfo GetGlobalInfo() =>
        new()
        {
            PluginId = SftpPluginIds.PluginId,
            Title = "SFTP",
            Description = "SFTP file panel provider.",
            Author = "CSharpFar",
            Version = new Version(1, 0, 0),
        };

    public PluginInfo GetPluginInfo() =>
        new()
        {
            PluginMenuItems =
            [
                new PluginMenuItem
                {
                    ItemId = SftpPluginIds.PluginMenuItemId,
                    Text = "SFTP...",
                    HotKey = 'S',
                },
            ],
            DiskMenuItems =
            [
                new PluginMenuItem
                {
                    ItemId = SftpPluginIds.DiskMenuItemId,
                    Text = "SFTP",
                    HotKey = 'S',
                },
            ],
            CommandPrefixes = ["sftp"],
        };

    public void SetStartupInfo(PluginStartupInfo startupInfo)
    {
        ArgumentNullException.ThrowIfNull(startupInfo);

        _startupInfo = startupInfo;
        _connectionStore = new SftpConnectionStore(
            startupInfo.Settings.GetSettingsDirectory(SftpPluginIds.PluginId));
    }

    public PluginOpenResult Open(PluginOpenInfo openInfo)
    {
        ArgumentNullException.ThrowIfNull(openInfo);

        if (!EnsureStorage(out string? storageError))
            return PluginOpenResult.Failed(storageError);

        var side = openInfo.PanelSide ?? StartupInfo.Panels.ActiveSide;
        return openInfo.OpenFrom switch
        {
            PluginOpenFrom.PluginMenu => OpenConnectionManager(side),
            PluginOpenFrom.LeftDiskMenu => OpenConnectionManager(PanelSide.Left),
            PluginOpenFrom.RightDiskMenu => OpenConnectionManager(PanelSide.Right),
            PluginOpenFrom.CommandLine => OpenConnectionManager(side),
            _ => PluginOpenResult.NoPanel(),
        };
    }

    private PluginStartupInfo StartupInfo =>
        _startupInfo ?? throw new InvalidOperationException("SFTP plugin startup info was not set.");

    private ISftpConnectionStore ConnectionStore =>
        _connectionStore ?? throw new InvalidOperationException("SFTP connection store was not initialized.");

    private ICredentialStore CredentialStore =>
        StartupInfo.Credentials ?? throw new InvalidOperationException("Credential storage is not configured.");

    private PluginOpenResult OpenConnectionManager(PanelSide side)
    {
        while (true)
        {
            var connections = ConnectionStore.Load();
            var result = new SftpConnectionManagerDialog(
                StartupInfo.Ui.Screen,
                StartupInfo.Ui.CurrentPalette).Show(connections);
            if (result is null)
                return PluginOpenResult.Completed();

            switch (result.Action)
            {
                case SftpConnectionManagerAction.Create:
                    if (ShowConnectionEditor(null, saveConnectionByDefault: true, allowTemporaryConnection: false) is { } created)
                        SaveConnectionResult(created);
                    break;
                case SftpConnectionManagerAction.Edit:
                    if (result.Connection is not null &&
                        ShowConnectionEditor(result.Connection, saveConnectionByDefault: true, allowTemporaryConnection: false) is { } edited)
                    {
                        SaveConnectionResult(edited);
                    }
                    break;
                case SftpConnectionManagerAction.Delete:
                    if (result.Connection is not null)
                        DeleteConnection(result.Connection);
                    break;
                case SftpConnectionManagerAction.Connect:
                    if (result.Connection is not null)
                        return OpenSavedConnection(side, result.Connection);
                    break;
            }
        }
    }

    private PluginOpenResult OpenSavedConnection(PanelSide side, SftpConnectionInfo connection)
    {
        string? password = connection.CredentialId is not null
            ? CredentialStore.TryReadPassword(connection.CredentialId)
            : null;

        if (password is null || string.IsNullOrWhiteSpace(connection.ExpectedHostKeyFingerprint))
        {
            var result = ShowConnectionEditor(
                connection,
                saveConnectionByDefault: true,
                allowTemporaryConnection: false);
            if (result is null)
                return PluginOpenResult.Completed();

            var openResult = OpenConnection(side, result.Connection, result.Password);
            if (openResult.Kind == PluginOpenResultKind.OpenedPanel)
                SaveConnectionResult(result);
            return openResult;
        }

        return OpenConnection(side, connection, password);
    }

    private PluginOpenResult OpenConnection(PanelSide side, SftpConnectionInfo connection, string password)
    {
        _ = side;
        return PluginOpenResult.OpenedPanel(new SftpFilePanelSource(connection, password));
    }

    private SftpConnectionDialogResult? ShowConnectionEditor(
        SftpConnectionInfo? connection,
        bool saveConnectionByDefault,
        bool allowTemporaryConnection)
    {
        string? savedPassword = connection?.CredentialId is not null
            ? CredentialStore.TryReadPassword(connection.CredentialId)
            : null;

        return new SftpConnectionDialog(StartupInfo.Ui.Screen, StartupInfo.Ui.CurrentPalette).Show(
            new SftpConnectionDialogRequest(
                connection,
                savedPassword,
                saveConnectionByDefault,
                allowTemporaryConnection),
            ValidateConnection);
    }

    private SftpConnectionDialogValidationResult ValidateConnection(SftpConnectionDialogResult result)
    {
        if (result.SavePassword && !result.SaveConnection)
            return SftpConnectionDialogValidationResult.Error("Password can be saved only with a saved connection.");

        if (string.IsNullOrWhiteSpace(result.Connection.ExpectedHostKeyFingerprint))
        {
            string? fingerprint = null;
            var untrustedProbe = new SftpFilePanelSource(
                result.Connection with { ExpectedHostKeyFingerprint = null },
                result.Password,
                acceptHostKey: (_, hostKeyFingerprint) =>
                {
                    fingerprint = hostKeyFingerprint;
                    return false;
                });
            try
            {
                _ = untrustedProbe.GetItem(result.Connection.RemoteRootPath);
            }
            catch (IOException ex)
            {
                return fingerprint is null
                    ? SftpConnectionDialogValidationResult.Error(ex.Message)
                    : SftpConnectionDialogValidationResult.RequireHostKeyTrust(fingerprint);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or InvalidOperationException)
            {
                return SftpConnectionDialogValidationResult.Error(ex.Message);
            }
        }

        try
        {
            var source = new SftpFilePanelSource(result.Connection, result.Password);
            _ = source.GetItem(result.Connection.RemoteRootPath);
            return SftpConnectionDialogValidationResult.Accepted();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return SftpConnectionDialogValidationResult.Error(ex.Message);
        }
    }

    private void SaveConnectionResult(SftpConnectionDialogResult result)
    {
        if (result.SavePassword && result.Connection.CredentialId is not null)
        {
            CredentialStore.SavePassword(result.Connection.CredentialId, result.Password);
        }
        else if (result.PreviousCredentialId is not null)
        {
            CredentialStore.DeletePassword(result.PreviousCredentialId);
        }

        if (result.SaveConnection)
            SaveConnection(result.Connection);
    }

    private void SaveConnection(SftpConnectionInfo connection)
    {
        var connections = ConnectionStore.Load().ToList();
        int index = connections.FindIndex(c => string.Equals(c.Id, connection.Id, StringComparison.Ordinal));
        if (index >= 0)
            connections[index] = connection;
        else
            connections.Add(connection);

        ConnectionStore.Save(connections);
    }

    private void DeleteConnection(SftpConnectionInfo connection)
    {
        if (!StartupInfo.Ui.Confirm("SFTP", "Delete saved connection?", connection.DisplayName))
            return;

        var connections = ConnectionStore
            .Load()
            .Where(item => !string.Equals(item.Id, connection.Id, StringComparison.Ordinal))
            .ToList();
        ConnectionStore.Save(connections);

        if (connection.CredentialId is not null)
            CredentialStore.DeletePassword(connection.CredentialId);
    }

    private bool EnsureStorage(out string error)
    {
        if (_connectionStore is not null && StartupInfo.Credentials is not null)
        {
            error = string.Empty;
            return true;
        }

        error = "SFTP connection storage is not configured.";
        return false;
    }
}

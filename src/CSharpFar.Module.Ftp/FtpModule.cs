using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;
using CSharpFar.Module.Abstractions;

namespace CSharpFar.Module.Ftp;

public sealed class FtpModule
{
    private ModuleStartupInfo? _startupInfo;
    private IFtpConnectionStore? _connectionStore;

    public void Initialize(ModuleStartupInfo startupInfo)
    {
        ArgumentNullException.ThrowIfNull(startupInfo);

        _startupInfo = startupInfo;
        _connectionStore = new FtpConnectionStore(
            startupInfo.Settings.GetSettingsDirectory(FtpModuleIds.ModuleId));
    }

    public ModuleActionResult OpenFromMenu(PanelSide panelSide) =>
        OpenConnectionManager(panelSide);

    public ModuleActionResult OpenFromDiskMenu(PanelSide panelSide) =>
        OpenConnectionManager(panelSide);

    public ModuleActionResult OpenFromCommandLine(PanelSide panelSide, string commandLine)
    {
        _ = commandLine;
        return OpenConnectionManager(panelSide);
    }

    private ModuleStartupInfo StartupInfo =>
        _startupInfo ?? throw new InvalidOperationException("FTP module startup info was not set.");

    private IFtpConnectionStore ConnectionStore =>
        _connectionStore ?? throw new InvalidOperationException("FTP connection store was not initialized.");

    private ICredentialStore CredentialStore =>
        StartupInfo.Credentials ?? throw new InvalidOperationException("Credential storage is not configured.");

    private ModuleActionResult OpenConnectionManager(PanelSide side)
    {
        if (!EnsureStorage(out string? storageError))
            return ModuleActionResult.Failed(storageError);

        while (true)
        {
            var connections = ConnectionStore.Load();
            var result = new FtpConnectionManagerDialog(
                StartupInfo.Ui.Screen,
                StartupInfo.Ui.CurrentPalette).Show(connections);
            if (result is null)
                return ModuleActionResult.Completed();

            switch (result.Action)
            {
                case FtpConnectionManagerAction.Create:
                    if (ShowConnectionEditor(null, saveConnectionByDefault: true, allowTemporaryConnection: false) is { } created)
                        SaveConnectionResult(created);
                    break;
                case FtpConnectionManagerAction.Edit:
                    if (result.Connection is not null &&
                        ShowConnectionEditor(result.Connection, saveConnectionByDefault: true, allowTemporaryConnection: false) is { } edited)
                    {
                        SaveConnectionResult(edited);
                    }
                    break;
                case FtpConnectionManagerAction.Delete:
                    if (result.Connection is not null)
                        DeleteConnection(result.Connection);
                    break;
                case FtpConnectionManagerAction.Connect:
                    if (result.Connection is not null)
                        return OpenSavedConnection(side, result.Connection);
                    break;
            }
        }
    }

    private ModuleActionResult OpenSavedConnection(PanelSide side, FtpConnectionInfo connection)
    {
        string? password = connection.CredentialId is not null
            ? CredentialStore.TryReadPassword(connection.CredentialId)
            : null;

        if (password is null || RequiresCertificateFingerprint(connection))
        {
            var result = ShowConnectionEditor(
                connection,
                saveConnectionByDefault: true,
                allowTemporaryConnection: false);
            if (result is null)
                return ModuleActionResult.Completed();

            var openResult = OpenConnection(side, result.Connection, result.Password);
            if (openResult.Kind == ModuleActionResultKind.OpenedPanel)
                SaveConnectionResult(result);
            return openResult;
        }

        return OpenConnection(side, connection, password);
    }

    private static ModuleActionResult OpenConnection(PanelSide side, FtpConnectionInfo connection, string password)
    {
        _ = side;
        return ModuleActionResult.OpenedPanel(new FtpFilePanelSource(connection, password));
    }

    private FtpConnectionDialogResult? ShowConnectionEditor(
        FtpConnectionInfo? connection,
        bool saveConnectionByDefault,
        bool allowTemporaryConnection)
    {
        string? savedPassword = connection?.CredentialId is not null
            ? CredentialStore.TryReadPassword(connection.CredentialId)
            : null;

        return new FtpConnectionDialog(StartupInfo.Ui.Screen, StartupInfo.Ui.CurrentPalette).Show(
            new FtpConnectionDialogRequest(
                connection,
                savedPassword,
                saveConnectionByDefault,
                allowTemporaryConnection),
            ValidateConnection);
    }

    private FtpConnectionDialogValidationResult ValidateConnection(FtpConnectionDialogResult result)
    {
        if (result.SavePassword && !result.SaveConnection)
            return FtpConnectionDialogValidationResult.Error("Password can be saved only with a saved connection.");

        try
        {
            FtpFilePanelSource.ValidateActiveModeLocalPortRange(result.Connection);
        }
        catch (ArgumentException ex)
        {
            return FtpConnectionDialogValidationResult.Error(ex.Message);
        }

        if (RequiresCertificateFingerprint(result.Connection))
        {
            string? fingerprint = null;
            var untrustedProbe = new FtpFilePanelSource(
                result.Connection with { ExpectedTlsCertificateFingerprint = null },
                result.Password,
                acceptCertificate: (_, certificateFingerprint) =>
                {
                    fingerprint = certificateFingerprint;
                    return false;
                });
            try
            {
                _ = untrustedProbe.GetItem(result.Connection.RemoteRootPath);
            }
            catch (IOException ex)
            {
                return fingerprint is null
                    ? FtpConnectionDialogValidationResult.Error(ex.Message)
                    : FtpConnectionDialogValidationResult.RequireCertificateTrust(fingerprint);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or InvalidOperationException or ArgumentException)
            {
                return FtpConnectionDialogValidationResult.Error(ex.Message);
            }
        }

        try
        {
            var source = new FtpFilePanelSource(result.Connection, result.Password);
            _ = source.GetItem(result.Connection.RemoteRootPath);
            return FtpConnectionDialogValidationResult.Accepted();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            return FtpConnectionDialogValidationResult.Error(ex.Message);
        }
    }

    private void SaveConnectionResult(FtpConnectionDialogResult result)
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

    private void SaveConnection(FtpConnectionInfo connection)
    {
        var connections = ConnectionStore.Load().ToList();
        int index = connections.FindIndex(c => string.Equals(c.Id, connection.Id, StringComparison.Ordinal));
        if (index >= 0)
            connections[index] = connection;
        else
            connections.Add(connection);

        ConnectionStore.Save(connections);
    }

    private void DeleteConnection(FtpConnectionInfo connection)
    {
        if (!StartupInfo.Ui.Confirm("FTP/FTPS", "Delete saved connection?", connection.DisplayName))
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

        error = "FTP/FTPS connection storage is not configured.";
        return false;
    }

    private static bool RequiresCertificateFingerprint(FtpConnectionInfo connection) =>
        connection.SecurityMode != FtpConnectionSecurityMode.PlainFtp &&
        string.IsNullOrWhiteSpace(connection.ExpectedTlsCertificateFingerprint);
}

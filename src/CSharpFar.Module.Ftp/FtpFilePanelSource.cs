using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;
using CSharpFar.Module.Abstractions;
using FluentFTP;
using FluentFTP.Exceptions;

namespace CSharpFar.Module.Ftp;

public sealed class FtpFilePanelSource : IModulePanel
{
    private readonly FtpConnectionInfo _connection;
    private readonly string _password;
    private readonly Func<FtpConnectionInfo, string, bool> _acceptCertificate;
    private readonly Action<FtpConnectionInfo, string>? _acceptedCertificate;

    public FtpFilePanelSource(
        FtpConnectionInfo connection,
        string password,
        Func<FtpConnectionInfo, string, bool>? acceptCertificate = null,
        Action<FtpConnectionInfo, string>? acceptedCertificate = null)
    {
        _connection = connection;
        _password = password;
        _acceptCertificate = acceptCertificate ?? ((_, _) => false);
        _acceptedCertificate = acceptedCertificate;
    }

    public PanelSourceId SourceId => PanelSourceId.Module(FtpModuleIds.ModuleId, _connection.Id);

    public string DisplayName => _connection.DisplayName;

    public ModulePanelInfo GetOpenPanelInfo() =>
        new()
        {
            Format = FtpPanelFormat(_connection),
            Title = $"{FtpPanelTitle(_connection)}: {_connection.DisplayName}",
            CurrentDirectory = NormalizePath(_connection.RemoteRootPath),
            ShortcutData = $"{_connection.Username}@{_connection.Host}:{_connection.Port}",
        };

    public void Dispose()
    {
    }

    public PanelProviderCapabilities Capabilities =>
        PanelProviderCapabilities.Enumerate |
        PanelProviderCapabilities.OpenRead |
        PanelProviderCapabilities.OpenWrite |
        PanelProviderCapabilities.CreateDirectory |
        PanelProviderCapabilities.Delete |
        PanelProviderCapabilities.Rename |
        PanelProviderCapabilities.CopyFrom |
        PanelProviderCapabilities.CopyTo |
        PanelProviderCapabilities.Refresh;

    public string NormalizePath(string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
            sourcePath = _connection.RemoteRootPath;

        sourcePath = sourcePath.Replace('\\', '/');
        if (!sourcePath.StartsWith('/'))
            sourcePath = "/" + sourcePath;

        var parts = new List<string>();
        foreach (string part in sourcePath.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == ".")
                continue;
            if (part == "..")
            {
                if (parts.Count > 0)
                    parts.RemoveAt(parts.Count - 1);
                continue;
            }

            parts.Add(part);
        }

        return parts.Count == 0 ? "/" : "/" + string.Join('/', parts);
    }

    public bool IsRootPath(string sourcePath) => NormalizePath(sourcePath) == "/";

    public string? GetParentPath(string sourcePath)
    {
        string normalized = NormalizePath(sourcePath);
        if (normalized == "/")
            return null;

        int slash = normalized.LastIndexOf('/');
        return slash <= 0 ? "/" : normalized[..slash];
    }

    public IReadOnlyList<FilePanelItem> EnumerateDirectory(
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string path = NormalizePath(sourcePath);
        return ExecuteFtpOperation(
            "read directory",
            path,
            client => client
                .GetListing(path)
                .Where(item => item.Name is not "." and not "..")
                .Select(ToPanelItem)
                .ToList());
    }

    public FilePanelItem? GetItem(
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string path = NormalizePath(sourcePath);

        return ExecuteFtpOperation<FilePanelItem?>(
            "read item",
            path,
            client =>
            {
                var item = client.GetObjectInfo(path, dateModified: true);
                if (item is not null)
                    return ToPanelItem(item);

                if (path == "/" || client.DirectoryExists(path))
                    return ToDirectoryItem(path, DateTime.MinValue);

                return null;
            });
    }

    public Task<Stream> OpenReadAsync(
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var client = CreateConnectedClient();
        string path = NormalizePath(sourcePath);
        try
        {
            Stream stream = client.OpenRead(path, FtpDataType.Binary, restart: 0, checkIfFileExists: true);
            return Task.FromResult<Stream>(new ClientOwnedStream(
                client,
                stream,
                readCompletionReply: false,
                ex => CreateFtpException(ex, "read file", path)));
        }
        catch (Exception ex) when (IsFtpClientException(ex))
        {
            client.Dispose();
            throw CreateFtpException(ex, "open file for reading", path);
        }
    }

    public Task<Stream> OpenWriteAsync(
        string sourcePath,
        bool overwrite,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var client = CreateConnectedClient();
        string path = NormalizePath(sourcePath);
        try
        {
            if (!overwrite && client.GetObjectInfo(path) is not null)
                throw new IOException($"FTP open file for writing failed for {path}: file already exists.");

            Stream stream = client.OpenWrite(path, FtpDataType.Binary, checkIfFileExists: false);
            return Task.FromResult<Stream>(new ClientOwnedStream(
                client,
                stream,
                readCompletionReply: true,
                ex => CreateFtpException(ex, "write file", path)));
        }
        catch (Exception ex) when (IsFtpClientException(ex))
        {
            client.Dispose();
            throw CreateFtpException(ex, "open file for writing", path);
        }
    }

    public Task CreateDirectoryAsync(
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string path = NormalizePath(sourcePath);
        ExecuteFtpOperation(
            "create directory",
            path,
            client => client.CreateDirectory(path, force: true));
        return Task.CompletedTask;
    }

    public Task DeleteAsync(
        string sourcePath,
        bool recursive,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string path = NormalizePath(sourcePath);
        ExecuteFtpOperation(
            "delete",
            path,
            client => DeleteCore(client, path, recursive, cancellationToken));
        return Task.CompletedTask;
    }

    public Task RenameAsync(
        string sourcePath,
        string newSourcePath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string path = NormalizePath(sourcePath);
        string newPath = NormalizePath(newSourcePath);
        ExecuteFtpOperation(
            "rename",
            path,
            client =>
            {
                var item = client.GetObjectInfo(path)
                    ?? throw new DirectoryNotFoundException($"FTP rename failed for {path}: path not found.");
                if (IsDirectory(item))
                    client.MoveDirectory(path, newPath, FtpRemoteExists.Overwrite);
                else
                    client.MoveFile(path, newPath, FtpRemoteExists.Overwrite);
            });
        return Task.CompletedTask;
    }

    internal static FtpConfig CreateConfig(FtpConnectionInfo connection)
    {
        ValidateActiveModeLocalPortRange(connection);

        var config = new FtpConfig
        {
            EncryptionMode = connection.SecurityMode switch
            {
                FtpConnectionSecurityMode.PlainFtp => FtpEncryptionMode.None,
                FtpConnectionSecurityMode.ExplicitFtps => FtpEncryptionMode.Explicit,
                FtpConnectionSecurityMode.ImplicitFtps => FtpEncryptionMode.Implicit,
                FtpConnectionSecurityMode.Auto => FtpEncryptionMode.Auto,
                _ => throw new ArgumentOutOfRangeException(nameof(connection), connection.SecurityMode, "Unsupported FTP security mode."),
            },
            DataConnectionType = connection.DataConnectionMode switch
            {
                FtpDataConnectionMode.AutoPassive => FtpDataConnectionType.AutoPassive,
                FtpDataConnectionMode.Passive => FtpDataConnectionType.PASV,
                FtpDataConnectionMode.Active => FtpDataConnectionType.AutoActive,
                _ => throw new ArgumentOutOfRangeException(nameof(connection), connection.DataConnectionMode, "Unsupported FTP data connection mode."),
            },
            DataConnectionEncryption =
                connection.SecurityMode != FtpConnectionSecurityMode.PlainFtp &&
                connection.UseDataConnectionTls,
            ValidateAnyCertificate = false,
        };

        if (connection.DataConnectionMode == FtpDataConnectionMode.Active &&
            connection.ActiveModeLocalPortFrom is { } from &&
            connection.ActiveModeLocalPortTo is { } to)
        {
            config.ActivePorts = Enumerable.Range(from, to - from + 1).ToArray();
        }

        return config;
    }

    public static void ValidateActiveModeLocalPortRange(FtpConnectionInfo connection)
    {
        if (connection.DataConnectionMode != FtpDataConnectionMode.Active)
            return;

        bool hasFrom = connection.ActiveModeLocalPortFrom.HasValue;
        bool hasTo = connection.ActiveModeLocalPortTo.HasValue;
        if (hasFrom != hasTo)
            throw new ArgumentException("Active-mode local port range must include both start and end ports.");

        if (!hasFrom)
            return;

        int from = connection.ActiveModeLocalPortFrom.GetValueOrDefault();
        int to = connection.ActiveModeLocalPortTo.GetValueOrDefault();
        if (from is <= 0 or > 65535 ||
            to is <= 0 or > 65535 ||
            from > to)
        {
            throw new ArgumentException("Active-mode local port range must be between 1 and 65535, with start not greater than end.");
        }
    }

    private static string FtpPanelFormat(FtpConnectionInfo connection) =>
        connection.SecurityMode == FtpConnectionSecurityMode.PlainFtp ? "FTP" : "FTPS";

    internal static string FtpPanelTitle(FtpConnectionInfo connection) =>
        connection.SecurityMode switch
        {
            FtpConnectionSecurityMode.PlainFtp => "FTP",
            FtpConnectionSecurityMode.ExplicitFtps => "FTPS explicit",
            FtpConnectionSecurityMode.ImplicitFtps => "FTPS implicit",
            FtpConnectionSecurityMode.Auto => "FTP/FTPS auto",
            _ => "FTP/FTPS",
        };

    private FilePanelItem ToPanelItem(FtpListItem item)
    {
        string fullName = NormalizePath(string.IsNullOrWhiteSpace(item.FullName) ? item.Name : item.FullName);
        bool isDirectory = IsDirectory(item);
        return new FilePanelItem
        {
            Name = string.IsNullOrWhiteSpace(item.Name)
                ? fullName[(fullName.LastIndexOf('/') + 1)..]
                : item.Name,
            FullPath = fullName,
            SourceId = SourceId,
            IsDirectory = isDirectory,
            Size = isDirectory ? null : item.Size,
            LastWriteTime = item.Modified == DateTime.MinValue ? DateTime.MinValue : item.Modified,
            Attributes = isDirectory ? FileAttributes.Directory : FileAttributes.Normal,
            IsParentDirectory = false,
        };
    }

    private FilePanelItem ToDirectoryItem(string path, DateTime lastWriteTime) =>
        new()
        {
            Name = path == "/" ? "/" : path[(path.LastIndexOf('/') + 1)..],
            FullPath = path,
            SourceId = SourceId,
            IsDirectory = true,
            Size = null,
            LastWriteTime = lastWriteTime,
            Attributes = FileAttributes.Directory,
            IsParentDirectory = false,
        };

    private FtpClient CreateConnectedClient()
    {
        var validation = new TlsCertificateValidationState();
        var config = CreateConfig(_connection);
        var client = new FtpClient(
            _connection.Host,
            _connection.Username,
            _password,
            _connection.Port,
            config);
        client.ValidateCertificate += (_, args) => HandleCertificateValidation(args, validation);

        try
        {
            client.Connect();
            return client;
        }
        catch (Exception ex) when (IsFtpClientException(ex))
        {
            client.Dispose();
            if (validation.ErrorMessage is not null)
                throw new IOException(validation.ErrorMessage, ex);

            if (ex is FtpAuthenticationException)
                throw new UnauthorizedAccessException(
                    $"FTP authentication failed for {_connection.Host}: {ex.Message}",
                    ex);

            throw new IOException(
                $"FTP connection failed for {_connection.Host}:{_connection.Port}: {ex.Message}",
                ex);
        }
    }

    private T ExecuteFtpOperation<T>(
        string action,
        string sourcePath,
        Func<FtpClient, T> operation)
    {
        using var client = CreateConnectedClient();
        try
        {
            return operation(client);
        }
        catch (Exception ex) when (IsFtpClientException(ex))
        {
            throw CreateFtpException(ex, action, sourcePath);
        }
    }

    private void ExecuteFtpOperation(
        string action,
        string sourcePath,
        Action<FtpClient> operation)
    {
        ExecuteFtpOperation(
            action,
            sourcePath,
            client =>
            {
                operation(client);
                return true;
            });
    }

    private void HandleCertificateValidation(
        FtpSslValidationEventArgs args,
        TlsCertificateValidationState state)
    {
        args.Accept = ValidateCertificateFingerprint(
            _connection,
            args.Certificate,
            _acceptCertificate,
            _acceptedCertificate,
            out string? errorMessage);
        state.ErrorMessage = errorMessage;
    }

    internal static bool ValidateCertificateFingerprint(
        FtpConnectionInfo connection,
        X509Certificate certificate,
        Func<FtpConnectionInfo, string, bool> acceptCertificate,
        Action<FtpConnectionInfo, string>? acceptedCertificate,
        out string? errorMessage)
    {
        string fingerprint = FormatCertificateFingerprint(certificate);
        if (!string.IsNullOrWhiteSpace(connection.ExpectedTlsCertificateFingerprint))
        {
            bool accepted = string.Equals(
                connection.ExpectedTlsCertificateFingerprint,
                fingerprint,
                StringComparison.OrdinalIgnoreCase);
            errorMessage = accepted
                ? null
                : $"FTPS certificate fingerprint changed for {connection.Host}.\n" +
                  $"Expected {connection.ExpectedTlsCertificateFingerprint}, got {fingerprint}.";
            return accepted;
        }

        bool trust = acceptCertificate(connection, fingerprint);
        if (trust)
        {
            acceptedCertificate?.Invoke(connection, fingerprint);
            errorMessage = null;
            return true;
        }

        errorMessage = $"FTPS certificate was not accepted for {connection.Host}.";
        return false;
    }

    internal static string FormatCertificateFingerprint(X509Certificate certificate)
    {
        using var certificate2 = new X509Certificate2(certificate);
        byte[] fingerprint = certificate2.GetCertHash(HashAlgorithmName.SHA256);
        return string.Join(':', fingerprint.Select(value => value.ToString("X2")));
    }

    private static bool IsDirectory(FtpListItem item) =>
        item.Type == FtpObjectType.Directory ||
        item is { Type: FtpObjectType.Link, LinkObject.Type: FtpObjectType.Directory };

    private static bool IsFtpClientException(Exception exception) =>
        exception is FtpException or
                     SocketException or
                     TimeoutException or
                     ObjectDisposedException or
                     InvalidOperationException or
                     AuthenticationException or
                     IOException or
                     UnauthorizedAccessException;

    private static Exception CreateFtpException(
        Exception exception,
        string action,
        string sourcePath)
    {
        if (exception is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
            return exception;

        if (exception is FtpAuthenticationException)
            return new UnauthorizedAccessException(
                $"FTP {action} failed for {sourcePath}: authentication failed.",
                exception);

        if (exception is FtpMissingObjectException)
            return new DirectoryNotFoundException(
                $"FTP {action} failed for {sourcePath}: path not found.",
                exception);

        if (exception is FtpCommandException commandException)
        {
            string message = commandException.Message;
            if (message.Contains("permission", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("access", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(commandException.CompletionCode, "530", StringComparison.Ordinal))
            {
                return new UnauthorizedAccessException(
                    $"FTP {action} failed for {sourcePath}: {message}",
                    exception);
            }

            if (string.Equals(commandException.CompletionCode, "550", StringComparison.Ordinal))
            {
                return new DirectoryNotFoundException(
                    $"FTP {action} failed for {sourcePath}: {message}",
                    exception);
            }
        }

        return new IOException(
            $"FTP {action} failed for {sourcePath}: {exception.Message}",
            exception);
    }

    private void DeleteCore(
        FtpClient client,
        string sourcePath,
        bool recursive,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var item = client.GetObjectInfo(sourcePath, dateModified: false)
            ?? throw new DirectoryNotFoundException($"FTP delete failed for {sourcePath}: path not found.");

        if (!IsDirectory(item))
        {
            client.DeleteFile(sourcePath);
            return;
        }

        if (recursive)
        {
            foreach (var child in client.GetListing(sourcePath).Where(item => item.Name is not "." and not ".."))
                DeleteCore(client, NormalizePath(child.FullName), recursive: true, cancellationToken);
        }

        client.DeleteDirectory(sourcePath);
    }

    private sealed class ClientOwnedStream : Stream
    {
        private readonly FtpClient _client;
        private readonly Stream _inner;
        private readonly bool _readCompletionReply;
        private readonly Func<Exception, Exception> _translateException;
        private bool _disposed;

        public ClientOwnedStream(
            FtpClient client,
            Stream inner,
            bool readCompletionReply,
            Func<Exception, Exception> translateException)
        {
            _client = client;
            _inner = inner;
            _readCompletionReply = readCompletionReply;
            _translateException = translateException;
        }

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => _inner.CanSeek;
        public override bool CanWrite => _inner.CanWrite;
        public override long Length => ExecuteStreamOperation(() => _inner.Length);

        public override long Position
        {
            get => ExecuteStreamOperation(() => _inner.Position);
            set => ExecuteStreamOperation(() => { _inner.Position = value; });
        }

        public override void Flush() => ExecuteStreamOperation(_inner.Flush);
        public override int Read(byte[] buffer, int offset, int count) =>
            ExecuteStreamOperation(() => _inner.Read(buffer, offset, count));
        public override long Seek(long offset, SeekOrigin origin) =>
            ExecuteStreamOperation(() => _inner.Seek(offset, origin));
        public override void SetLength(long value) => ExecuteStreamOperation(() => _inner.SetLength(value));
        public override void Write(byte[] buffer, int offset, int count) =>
            ExecuteStreamOperation(() => _inner.Write(buffer, offset, count));

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            try
            {
                return await _inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (IsFtpClientException(ex))
            {
                throw _translateException(ex);
            }
        }

        public override async ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            try
            {
                await _inner.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (IsFtpClientException(ex))
            {
                throw _translateException(ex);
            }
        }

        private void ExecuteStreamOperation(Action operation)
        {
            try
            {
                operation();
            }
            catch (Exception ex) when (IsFtpClientException(ex))
            {
                throw _translateException(ex);
            }
        }

        private T ExecuteStreamOperation<T>(Func<T> operation)
        {
            try
            {
                return operation();
            }
            catch (Exception ex) when (IsFtpClientException(ex))
            {
                throw _translateException(ex);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                DisposeCore();

            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            if (!_disposed)
            {
                try
                {
                    await _inner.DisposeAsync().ConfigureAwait(false);
                    ReadCompletionReply();
                }
                catch (Exception ex) when (IsFtpClientException(ex))
                {
                    throw _translateException(ex);
                }
                finally
                {
                    _disposed = true;
                    _client.Dispose();
                }
            }

            await base.DisposeAsync().ConfigureAwait(false);
        }

        private void DisposeCore()
        {
            if (_disposed)
                return;

            try
            {
                _inner.Dispose();
                ReadCompletionReply();
            }
            catch (Exception ex) when (IsFtpClientException(ex))
            {
                throw _translateException(ex);
            }
            finally
            {
                _disposed = true;
                _client.Dispose();
            }
        }

        private void ReadCompletionReply()
        {
            if (_readCompletionReply)
                _client.GetReply();
        }
    }

    private sealed class TlsCertificateValidationState
    {
        public string? ErrorMessage { get; set; }
    }
}

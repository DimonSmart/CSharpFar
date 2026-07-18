using System.Net.Sockets;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;
using CSharpFar.Module.Abstractions;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace CSharpFar.Module.Sftp;

public sealed class SftpFilePanelSource : IModulePanel
{
    private readonly SftpConnectionInfo _connection;
    private readonly string _password;
    private readonly Func<SftpConnectionInfo, string, bool> _acceptHostKey;
    private readonly Action<SftpConnectionInfo, string>? _acceptedHostKey;

    public SftpFilePanelSource(
        SftpConnectionInfo connection,
        string password,
        Func<SftpConnectionInfo, string, bool>? acceptHostKey = null,
        Action<SftpConnectionInfo, string>? acceptedHostKey = null)
    {
        _connection = connection;
        _password = password;
        _acceptHostKey = acceptHostKey ?? ((_, _) => false);
        _acceptedHostKey = acceptedHostKey;
    }

    public PanelSourceId SourceId => PanelSourceId.Module(SftpModuleIds.ModuleId, _connection.Id);

    public string DisplayName => _connection.DisplayName;

    public ModulePanelInfo GetOpenPanelInfo() =>
        new()
        {
            Format = "SFTP",
            Title = $"SFTP: {_connection.DisplayName}",
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
        return ExecuteSftpOperation(
            "read directory",
            path,
            client => client
                .ListDirectory(path)
                .Where(item => item.Name is not "." and not "..")
                .Select(item => ToPanelItem(item))
                .ToList());
    }

    public FilePanelItem? GetItem(
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string path = NormalizePath(sourcePath);

        return ExecuteSftpOperation<FilePanelItem?>(
            "read item",
            path,
            client =>
            {
                if (!client.Exists(path))
                    return null;

                var attributes = client.GetAttributes(path);
                return new FilePanelItem
                {
                    Name = path == "/" ? "/" : path[(path.LastIndexOf('/') + 1)..],
                    FullPath = path,
                    SourceId = SourceId,
                    IsDirectory = attributes.IsDirectory,
                    Size = attributes.IsDirectory ? null : attributes.Size,
                    LastWriteTime = attributes.LastWriteTime,
                    Attributes = attributes.IsDirectory ? FileAttributes.Directory : FileAttributes.Normal,
                    IsParentDirectory = false,
                };
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
            Stream stream = client.OpenRead(path);
            return Task.FromResult<Stream>(new ClientOwnedStream(
                client,
                stream,
                ex => CreateSftpException(ex, "read file", path)));
        }
        catch (Exception ex) when (IsSftpClientException(ex))
        {
            client.Dispose();
            throw CreateSftpException(ex, "open file for reading", path);
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
            Stream stream = client.Open(
                path,
                overwrite ? FileMode.Create : FileMode.CreateNew,
                FileAccess.Write);
            return Task.FromResult<Stream>(new ClientOwnedStream(
                client,
                stream,
                ex => CreateSftpException(ex, "write file", path)));
        }
        catch (Exception ex) when (IsSftpClientException(ex))
        {
            client.Dispose();
            throw CreateSftpException(ex, "open file for writing", path);
        }
    }

    public Task CreateDirectoryAsync(
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string path = NormalizePath(sourcePath);
        ExecuteSftpOperation(
            "create directory",
            path,
            client => client.CreateDirectory(path));
        return Task.CompletedTask;
    }

    public Task DeleteAsync(
        string sourcePath,
        bool recursive,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string path = NormalizePath(sourcePath);
        ExecuteSftpOperation(
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
        ExecuteSftpOperation(
            "rename",
            path,
            client => client.RenameFile(path, newPath));
        return Task.CompletedTask;
    }

    private FilePanelItem ToPanelItem(Renci.SshNet.Sftp.ISftpFile item) =>
        new()
        {
            Name = item.Name,
            FullPath = NormalizePath(item.FullName),
            SourceId = SourceId,
            IsDirectory = item.IsDirectory,
            Size = item.IsDirectory ? null : item.Length,
            LastWriteTime = item.LastWriteTime,
            Attributes = item.IsDirectory ? FileAttributes.Directory : FileAttributes.Normal,
            IsParentDirectory = false,
        };

    private SftpClient CreateConnectedClient()
    {
        var client = new SftpClient(_connection.Host, _connection.Port, _connection.Username, _password);
        var hostKeyValidation = new HostKeyValidationState();
        client.HostKeyReceived += (_, args) => HandleHostKeyReceived(args, hostKeyValidation);
        try
        {
            client.Connect();
            return client;
        }
        catch (Exception ex) when (IsSftpClientException(ex))
        {
            client.Dispose();
            if (hostKeyValidation.ErrorMessage is not null)
                throw new IOException(hostKeyValidation.ErrorMessage, ex);

            if (ex is SshAuthenticationException)
                throw new UnauthorizedAccessException(
                    $"SFTP authentication failed for {_connection.Host}: {ex.Message}",
                    ex);

            throw new IOException(
                $"SFTP connection failed for {_connection.Host}:{_connection.Port}: {ex.Message}",
                ex);
        }
    }

    private T ExecuteSftpOperation<T>(
        string action,
        string sourcePath,
        Func<SftpClient, T> operation)
    {
        using var client = CreateConnectedClient();
        try
        {
            return operation(client);
        }
        catch (Exception ex) when (IsSftpClientException(ex))
        {
            throw CreateSftpException(ex, action, sourcePath);
        }
    }

    private void ExecuteSftpOperation(
        string action,
        string sourcePath,
        Action<SftpClient> operation)
    {
        ExecuteSftpOperation(
            action,
            sourcePath,
            client =>
            {
                operation(client);
                return true;
            });
    }

    private void HandleHostKeyReceived(HostKeyEventArgs args, HostKeyValidationState state)
    {
        string fingerprint = FormatFingerprint(args.FingerPrint);
        if (!string.IsNullOrWhiteSpace(_connection.ExpectedHostKeyFingerprint))
        {
            args.CanTrust = string.Equals(
                _connection.ExpectedHostKeyFingerprint,
                fingerprint,
                StringComparison.OrdinalIgnoreCase);
            if (!args.CanTrust)
            {
                state.ErrorMessage =
                    $"SFTP host key fingerprint changed for {_connection.Host}.\n" +
                    $"Expected {_connection.ExpectedHostKeyFingerprint}, got {fingerprint}.";
            }
            return;
        }

        args.CanTrust = _acceptHostKey(_connection, fingerprint);
        if (!args.CanTrust)
            state.ErrorMessage = $"SFTP host key was not accepted for {_connection.Host}.";
        if (args.CanTrust)
            _acceptedHostKey?.Invoke(_connection, fingerprint);
    }

    private static string FormatFingerprint(byte[] fingerprint) =>
        string.Join(':', fingerprint.Select(value => value.ToString("X2")));

    private static bool IsSftpClientException(Exception exception) =>
        exception is SshException or
                     SftpPermissionDeniedException or
                     SftpPathNotFoundException or
                     SocketException or
                     TimeoutException or
                     ObjectDisposedException or
                     InvalidOperationException or
                     IOException or
                     UnauthorizedAccessException;

    private static Exception CreateSftpException(
        Exception exception,
        string action,
        string sourcePath)
    {
        if (exception is IOException or UnauthorizedAccessException)
            return exception;

        if (exception is SftpPermissionDeniedException)
            return new UnauthorizedAccessException(
                $"SFTP {action} failed for {sourcePath}: permission denied.",
                exception);

        if (exception is SftpPathNotFoundException)
            return new DirectoryNotFoundException(
                $"SFTP {action} failed for {sourcePath}: path not found.",
                exception);

        return new IOException(
            $"SFTP {action} failed for {sourcePath}: {exception.Message}",
            exception);
    }

    private void DeleteCore(
        SftpClient client,
        string sourcePath,
        bool recursive,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var attributes = client.GetAttributes(sourcePath);
        if (!attributes.IsDirectory)
        {
            client.DeleteFile(sourcePath);
            return;
        }

        if (recursive)
        {
            foreach (var child in client.ListDirectory(sourcePath).Where(item => item.Name is not "." and not ".."))
                DeleteCore(client, NormalizePath(child.FullName), recursive: true, cancellationToken);
        }

        client.DeleteDirectory(sourcePath);
    }

    private sealed class ClientOwnedStream : Stream
    {
        private readonly SftpClient _client;
        private readonly Stream _inner;
        private readonly Func<Exception, Exception> _translateException;

        public ClientOwnedStream(
            SftpClient client,
            Stream inner,
            Func<Exception, Exception> translateException)
        {
            _client = client;
            _inner = inner;
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
            catch (Exception ex) when (IsSftpClientException(ex))
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
            catch (Exception ex) when (IsSftpClientException(ex))
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
            catch (Exception ex) when (IsSftpClientException(ex))
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
            catch (Exception ex) when (IsSftpClientException(ex))
            {
                throw _translateException(ex);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
                _client.Dispose();
            }

            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            await _inner.DisposeAsync().ConfigureAwait(false);
            _client.Dispose();
            await base.DisposeAsync().ConfigureAwait(false);
        }
    }

    private sealed class HostKeyValidationState
    {
        public string? ErrorMessage { get; set; }
    }
}

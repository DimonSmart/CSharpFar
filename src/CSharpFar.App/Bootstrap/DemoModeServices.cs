using System.Net;
using System.Net.NetworkInformation;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;
using CSharpFar.Platform.Abstractions;

namespace CSharpFar.App.Bootstrap;

internal static class DemoModeServices
{
    public sealed class DemoProcessesAndPortsPlatformService : IProcessesAndPortsPlatformService
    {
        public ProcessesAndPortsSupportInfo Support { get; } = new(true, false, TerminationUnavailableReason: "Terminate process is disabled in demo mode.");
        public ProcessesAndPortsSnapshot CaptureSnapshot(ProcessesAndPortsQuery query, CancellationToken cancellationToken = default)
        {
            var process = new ProcessSnapshot(18424, "dotnet", @"C:\\Program Files\\dotnet\\dotnet.exe", DateTimeOffset.Parse("2026-07-30T18:12:43+00:00"));
            var all = new[] { new ProcessNetworkEndpoint(NetworkTransportProtocol.Tcp, IPAddress.Loopback, 64341, null, null, TcpState.Listen, process), new ProcessNetworkEndpoint(NetworkTransportProtocol.Tcp, IPAddress.IPv6Loopback, 64341, null, null, TcpState.Listen, process), new ProcessNetworkEndpoint(NetworkTransportProtocol.Udp, IPAddress.Any, 5353, null, null, null, new(2380, "svchost", null, null)) };
            return new(DateTimeOffset.Parse("2026-07-30T18:37:10+00:00"), all.Where(x => x.Protocol == NetworkTransportProtocol.Tcp ? (x.TcpState == TcpState.Listen ? query.IncludeTcpListeners : query.IncludeOtherTcpConnections) : query.IncludeUdpEndpoints).ToArray());
        }
        public ProcessTerminationResult TerminateProcess(ProcessIdentity identity, CancellationToken cancellationToken = default) => new(ProcessTerminationStatus.NotSupported, "Terminate process is disabled in demo mode.");
    }
    public static IReadOnlyList<FileSystemVolume> CreateVolumes() =>
    [
        new FileSystemVolume
        {
            Id = "demo",
            DisplayName = "[DEMO] /",
            RootPath = "/",
            Kind = VolumeKind.Pseudo,
            Status = VolumeStatus.Ready,
            Shortcut = "0",
        },
    ];

    public sealed class DisabledShellService : IShellService
    {
        public void Execute(string command, string workingDirectory) =>
            throw new InvalidOperationException("External commands are disabled in demo mode.");
    }

    public sealed class DisabledLocalFileSystemService : IFileSystemService
    {
        public IReadOnlyList<FilePanelItem> ReadDirectory(string path) => throw Disabled();

        public bool DirectoryExists(string path) => throw Disabled();

        public bool FileExists(string path) => throw Disabled();

        private static IOException Disabled() =>
            new("Local file system access is disabled in demo mode.");
    }

    public sealed class DisabledFileLauncher : IFileLauncher
    {
        public FileLaunchMode GetLaunchMode(string fullPath) => FileLaunchMode.AssociatedDetached;

        public void OpenFile(string fullPath, string workingDirectory) =>
            throw new InvalidOperationException("External file launching is disabled in demo mode.");
    }

    public sealed class DisabledFileSystemPlatformOperations : IFileSystemPlatformOperations
    {
        public bool SupportsRecycleBin => false;

        public void DeleteFile(string path, bool useRecycleBin) => throw Disabled();

        public void DeleteDirectory(string path, bool recursive, bool useRecycleBin) => throw Disabled();

        public bool IsSymbolicLink(string path) => throw Disabled();

        public bool TryCopySymbolicLink(string sourcePath, string destinationPath, out string? error) =>
            throw Disabled();

        public void PreserveFileMetadata(
            string sourcePath,
            string destinationPath,
            FileOperationOptions options,
            IFileOperationErrorSink errors) => throw Disabled();

        private static IOException Disabled() =>
            new("Local file system operations are disabled in demo mode.");
    }

    public sealed class EmptyCredentialStore : ICredentialStore
    {
        public void SavePassword(string credentialId, string password) { }

        public string? TryReadPassword(string credentialId) => null;

        public void DeletePassword(string credentialId) { }
    }

    public sealed class DemoVolumeService : IVolumeService
    {
        private readonly IReadOnlyList<FileSystemVolume> _volumes = CreateVolumes();

        public IReadOnlyList<FileSystemVolume> GetVolumes() => _volumes;
    }

    public sealed class DisabledSearchService : ISearchService
    {
        public IAsyncEnumerable<SearchResultItem> SearchAsync(
            SearchRequest request,
            IProgress<SearchProgress>? progress,
            CancellationToken cancellationToken = default) =>
            new DisabledSearchEnumerable(cancellationToken);

        private sealed class DisabledSearchEnumerable : IAsyncEnumerable<SearchResultItem>, IAsyncEnumerator<SearchResultItem>
        {
            private readonly CancellationToken _cancellationToken;

            public DisabledSearchEnumerable(CancellationToken cancellationToken)
            {
                _cancellationToken = cancellationToken;
            }

            public SearchResultItem Current => null!;

            public IAsyncEnumerator<SearchResultItem> GetAsyncEnumerator(CancellationToken cancellationToken = default) =>
                new DisabledSearchEnumerable(cancellationToken.CanBeCanceled ? cancellationToken : _cancellationToken);

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;

            public ValueTask<bool> MoveNextAsync()
            {
                if (_cancellationToken.IsCancellationRequested)
                    return ValueTask.FromCanceled<bool>(_cancellationToken);

                return ValueTask.FromException<bool>(
                    new IOException("Local filesystem search is disabled in this composition."));
            }
        }
    }
}

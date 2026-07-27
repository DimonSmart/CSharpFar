using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Bootstrap;

internal static class DemoModeServices
{
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

    public sealed class DisabledFileLauncher : IFileLauncher
    {
        public FileLaunchMode GetLaunchMode(string fullPath) => FileLaunchMode.ShellAssociation;

        public void OpenFile(string fullPath, string workingDirectory) =>
            throw new InvalidOperationException("External file launching is disabled in demo mode.");
    }

    public sealed class DemoVolumeService : IVolumeService
    {
        private readonly IReadOnlyList<FileSystemVolume> _volumes = CreateVolumes();

        public IReadOnlyList<FileSystemVolume> GetVolumes() => _volumes;
    }
}

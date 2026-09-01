using CSharpFar.App.Panels;
using CSharpFar.Core.Models;
using CSharpFar.Platform.Abstractions;

namespace CSharpFar.Tests;

public sealed class FileUsagePanelControllerTests
{
    [Fact]
    public async Task SelectionChangeCancelsAndLateResultCannotReplaceNewerFile()
    {
        var service = new ControlledService();
        using var controller = new FileUsagePanelController(service, () => { });
        controller.Update(true, PanelSourceId.Local, Item("first.txt"));
        ControlledService.Call first = await service.NextCall();
        controller.Update(true, PanelSourceId.Local, Item("second.txt"));
        ControlledService.Call second = await service.NextCall();

        Assert.True(first.Token.IsCancellationRequested);
        second.Complete(Snapshot("second.txt", Owner(2, "second")));
        await Eventually(() => controller.Snapshot?.Path == "second.txt");
        first.Complete(Snapshot("first.txt", Owner(1, "first")));
        await Task.Delay(20);
        Assert.Equal("second.txt", controller.Snapshot?.Path);
    }

    [Fact]
    public async Task RefreshCancelsPriorInspectionAndPreservesOwnerIdentity()
    {
        var service = new ControlledService();
        using var controller = new FileUsagePanelController(service, () => { });
        controller.Update(true, PanelSourceId.Local, Item("file.txt"));
        ControlledService.Call initial = await service.NextCall();
        FileUsageOwnerEntry retained = Owner(7, "retained");
        initial.Complete(Snapshot("file.txt", Owner(1, "other"), retained));
        await Eventually(() => controller.Snapshot is not null);
        controller.SelectOwner(1);

        controller.Refresh();
        ControlledService.Call refresh = await service.NextCall();
        Assert.True(controller.IsInspecting);
        Assert.NotNull(controller.Snapshot);
        refresh.Complete(Snapshot("file.txt", retained, Owner(8, "new")));
        await Eventually(() => !controller.IsInspecting);
        Assert.Equal(0, controller.SelectedOwnerIndex);
    }

    [Fact]
    public async Task CloseAndDisposeCancelPendingInspection()
    {
        var service = new ControlledService();
        var controller = new FileUsagePanelController(service, () => { });
        controller.Update(true, PanelSourceId.Local, Item("file.txt"));
        ControlledService.Call closing = await service.NextCall();
        controller.Update(false, PanelSourceId.Local, null);
        Assert.True(closing.Token.IsCancellationRequested);

        controller.Update(true, PanelSourceId.Local, Item("other.txt"));
        ControlledService.Call disposing = await service.NextCall();
        controller.Dispose();
        Assert.True(disposing.Token.IsCancellationRequested);
    }

    [Fact]
    public void UnsupportedTargetsHaveExplanatoryMessages()
    {
        using var controller = new FileUsagePanelController(new ControlledService(), () => { });
        controller.Update(true, PanelSourceId.Demo, Item("remote.txt"));
        Assert.Contains("local", controller.Message, StringComparison.OrdinalIgnoreCase);
        controller.Update(true, PanelSourceId.Local, new FilePanelItem { Name = "folder", FullPath = "C:/folder", IsDirectory = true });
        Assert.Contains("Directories", controller.Message);
    }

    private static FilePanelItem Item(string name) => new() { Name = name, FullPath = "C:/" + name, IsDirectory = false };
    private static FileUsageOwnerEntry Owner(int pid, string name) =>
        new(new ProcessSnapshot(pid, name, $"C:/{name}.exe", DateTimeOffset.UnixEpoch.AddSeconds(pid)));
    private static FileUsageSnapshot Snapshot(string path, params FileUsageOwnerEntry[] owners) =>
        new(path, DateTimeOffset.UtcNow, owners.Length == 0 ? FileUsageState.Free : FileUsageState.InUse, owners, []);
    private static async Task Eventually(Func<bool> condition)
    {
        for (int i = 0; i < 100 && !condition(); i++) await Task.Delay(5);
        Assert.True(condition());
    }

    private sealed class ControlledService : IFileUsagePlatformService
    {
        private readonly System.Threading.Channels.Channel<Call> _calls = System.Threading.Channels.Channel.CreateUnbounded<Call>();
        public FileUsageSupportInfo Support { get; } = new(true, false);
        public FileUsageSnapshot Inspect(string path, CancellationToken cancellationToken = default)
        {
            var call = new Call(path, cancellationToken);
            _calls.Writer.TryWrite(call);
            return call.Completion.Task.GetAwaiter().GetResult();
        }
        public FileUsageReleaseResult Release(FileUsageReleaseRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<Call> NextCall() => _calls.Reader.ReadAsync();
        public sealed class Call(string path, CancellationToken token)
        {
            public string Path { get; } = path;
            public CancellationToken Token { get; } = token;
            public TaskCompletionSource<FileUsageSnapshot> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public void Complete(FileUsageSnapshot snapshot) => Completion.TrySetResult(snapshot);
        }
    }
}

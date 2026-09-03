using CSharpFar.Core.Models;
using CSharpFar.Platform.Abstractions;

namespace CSharpFar.App.Panels;

internal sealed class FileUsagePanelController : IDisposable
{
    private readonly IFileUsagePlatformService _service;
    private readonly Action _wake;
    private readonly object _gate = new();
    private CancellationTokenSource? _cancellation;
    private long _operationId;
    private string? _path;
    private bool _disposed;

    public FileUsagePanelController(IFileUsagePlatformService service, Action wake)
    {
        _service = service;
        _wake = wake;
    }

    public FileUsageSnapshot? Snapshot { get; private set; }
    public bool IsInspecting { get; private set; }
    public string? Message { get; private set; }
    public int SelectedOwnerIndex { get; private set; } = -1;
    public bool IsReleasing { get; private set; }
    public long PresentationRevision { get; private set; }
    public bool CanUnlock => TryGetEligibleOwner(out _);

    public void Update(bool enabled, PanelSourceId sourceId, FilePanelItem? item)
    {
        lock (_gate)
        {
            if (_disposed) return;
            if (!enabled) { CancelAndClear(); return; }
            if (sourceId != PanelSourceId.Local)
            {
                SetExplanation("File Usage is available only for local file panels.");
                return;
            }
            if (item is null || item.IsParentDirectory)
            {
                SetExplanation("No file selected.");
                return;
            }
            if (item.IsDirectory)
            {
                SetExplanation("Directories cannot be inspected by File Usage.");
                return;
            }
            if (_path == item.FullPath) return;
            Start(item.FullPath, retainSnapshot: false);
        }
    }

    public void Refresh()
    {
        lock (_gate)
            if (!_disposed && _path is not null)
                Start(_path, retainSnapshot: true);
    }

    public bool RequestUnlock(Func<string, bool> confirm)
    {
        ArgumentNullException.ThrowIfNull(confirm);
        FileUsageOwnerEntry owner;
        string path;
        lock (_gate)
        {
            if (_disposed || IsReleasing || _path is null || !TryGetEligibleOwner(out owner)) return false;
            path = _path;
        }

        ProcessSnapshot process = owner.Process;
        string details = $"Process: {process.Name ?? "Unavailable"}\n" +
            $"PID: {process.ProcessId}\n" +
            $"Path: {process.ExecutablePath ?? "Unavailable"}\n" +
            $"Started: {process.StartedAt?.ToString("yyyy-MM-dd HH:mm:ss zzz") ?? "Unavailable"}\n\n" +
            "Unlocking terminates this process. Unsaved data may be lost. Continue?";
        if (!confirm(details)) return true;

        lock (_gate)
        {
            if (_disposed || IsReleasing || _path != path || !TryGetEligibleOwner(out FileUsageOwnerEntry current) ||
                current.Process.Identity != process.Identity) return true;
            StartRelease(path, current);
        }
        return true;
    }

    public bool MoveSelection(int offset)
    {
        lock (_gate)
        {
            int count = Snapshot?.Owners.Count ?? 0;
            if (count == 0) return false;
            int next = Math.Clamp(SelectedOwnerIndex < 0 ? 0 : SelectedOwnerIndex + offset, 0, count - 1);
            if (next != SelectedOwnerIndex) { SelectedOwnerIndex = next; PresentationRevision++; }
            return true;
        }
    }

    public bool SelectOwner(int index)
    {
        lock (_gate)
        {
            if (index < 0 || index >= (Snapshot?.Owners.Count ?? 0)) return false;
            if (SelectedOwnerIndex != index) { SelectedOwnerIndex = index; PresentationRevision++; }
            return true;
        }
    }

    public void NormalizeSelection(int visibleOwnerCount)
    {
        lock (_gate)
        {
            int count = Math.Min(visibleOwnerCount, Snapshot?.Owners.Count ?? 0);
            int next = count == 0 ? -1 : Math.Clamp(SelectedOwnerIndex < 0 ? 0 : SelectedOwnerIndex, 0, count - 1);
            if (next != SelectedOwnerIndex) { SelectedOwnerIndex = next; PresentationRevision++; }
        }
    }

    private void SetExplanation(string message)
    {
        if (_path is null && Message == message) return;
        CancelInspection();
        _path = null; Snapshot = null; IsInspecting = false; IsReleasing = false; Message = message; SelectedOwnerIndex = -1;
        PresentationRevision++;
    }

    private void Start(string path, bool retainSnapshot, bool retainMessage = false)
    {
        ProcessIdentity? selected = SelectedOwnerIndex >= 0 && SelectedOwnerIndex < (Snapshot?.Owners.Count ?? 0)
            ? Snapshot!.Owners[SelectedOwnerIndex].Process.Identity : null;
        CancelInspection();
        long id = ++_operationId;
        PresentationRevision++;
        var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;
        _path = path;
        if (!retainSnapshot) Snapshot = null;
        IsReleasing = false;
        IsInspecting = true;
        if (!retainMessage) Message = null;
        _ = Task.Run(() => _service.Inspect(path, cancellation.Token), cancellation.Token).ContinueWith(task =>
        {
            lock (_gate)
            {
                if (_disposed || id != _operationId || cancellation.IsCancellationRequested || task.IsCanceled) return;
                if (task.IsFaulted)
                {
                    Message = task.Exception?.GetBaseException().Message ?? "File Usage inspection failed.";
                }
                else
                {
                    Snapshot = task.Result;
                    if (task.Result.Error is not null || !retainMessage)
                        Message = task.Result.Error?.Message;
                    SelectedOwnerIndex = FindOwner(task.Result.Owners, selected);
                }
                // Publish completion only after the refreshed snapshot and its
                // preserved selection have both been installed. Consumers use
                // IsInspecting as the signal that the result is ready to render.
                IsInspecting = false;
                PresentationRevision++;
            }
            _wake();
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private void StartRelease(string path, FileUsageOwnerEntry owner)
    {
        CancelInspection();
        long id = ++_operationId;
        var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;
        IsReleasing = true;
        Message = "Unlocking owner...";
        var request = new FileUsageReleaseRequest(owner.Process.Identity!, owner.Kind);
        _ = Task.Run(() => _service.Release(request, cancellation.Token), cancellation.Token).ContinueWith(task =>
        {
            lock (_gate)
            {
                if (_disposed || id != _operationId || cancellation.IsCancellationRequested || task.IsCanceled) return;
                IsReleasing = false;
                Message = task.IsFaulted
                    ? $"Unlock failed: {task.Exception?.GetBaseException().Message ?? "Unknown error."}"
                    : FormatReleaseResult(task.Result);
                Start(path, retainSnapshot: true, retainMessage: true);
            }
            _wake();
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private bool TryGetEligibleOwner(out FileUsageOwnerEntry owner)
    {
        owner = null!;
        if (!_service.Support.CanReleaseOwners || IsReleasing || SelectedOwnerIndex < 0 ||
            SelectedOwnerIndex >= (Snapshot?.Owners.Count ?? 0)) return false;
        owner = Snapshot!.Owners[SelectedOwnerIndex];
        return owner.Kind != FileUsageOwnerKind.Service && owner.Process.Identity is not null;
    }

    private static string FormatReleaseResult(FileUsageReleaseResult result)
    {
        string outcome = result.Status switch
        {
            FileUsageReleaseStatus.Success => "Owner released successfully.",
            FileUsageReleaseStatus.AlreadyExited => "The owner has already exited.",
            FileUsageReleaseStatus.StaleIdentity => "The process identity is stale; no process was terminated.",
            FileUsageReleaseStatus.CurrentProcess => "CSharpFar cannot terminate itself.",
            FileUsageReleaseStatus.AccessDenied => "Access denied while unlocking the owner.",
            FileUsageReleaseStatus.IneligibleOwner => "This owner is not eligible for Unlock.",
            FileUsageReleaseStatus.NotSupported => "Unlock is not supported.",
            _ => "Unlock failed.",
        };
        return string.IsNullOrWhiteSpace(result.Message) ? outcome : $"{outcome} {result.Message}";
    }

    private static int FindOwner(IReadOnlyList<FileUsageOwnerEntry> owners, ProcessIdentity? selected)
    {
        if (owners.Count == 0) return -1;
        if (selected is not null)
        {
            int match = owners.ToList().FindIndex(owner => owner.Process.Identity == selected);
            if (match >= 0) return match;
        }
        return 0;
    }

    private void CancelInspection()
    {
        ++_operationId;
        _cancellation?.Cancel();
        _cancellation?.Dispose();
        _cancellation = null;
    }

    private void CancelAndClear()
    {
        CancelInspection();
        _path = null; Snapshot = null; IsInspecting = false; IsReleasing = false; Message = null; SelectedOwnerIndex = -1;
        PresentationRevision++;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            CancelAndClear();
            _disposed = true;
        }
    }
}

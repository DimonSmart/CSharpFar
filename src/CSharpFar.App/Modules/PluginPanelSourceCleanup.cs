using System.Runtime.CompilerServices;
using CSharpFar.Core.Abstractions;

namespace CSharpFar.App.Modules;

/// <summary>
/// Owns cleanup of rejected panel instances, not live sources in the source registry.
/// Never waits for plugin code on the UI thread; observes failures even after an await.
/// </summary>
internal sealed class PluginPanelSourceCleanup(Action<Exception>? reportError)
{
    private readonly object _gate = new();
    private readonly ConditionalWeakTable<IFilePanelSource, object> _discarded = new();
    private readonly HashSet<Task> _pending = [];

    public void Discard(IFilePanelSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        lock (_gate)
        {
            // Multiple rejection paths must not dispose the same source twice.
            if (_discarded.TryGetValue(source, out _))
                return;
            _discarded.Add(source, new object());
        }

        Task cleanup = DisposeAndReportAsync(source);
        bool tracked;
        lock (_gate)
        {
            tracked = !cleanup.IsCompleted;
            if (tracked)
                _pending.Add(cleanup);
        }

        if (tracked)
            _ = ObserveAndUntrackAsync(cleanup);
    }

    public async Task AwaitPendingAsync()
    {
        while (true)
        {
            Task[] tasks;
            lock (_gate)
            {
                tasks = [.. _pending];
            }
            if (tasks.Length == 0)
                return;

            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
    }

    private async Task DisposeAndReportAsync(IFilePanelSource source)
    {
        try
        {
            if (source is IAsyncDisposable asyncDisposable)
                await asyncDisposable.DisposeAsync().ConfigureAwait(false);
            else if (source is IDisposable disposable)
                disposable.Dispose();
        }
        catch (Exception error)
        {
            try { reportError?.Invoke(error); }
            catch { /* Diagnostics must not alter a panel operation or crash the input loop. */ }
        }
    }

    private async Task ObserveAndUntrackAsync(Task cleanup)
    {
        try
        {
            await cleanup.ConfigureAwait(false);
        }
        finally
        {
            lock (_gate)
                _pending.Remove(cleanup);
        }
    }
}

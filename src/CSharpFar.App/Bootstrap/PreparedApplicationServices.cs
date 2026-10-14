using CSharpFar.App.Diagnostics;
using CSharpFar.App.Plugins;
using CSharpFar.Plugin.Abstractions;

namespace CSharpFar.App.Bootstrap;

/// <summary>A single composition attempt, with one registration owner and one finalizer.</summary>
internal sealed class PreparedApplicationServices(
    NativePluginRuntime runtime,
    IReadOnlyList<(PluginDescriptor Descriptor, ICSharpFarPlugin Plugin)> composition,
    List<string> failures,
    IDiagnosticLog diagnostics,
    Func<ApplicationServices> finish,
    Action releaseFailedPreparation)
{
    public ApplicationServices BuildSynchronously()
    {
        var cancellation = new CancellationTokenSource();
        Task<PluginInitializationResult>? pending = null;
        try
        {
            foreach (var (descriptor, plugin) in composition)
            {
                if (!Valid(descriptor, plugin))
                    continue;

                var registration = runtime.RegisterAsync(descriptor, plugin, cancellation.Token);
                if (!registration.IsCompleted)
                {
                    pending = registration.AsTask();
                    throw new InvalidOperationException(
                        $"Plugin '{descriptor.Id}' ({descriptor.Title}) has unfinished asynchronous initialization. " +
                        "Use ApplicationFactory.CreateAsync.");
                }

                Record(descriptor, registration.GetAwaiter().GetResult());
            }

            runtime.Seal();
            var completed = finish();
            cancellation.Dispose();
            return completed;
        }
        catch
        {
            runtime.Abort();
            // Cancellation callbacks belong to arbitrary plugin code. Do not execute
            // them synchronously on the terminal/bootstrap thread.
            Task cancellationCompletion = RequestCancellationAsync(cancellation);
            ReleasePreparedResources();
            // The pending task is observed and owned resources are eventually released.
            _ = ObserveAndDisposeAsync(pending, cancellation, cancellationCompletion);
            throw;
        }
    }

    public async Task<ApplicationServices> BuildAsynchronously(CancellationToken cancellationToken)
    {
        Task<PluginInitializationResult>? pending = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var (descriptor, plugin) in composition)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!Valid(descriptor, plugin))
                    continue;

                pending = runtime.RegisterAsync(descriptor, plugin, cancellationToken).AsTask();
                var result = await pending.WaitAsync(cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                pending = null;
                Record(descriptor, result);
            }

            cancellationToken.ThrowIfCancellationRequested();
            runtime.Seal();
            cancellationToken.ThrowIfCancellationRequested();
            return finish();
        }
        catch
        {
            runtime.Abort();
            ReleasePreparedResources();
            if (pending is { IsCompleted: false })
                _ = ObserveAndDisposeAsync(pending);
            else
                await runtime.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private bool Valid(PluginDescriptor? descriptor, ICSharpFarPlugin? plugin)
    {
        if (descriptor is not null && plugin is not null)
            return true;

        RecordFailure(descriptor?.Id ?? "<null>",
            "Plugin descriptor and instance must both be non-null.");
        return false;
    }

    private void Record(PluginDescriptor descriptor, PluginInitializationResult result)
    {
        if (!result.Success)
            RecordFailure(descriptor.Id, result.Error ?? "Initialization failed.");
        else if (diagnostics.IsEnabled)
        {
            try { diagnostics.Write(DiagnosticCategory.Plugin, $"Plugin '{descriptor.Id}' initialized."); }
            catch { /* Logging must not affect a successful registration. */ }
        }
    }

    private void RecordFailure(string pluginId, string reason)
    {
        string message = DiagnosticSanitizer.SanitizeText($"Plugin '{pluginId}': {reason}");
        failures.Add(message);
        try
        {
            if (diagnostics.IsEnabled)
                diagnostics.Write(DiagnosticCategory.Plugin, message);
        }
        catch { /* Diagnostic transport must never influence composition. */ }
    }

    private void ReleasePreparedResources()
    {
        try { releaseFailedPreparation(); }
        catch (Exception error)
        {
            try { diagnostics.WriteException(DiagnosticCategory.Plugin, error, "Bootstrap cleanup"); }
            catch { }
        }
    }

    private static async Task RequestCancellationAsync(CancellationTokenSource source)
    {
        try { await source.CancelAsync().ConfigureAwait(false); }
        catch { /* Plugin cancellation callbacks must not hide the original error. */ }
    }

    private async Task ObserveAndDisposeAsync(
        Task<PluginInitializationResult>? pending,
        CancellationTokenSource? cancellation = null,
        Task? cancellationCompletion = null)
    {
        try
        {
            if (pending is not null)
                await pending.ConfigureAwait(false);
        }
        catch { /* Observe the late task, including cancellation and faults. */ }
        finally
        {
            try { await runtime.DisposeAsync().ConfigureAwait(false); }
            catch (Exception error)
            {
                try { diagnostics.WriteException(DiagnosticCategory.Plugin, error, "Bootstrap cleanup"); }
                catch { }
            }
            if (cancellationCompletion is not null)
                await cancellationCompletion.ConfigureAwait(false);
            cancellation?.Dispose();
        }
    }
}

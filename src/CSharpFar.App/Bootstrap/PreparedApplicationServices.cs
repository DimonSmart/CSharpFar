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
            cancellation.Cancel();
            ReleasePreparedResources();
            // The pending in-process plugin cannot be forcibly stopped. Its final
            // outcome is observed, and runtime ownership is cleaned up when it ends.
            _ = ObserveAndDisposeAsync(pending, cancellation);
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
            diagnostics.Write(DiagnosticCategory.Plugin, $"Plugin '{descriptor.Id}' initialized.");
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

    private async Task ObserveAndDisposeAsync(
        Task<PluginInitializationResult>? pending, CancellationTokenSource? cancellation = null)
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
            cancellation?.Dispose();
        }
    }
}

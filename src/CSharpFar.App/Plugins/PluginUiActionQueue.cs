using System.Collections.Concurrent;

namespace CSharpFar.App.Plugins;

/// <summary>Returns asynchronous plugin completions to the application's owning input/UI thread.</summary>
internal sealed class PluginUiActionQueue(Action wakeInputLoop, Action<Exception>? reportError = null)
{
    private readonly ConcurrentQueue<Action> _actions = new();

    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        _actions.Enqueue(action);
        try { wakeInputLoop(); }
        catch (Exception error)
        {
            // The action is already queued; reporting the wake failure must not
            // make its producer dispose a result that the queue still owns.
            try { reportError?.Invoke(error); }
            catch { /* Diagnostics must not terminate a plugin operation. */ }
        }
    }

    public void Drain()
    {
        while (_actions.TryDequeue(out var action))
        {
            try { action(); }
            catch (Exception error)
            {
                try { reportError?.Invoke(error); }
                catch { /* Diagnostics must not terminate the input loop. */ }
            }
        }
    }
}

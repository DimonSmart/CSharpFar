using System.Collections.Concurrent;

namespace CSharpFar.App.Plugins;

/// <summary>Returns asynchronous plugin completions to the application's owning input/UI thread.</summary>
internal sealed class PluginUiActionQueue(Action wakeInputLoop)
{
    private readonly ConcurrentQueue<Action> _actions = new();

    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        _actions.Enqueue(action);
        wakeInputLoop();
    }

    public void Drain()
    {
        while (_actions.TryDequeue(out var action))
            action();
    }
}

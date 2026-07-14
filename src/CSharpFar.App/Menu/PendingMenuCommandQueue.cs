using CSharpFar.Core.Menu;

namespace CSharpFar.App.Menu;

internal sealed class PendingMenuCommandQueue
{
    private MenuCommandRequest? _pending;

    public MenuCommandResult Enqueue(MenuCommandRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (_pending is not null)
            throw new InvalidOperationException("A menu command was requested before the previous command was consumed.");

        _pending = request;
        return new MenuCommandResult { Success = true };
    }

    public bool TryTake(out MenuCommandRequest request)
    {
        if (_pending is null)
        {
            request = null!;
            return false;
        }

        request = _pending;
        _pending = null;
        return true;
    }
}

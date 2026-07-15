using CSharpFar.App.Menu;
using CSharpFar.Core.Menu;

namespace CSharpFar.Tests;

public sealed class PendingMenuCommandQueueTests
{
    [Fact]
    public void EnqueueAndTakeReturnsRequestThenClearsQueue()
    {
        var queue = new PendingMenuCommandQueue();
        var request = Request("copy");

        var result = queue.Enqueue(request);

        Assert.True(result.Success);
        Assert.True(queue.TryTake(out var taken));
        Assert.Same(request, taken);
        Assert.False(queue.TryTake(out _));
    }

    [Fact]
    public void SecondEnqueueBeforeTakeThrowsDeterministicException()
    {
        var queue = new PendingMenuCommandQueue();
        queue.Enqueue(Request("first"));

        var ex = Assert.Throws<InvalidOperationException>(() => queue.Enqueue(Request("second")));

        Assert.Contains("previous command was consumed", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void QueueStoresButDoesNotExecuteCommand()
    {
        var queue = new PendingMenuCommandQueue();
        bool executed = false;
        var request = new MenuCommandRequest
        {
            CommandId = "copy",
            Args = new Action(() => executed = true),
        };

        queue.Enqueue(request);

        Assert.False(executed);
    }

    private static MenuCommandRequest Request(string commandId) => new()
    {
        CommandId = commandId,
    };
}

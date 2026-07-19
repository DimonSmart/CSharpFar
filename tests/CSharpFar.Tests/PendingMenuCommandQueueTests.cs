using CSharpFar.App.Menu;
using CSharpFar.Core.Menu;

namespace CSharpFar.Tests;

public sealed class PendingMenuCommandQueueTests
{
    [Fact]
    public void QueueRejectsSecondPendingRequestAndAcceptsAfterTake()
    {
        var queue = new PendingMenuCommandQueue();
        queue.Enqueue(Request("first"));

        Assert.Throws<InvalidOperationException>(() => queue.Enqueue(Request("second")));

        Assert.True(queue.TryTake(out _));
        var result = queue.Enqueue(Request("third"));

        Assert.True(result.Success);
    }

    private static MenuCommandRequest Request(string commandId) => new()
    {
        CommandId = commandId,
    };
}

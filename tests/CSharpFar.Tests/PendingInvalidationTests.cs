using CSharpFar.App;

namespace CSharpFar.Tests;

public sealed class PendingInvalidationTests
{
    [Fact]
    public void Commit_ClearsOnlyRequestsRepresentedByAcceptedAttempt()
    {
        var pending = new PendingInvalidation<TestPart>(TestPart.Full);
        pending.Request(TestPart.First);
        pending.Request(TestPart.Second);

        PendingInvalidationSnapshot<TestPart> attempt = pending.SnapshotForRenderAttempt();
        pending.Request(TestPart.First);
        pending.Commit(attempt);

        Assert.Equal(TestPart.First, pending.SnapshotForRenderAttempt().Parts);
    }

    [Fact]
    public void Full_DominatesSnapshotAndPreservesNewerRequests()
    {
        var pending = new PendingInvalidation<TestPart>(TestPart.Full);
        pending.Request(TestPart.First);
        pending.RequestFull();

        PendingInvalidationSnapshot<TestPart> attempt = pending.SnapshotForRenderAttempt();
        pending.Request(TestPart.Second);
        pending.Commit(attempt);

        Assert.Equal(TestPart.Full, attempt.Parts);
        Assert.Equal(TestPart.Second, pending.SnapshotForRenderAttempt().Parts);
    }

    [Fact]
    public void RejectedAttempt_LeavesMergedRequestsPending()
    {
        var pending = new PendingInvalidation<TestPart>(TestPart.Full);
        pending.Request(TestPart.First);
        _ = pending.SnapshotForRenderAttempt();
        pending.Request(TestPart.Second);

        Assert.Equal(
            TestPart.First | TestPart.Second,
            pending.SnapshotForRenderAttempt().Parts);
    }

    [Flags]
    private enum TestPart
    {
        None = 0,
        First = 1,
        Second = 2,
        Full = 1 << 30,
    }
}

using CSharpFar.Ui;

namespace CSharpFar.Tests;

public sealed class UiThemeTests
{
    [Fact]
    public async Task UseTemporary_DoesNotLeakAcrossParallelExecutionContexts()
    {
        var temporary = new ConsolePalette { Name = "Temporary test theme" };
        var temporaryEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var siblingObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ConsolePalette? siblingTheme = null;

        Task temporaryTask = Task.Run(async () =>
        {
            using (UiTheme.UseTemporary(temporary))
            {
                temporaryEntered.SetResult();
                await siblingObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));
                Assert.Same(temporary, UiTheme.Current);
            }
        });

        Task siblingTask = Task.Run(async () =>
        {
            await temporaryEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            siblingTheme = UiTheme.Current;
            siblingObserved.SetResult();
        });

        await Task.WhenAll(temporaryTask, siblingTask);

        Assert.NotSame(temporary, siblingTheme);
        Assert.NotSame(temporary, UiTheme.Current);
    }
}

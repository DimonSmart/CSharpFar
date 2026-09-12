using CSharpFar.Ui;

namespace CSharpFar.Tests;

public sealed class UiThemeTests
{
    [Fact]
    public void UseTemporary_DoesNotLeakAcrossParallelExecutionContexts()
    {
        var temporary = new ConsolePalette { Name = "Temporary test theme" };
        using var temporaryEntered = new ManualResetEventSlim(false);
        using var siblingObserved = new ManualResetEventSlim(false);
        ConsolePalette? siblingTheme = null;

        Task temporaryTask = Task.Run(() =>
        {
            using (UiTheme.UseTemporary(temporary))
            {
                temporaryEntered.Set();
                Assert.True(siblingObserved.Wait(TimeSpan.FromSeconds(2)));
                Assert.Same(temporary, UiTheme.Current);
            }
        });

        Task siblingTask = Task.Run(() =>
        {
            Assert.True(temporaryEntered.Wait(TimeSpan.FromSeconds(2)));
            siblingTheme = UiTheme.Current;
            siblingObserved.Set();
        });

        Task.WaitAll(temporaryTask, siblingTask);

        Assert.NotSame(temporary, siblingTheme);
        Assert.NotSame(temporary, UiTheme.Current);
    }
}

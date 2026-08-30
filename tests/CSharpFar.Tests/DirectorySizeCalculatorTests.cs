using CSharpFar.App.Viewer;

namespace CSharpFar.Tests;

public sealed class DirectorySizeCalculatorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"CSharpFarDirectorySize_{Guid.NewGuid():N}");

    public DirectorySizeCalculatorTests()
    {
        Directory.CreateDirectory(_root);
        for (int i = 0; i < 3; i++)
        {
            string child = Path.Combine(_root, i.ToString());
            Directory.CreateDirectory(child);
            File.WriteAllBytes(Path.Combine(child, "file.bin"), new byte[16]);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Start_UsesTheProgressModeOfThatScanOperation(
        bool expectsProgress)
    {
        int progressCount = 0;
        var completed = new TaskCompletionSource<DirectoryScanUpdate>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var calculator = new DirectorySizeCalculator(throttleMs: 0);
        calculator.Progress += _ => Interlocked.Increment(ref progressCount);
        calculator.Completed += update => completed.TrySetResult(update);

        long operationId = calculator.Start(
            _root,
            expectsProgress ? DirectoryScanProgressMode.ReportProgress : DirectoryScanProgressMode.Silent);
        DirectoryScanUpdate result = await completed.Task.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.Equal(operationId, result.OperationId);
        Assert.True(result.State.IsCompleted);
        Assert.Equal(expectsProgress, Volatile.Read(ref progressCount) > 0);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}

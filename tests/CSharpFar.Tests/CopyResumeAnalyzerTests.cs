using CSharpFar.FileSystem;

namespace CSharpFar.Tests;

public sealed class CopyResumeAnalyzerTests : IDisposable
{
    private readonly string _root;

    public CopyResumeAnalyzerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"CSharpFarCopyResume_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void CanResume_WhenDestinationPrefixMatchesSource()
    {
        string source = CreateDeterministicFile("source.bin", 2 * 1024 * 1024);
        string destination = CopyPrefix(source, "destination.bin", 1280 * 1024);

        CopyResumePlan plan = new CopyResumeAnalyzer().Analyze(source, destination);

        Assert.Equal(CopyResumePlanKind.CanResume, plan.Kind);
        Assert.Equal(new FileInfo(destination).Length, plan.SafeResumeOffset);
        Assert.Equal(0, plan.RollbackBytes);
    }

    [Fact]
    public void CanResumeFromZero_WhenDestinationIsEmpty()
    {
        string source = CreateDeterministicFile("source.bin", 1024);
        string destination = Path.Combine(_root, "destination.bin");
        File.WriteAllBytes(destination, []);

        CopyResumePlan plan = new CopyResumeAnalyzer().Analyze(source, destination);

        Assert.Equal(CopyResumePlanKind.CanResume, plan.Kind);
        Assert.Equal(0, plan.SafeResumeOffset);
    }

    [Fact]
    public void CannotResume_WhenDestinationIsLargerThanSource()
    {
        string source = CreateDeterministicFile("source.bin", 1024);
        string destination = CreateDeterministicFile("destination.bin", 2048);

        CopyResumePlan plan = new CopyResumeAnalyzer().Analyze(source, destination);

        Assert.Equal(CopyResumePlanKind.CannotResume, plan.Kind);
        Assert.Contains("larger", plan.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AlreadyComplete_WhenLengthsAreEqual()
    {
        string source = CreateDeterministicFile("source.bin", 1024);
        string destination = CreateDeterministicFile("destination.bin", 1024);

        CopyResumePlan plan = new CopyResumeAnalyzer().Analyze(source, destination);

        Assert.Equal(CopyResumePlanKind.AlreadyComplete, plan.Kind);
    }

    [Fact]
    public void RollsBack_WhenTailDoesNotMatch()
    {
        string source = CreateDeterministicFile("source.bin", 8 * 1024 * 1024);
        string destination = CopyPrefix(source, "destination.bin", 6 * 1024 * 1024);
        CorruptRange(destination, startOffset: (6 * 1024 * 1024) - (32 * 1024), length: 32 * 1024);

        CopyResumePlan plan = new CopyResumeAnalyzer().Analyze(source, destination);

        Assert.Equal(CopyResumePlanKind.CanResume, plan.Kind);
        Assert.True(plan.SafeResumeOffset < new FileInfo(destination).Length);
        Assert.True(plan.RollbackBytes > 0);
    }

    [Fact]
    public void CannotResume_WhenNoMatchingRangeFound()
    {
        string source = CreateDeterministicFile("source.bin", 8 * 1024 * 1024);
        string destination = Path.Combine(_root, "destination.bin");
        File.WriteAllBytes(destination, Enumerable.Repeat((byte)255, 6 * 1024 * 1024).ToArray());

        CopyResumePlan plan = new CopyResumeAnalyzer().Analyze(source, destination);

        Assert.Equal(CopyResumePlanKind.CannotResume, plan.Kind);
        Assert.Contains("No matching", plan.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CannotResume_WhenSourceSnapshotChanged()
    {
        string source = CreateDeterministicFile("source.bin", 2 * 1024 * 1024);
        string destination = CopyPrefix(source, "destination.bin", 1024 * 1024);
        var snapshot = new CopyResumeSourceSnapshot(
            new FileInfo(source).Length,
            File.GetLastWriteTimeUtc(source).AddSeconds(-1));

        CopyResumePlan plan = new CopyResumeAnalyzer().Analyze(source, destination, snapshot);

        Assert.Equal(CopyResumePlanKind.CannotResume, plan.Kind);
        Assert.Contains("changed", plan.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CannotResume_ReportsSourceReadFailure_WhenSourceIsMissing()
    {
        string source = Path.Combine(_root, "missing.bin");
        string destination = CreateDeterministicFile("destination.bin", 1024);

        CopyResumePlan plan = new CopyResumeAnalyzer().Analyze(source, destination);

        Assert.Equal(CopyResumePlanKind.CannotResume, plan.Kind);
        Assert.Equal(CopyResumeReadFailureSide.Source, plan.ReadFailureSide);
    }

    private string CreateDeterministicFile(string name, int length)
    {
        string path = Path.Combine(_root, name);
        byte[] bytes = new byte[length];
        for (int i = 0; i < bytes.Length; i++)
            bytes[i] = (byte)(i % 251);

        File.WriteAllBytes(path, bytes);
        return path;
    }

    private string CopyPrefix(string source, string name, int length)
    {
        string destination = Path.Combine(_root, name);
        byte[] bytes = File.ReadAllBytes(source);
        File.WriteAllBytes(destination, bytes[..length]);
        return destination;
    }

    private static void CorruptRange(string path, int startOffset, int length)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None);
        stream.Position = startOffset;
        for (int i = 0; i < length; i++)
            stream.WriteByte(255);
    }
}

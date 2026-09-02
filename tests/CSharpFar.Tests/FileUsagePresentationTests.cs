using CSharpFar.App.Rendering;
using CSharpFar.Platform.Abstractions;

namespace CSharpFar.Tests;

public sealed class FileUsagePresentationTests
{
    [Theory]
    [InlineData("ab", 1, "…")]
    [InlineData("ab", 2, "ab")]
    public void Ellipsize_UsesVisibleEllipsisWithinWidth(string value, int width, string expected)
    {
        Assert.Equal(expected, FileUsagePresentation.Ellipsize(value, width));
        Assert.True(expected.Length <= width);
    }

    [Theory]
    [InlineData(48)]
    [InlineData(25)]
    public void LongWriteError_WrapsInAnIndentedReasonBlock(int width)
    {
        FileUsageLayout layout = FileUsagePresentation.Build(Blocked("The system cannot open this file because another process has denied write sharing access."),
            false, null, -1, false, width, 30);

        int write = Index(layout, "Write: BLOCKED");
        FileUsageRow[] reasons = layout.Body.Skip(write + 1).TakeWhile(row => row.Text.StartsWith("  Reason:") || row.Text.StartsWith("          ")).ToArray();
        Assert.NotEmpty(reasons);
        Assert.StartsWith("  Reason: ", reasons[0].Text);
        Assert.All(reasons.Skip(1), row => Assert.StartsWith("          ", row.Text));
        Assert.Equal("The system cannot open this file because another process has denied write sharing access.",
            string.Join(' ', reasons.Select(row => row.Text.Trim()).Select(text => text.Replace("Reason: ", ""))).Trim());
    }

    [Fact]
    public void BlockedAndAllowedValues_HaveDifferentSemanticStyles()
    {
        FileUsageLayout layout = FileUsagePresentation.Build(Blocked("sharing violation"), false, null, -1, false, 50, 20);
        Assert.Contains(layout.Body.Single(row => row.Text.StartsWith("State:")).Runs, run => run.Text == "BLOCKED" && run.Style == FileUsageStyleRole.Blocked);
        Assert.Contains(layout.Body.Single(row => row.Text.StartsWith("Read:")).Runs, run => run.Text == "Allowed" && run.Style == FileUsageStyleRole.Secondary);
        Assert.Contains(layout.Body.Single(row => row.Text.StartsWith("Write:")).Runs, run => run.Text == "BLOCKED" && run.Style == FileUsageStyleRole.Blocked);
    }

    [Fact]
    public void Owners_AreStableSelectedAndRetainNameAndPid()
    {
        FileUsageLayout layout = FileUsagePresentation.Build(Snapshot([Owner(7, "editor"), Owner(91, "compiler")]), false, null, 1, false, 35, 20);
        FileUsageRow[] owners = layout.Body.Where(row => row.OwnerIndex is not null).ToArray();
        Assert.Equal(new int?[] { 0, 1 }, owners.Select(row => row.OwnerIndex).ToArray());
        Assert.Contains("editor", owners[0].Text); Assert.Contains("PID 7", owners[0].Text);
        Assert.Contains("compiler", owners[1].Text); Assert.Contains("PID 91", owners[1].Text);
        Assert.All(owners[1].Runs, run => Assert.Equal(FileUsageStyleRole.SelectedOwner, run.Style));
    }

    [Fact]
    public void UnlockHint_IsASeparateConditionalAction()
    {
        FileUsageSnapshot snapshot = Snapshot([Owner(7, "editor")]);
        FileUsageLayout eligible = FileUsagePresentation.Build(snapshot, false, null, 0, true, 40, 20);
        FileUsageLayout ineligible = FileUsagePresentation.Build(snapshot, false, null, 0, false, 40, 20);
        Assert.Equal("Ctrl+U Unlock owner", eligible.Action?.Text);
        Assert.Equal([FileUsageStyleRole.ActionKey, FileUsageStyleRole.ActionLabel], eligible.Action!.Runs.Select(run => run.Style).ToArray());
        Assert.DoesNotContain(eligible.Body, row => row.Text.Contains("Unlock owner"));
        Assert.Null(ineligible.Action);
    }

    [Fact]
    public void LongPath_IsEllipsizedAsLowPriorityDetail()
    {
        const int width = 32;
        FileUsageSnapshot snapshot = Snapshot([Owner(7, "editor", "C:/a/very/long/executable/path/that/will/not/fit/editor.exe")]);
        FileUsageRow path = FileUsagePresentation.Build(snapshot, false, null, 0, false, width, 20).Body.Single(row => row.Text.TrimStart().StartsWith("Path:"));
        Assert.EndsWith("…", path.Text); Assert.DoesNotContain("editor.exe", path.Text);
        Assert.True(path.Text.Length <= width);
        Assert.Equal(FileUsageStyleRole.Secondary, path.Runs[0].Style);
    }

    [Fact]
    public void UnbreakableDiagnosticToken_IsEllipsizedWithinWidth()
    {
        const int width = 25;
        FileUsageLayout layout = FileUsagePresentation.Build(Blocked("AccessDeniedByAnotherProcessWithoutAnyBreaks"),
            false, null, -1, false, width, 20);

        FileUsageRow reason = layout.Body.Single(row => row.Text.StartsWith("  Reason:"));
        Assert.EndsWith("…", reason.Text);
        Assert.DoesNotContain(':', reason.Text[(reason.Text.IndexOf(':') + 1)..]);
        Assert.True(reason.Text.Length <= width);
    }

    [Fact]
    public void ConstrainedHeight_KeepsReasonAndOwnersBeforeDetails()
    {
        FileUsageLayout layout = FileUsagePresentation.Build(Blocked("write access is denied by another system process", [Owner(7, "editor")]), false, null, 0, false, 35, 9);
        Assert.Contains(layout.Body, row => row.Text.StartsWith("  Reason:"));
        Assert.Contains(layout.Body, row => row.Text == "Owners:");
        Assert.DoesNotContain(layout.Body, row => row.Text.TrimStart().StartsWith("Path:"));
        Assert.True(Index(layout, "Reason:") < Index(layout, "Owners:"));
    }

    private static int Index(FileUsageLayout layout, string text) => layout.Body.ToList().FindIndex(row => row.Text.Contains(text));
    private static FileUsageSnapshot Blocked(string reason, IReadOnlyList<FileUsageOwnerEntry>? owners = null) =>
        new("C:/file.txt", DateTimeOffset.UnixEpoch, FileUsageState.Blocked, owners ?? [],
        [
            new(FileUsageOperation.Read, FileUsageProbeStatus.Allowed),
            new(FileUsageOperation.Write, FileUsageProbeStatus.Blocked, new(FileUsageErrorKind.PlatformError, reason)),
            new(FileUsageOperation.Delete, FileUsageProbeStatus.Allowed),
            new(FileUsageOperation.Rename, FileUsageProbeStatus.Allowed),
        ]);
    private static FileUsageSnapshot Snapshot(IReadOnlyList<FileUsageOwnerEntry> owners) =>
        new("C:/file.txt", DateTimeOffset.UnixEpoch, FileUsageState.InUse, owners, []);
    private static FileUsageOwnerEntry Owner(int pid, string name, string? path = null) =>
        new(new ProcessSnapshot(pid, name, path, DateTimeOffset.UnixEpoch));
}

using CSharpFar.Ui.Demo;

namespace CSharpFar.Tests;

public sealed class UiDemoWorkflowTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 9, 4, 12, 34, 56, TimeSpan.Zero);

    [Fact]
    public void CreateCommit_IsDeterministicAndAdvancesCurrentBranch()
    {
        var repository = new DemoRepository(() => FixedNow);
        DemoCommit first = repository.CreateCommit("  Test Author  ", "  Subject  ", "Feature", amend: true, signOff: true);

        Assert.Equal("d0019919", first.Hash);
        Assert.Equal("main", first.Branch);
        Assert.Equal("Test Author", first.Author);
        Assert.Equal(FixedNow, first.Timestamp);
        Assert.Equal("feature: Subject (signed-off)", first.Subject);
        Assert.Equal(3, first.ChangedFiles.Count);
        Assert.Equal(first.Hash, repository.CurrentBranch.TipHash);
        Assert.Same(first, repository.Commits[0]);

        repository.Reset();
        Assert.Equal(first.Hash, repository.CreateCommit("Test Author", "Subject", "Feature", true, true).Hash);
    }

    [Fact]
    public void CommitMutation_ValidatesInputAndDeletionIsPredictable()
    {
        var repository = new DemoRepository(() => FixedNow);
        Assert.Throws<ArgumentException>(() => repository.CreateCommit("", "subject", "Commit", false, false));
        Assert.Throws<ArgumentException>(() => repository.CreateCommit("author", " ", "Commit", false, false));

        string hash = repository.Commits[1].Hash;
        Assert.True(repository.DeleteCommit(hash));
        Assert.DoesNotContain(repository.Commits, commit => commit.Hash == hash);
        Assert.False(repository.DeleteCommit(hash));
    }

    [Fact]
    public void RefreshingFilteredItems_PreservesSelectedCommitIdentity()
    {
        var repository = new DemoRepository();
        var table = new TableList<DemoCommit>(repository.Commits, new TableListDefinition<DemoCommit>
        {
            Columns = [TableColumn<DemoCommit>.Text("Hash", commit => commit.Hash, TableWidth.Fixed(8))]
        });
        table.SetSelectedIndex(10);
        string selectedHash = table.SelectedItem!.Hash;

        table.ReplaceItems(repository.Commits.Where(commit => commit.Hash == selectedHash || !commit.IsMerge).ToArray(), commit => commit.Hash);

        Assert.Equal(selectedHash, table.SelectedItem!.Hash);
    }

    [Fact]
    public void BranchMutations_EnforceCurrentProtectedAndNameRules()
    {
        var repository = new DemoRepository();
        Assert.False(repository.DeleteBranch("main", out string? currentError));
        Assert.Contains("current", currentError, StringComparison.OrdinalIgnoreCase);
        Assert.False(repository.RenameBranch("release/2.0", "release/next", out string? protectedError));
        Assert.Contains("Unprotect", protectedError);
        Assert.False(repository.CreateBranch("bad branch", out string? invalidError));
        Assert.Contains("spaces", invalidError);

        Assert.True(repository.CreateBranch("feature/new", out _));
        Assert.True(repository.RenameBranch("feature/new", "feature/renamed", out _));
        Assert.True(repository.SwitchBranch("feature/renamed"));
        Assert.Equal("feature/renamed", repository.CurrentBranch.Name);
    }

    [Fact]
    public void Filtering_ComposesCriteriaAndHonorsMergeVisibility()
    {
        var repository = new DemoRepository();
        Assert.DoesNotContain(DemoCommitQuery.Apply(repository.Commits, new(), showMergesWhenUnfiltered: false), commit => commit.IsMerge);

        IReadOnlyList<DemoCommit> unicode = DemoCommitQuery.Apply(repository.Commits,
            new DemoCommitFilter("日本語", Branch: "docs/unicode", CaseSensitive: true));
        Assert.NotEmpty(unicode);
        Assert.All(unicode, commit => Assert.Contains("日本語", commit.Subject));

        Assert.Empty(DemoCommitQuery.Apply(repository.Commits, new DemoCommitFilter("ADAPTIVE COMMIT TABLE", CaseSensitive: true)));
        Assert.NotEmpty(DemoCommitQuery.Apply(repository.Commits, new DemoCommitFilter("ADAPTIVE COMMIT TABLE")));
    }

    [Fact]
    public void SettingsAndReset_RestoreAllInitialState()
    {
        var repository = new DemoRepository();
        DemoSettings initial = repository.Settings;
        string initialHash = repository.Commits[0].Hash;
        repository.ToggleDates();
        repository.ToggleMerges();
        repository.UpdateSettings(repository.Settings with { RepositoryName = "Changed", ProtectionRules = new Dictionary<string, CheckState>() });
        repository.CreateBranch("temporary", out _);
        repository.DeleteCommit(initialHash);

        repository.Reset();

        Assert.Equal(34, repository.Commits.Count);
        Assert.Equal(5, repository.Branches.Count);
        Assert.Equal(initial with { ProtectionRules = null }, repository.Settings with { ProtectionRules = null });
        Assert.Equal(initial.ProtectionRules!.OrderBy(pair => pair.Key), repository.Settings.ProtectionRules!.OrderBy(pair => pair.Key));
        Assert.Equal(initialHash, repository.Commits[0].Hash);
        Assert.Equal("main", repository.CurrentBranch.Name);
    }
}

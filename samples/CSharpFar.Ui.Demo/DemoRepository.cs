namespace CSharpFar.Ui.Demo;

public sealed record DemoChangedFile(string Path, DemoChangeKind Change, int Additions, int Deletions);
public enum DemoChangeKind { Added, Modified, Deleted, Renamed }
public sealed record DemoBranch(string Name, string TipHash, bool IsCurrent = false, bool IsProtected = false);
public sealed record DemoSettings(
    bool ShowMerges = true,
    bool CompactDates = true,
    string RepositoryName = "CSharpFar UI Demo",
    string DefaultAuthor = "Ada Lovelace",
    string PullStrategy = "Merge",
    string DefaultBranch = "main",
    bool AutoFetch = true,
    bool PruneOnFetch = false,
    bool ConfirmDestructiveActions = true,
    IReadOnlyDictionary<string, CheckState>? ProtectionRules = null);
public sealed record DemoCommit(
    string Hash, string Branch, string Author, DateTimeOffset Timestamp, string Subject,
    IReadOnlyList<DemoChangedFile> ChangedFiles, bool IsMerge = false);

public sealed record DemoCommitFilter(
    string Text = "", string Author = "Any author", string Branch = "All branches",
    bool CaseSensitive = false, bool IncludeMerges = true)
{
    public bool IsActive => Text.Length > 0 || Author != "Any author" || Branch != "All branches" || CaseSensitive || !IncludeMerges;
}

public static class DemoCommitQuery
{
    public static IReadOnlyList<DemoCommit> Apply(
        IEnumerable<DemoCommit> commits, DemoCommitFilter filter, bool showMergesWhenUnfiltered = true) => commits
        .Where(commit => (filter.IsActive ? filter.IncludeMerges : showMergesWhenUnfiltered) || !commit.IsMerge)
        .Where(commit => filter.Branch == "All branches" || commit.Branch == filter.Branch)
        .Where(commit => filter.Author == "Any author" || commit.Author == filter.Author)
        .Where(commit => filter.Text.Length == 0 || Contains(commit.Subject, filter.Text, filter.CaseSensitive) || Contains(commit.Hash, filter.Text, filter.CaseSensitive))
        .ToArray();

    private static bool Contains(string source, string value, bool caseSensitive) =>
        source.Contains(value, caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
}

/// <summary>A deterministic, resettable repository-shaped data source for the UI demo.</summary>
public sealed class DemoRepository
{
    private List<DemoCommit> _commits = [];
    private IReadOnlyList<DemoBranch> _branches = [];
    private int _createdCommitCount;
    private readonly Func<DateTimeOffset> _now;

    public DemoRepository(Func<DateTimeOffset>? now = null)
    {
        _now = now ?? (() => DateTimeOffset.Now);
        Reset();
    }
    public IReadOnlyList<DemoCommit> Commits => _commits;
    public IReadOnlyList<DemoBranch> Branches => _branches;
    public DemoSettings Settings { get; private set; } = new();

    public void Reset()
    {
        string[] authors = ["Ada Lovelace", "Linus Torvalds", "Grace Hopper", "José García", "山田 太郎", "Sam Rivera"];
        string[] branches = ["main", "feature/table-navigation", "fix/resize", "release/2.0", "docs/unicode"];
        string[] subjects =
        [
            "Initial in-memory repository", "Add adaptive commit table", "Handle terminal resize without losing selection",
            "Fix scrollbar mouse capture", "Документация: быстрый старт", "Improve keyboard navigation",
            "Merge feature/table-navigation into main", "Render narrow viewports gracefully",
            "Refactor demo state transitions", "Add 日本語 and emoji-friendly examples",
            "Correct author alignment", "A deliberately long commit subject demonstrating truncation while preserving the selected commit identity across viewport changes"
        ];
        var commits = new List<DemoCommit>();
        DateTimeOffset start = new(2026, 8, 29, 18, 40, 0, TimeSpan.Zero);
        for (int i = 0; i < 34; i++)
        {
            int fileCount = i is 3 or 17 ? 18 : 1 + i % 6;
            var files = Enumerable.Range(0, fileCount).Select(n => new DemoChangedFile(
                n == fileCount - 1 && i % 5 == 0
                    ? $"src/very/long/component/path/with-unicode/данные-{i}-{n}.cs"
                    : $"src/Demo/Feature{i % 7}/File{n}.cs",
                (DemoChangeKind)((i + n) % 4), 2 + (i * 3 + n) % 91, (i + n * 2) % 27)).ToArray();
            commits.Add(new DemoCommit($"{0x8a12bc00 + i * 7919:x8}", branches[i % branches.Length], authors[i % authors.Length],
                start.AddHours(-i * 9).AddMinutes(-(i * 17 % 53)), subjects[i % subjects.Length], files, i is 6 or 23));
        }
        _commits = commits;
        _branches = branches.Select((name, i) => new DemoBranch(name, commits.First(c => c.Branch == name).Hash, i == 0, i is 0 or 3)).ToArray();
        Settings = new(ProtectionRules: InitialProtectionRules());
        _createdCommitCount = 0;
    }

    public DemoCommit CreateCommit(string author, string subject, string type, bool amend, bool signOff)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(author);
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        int sequence = ++_createdCommitCount;
        DemoBranch current = _branches.First(branch => branch.IsCurrent);
        string hash = $"{0xd0000000u + (uint)(sequence * 104729):x8}";
        string decoratedSubject = type == "Commit" ? subject.Trim() : $"{type.ToLowerInvariant()}: {subject.Trim()}";
        if (signOff) decoratedSubject += " (signed-off)";
        DemoChangedFile[] files =
        [
            new($"src/Demo/Generated/Commit{sequence:000}.cs", DemoChangeKind.Added, 8 + sequence, 0),
            new("README.md", DemoChangeKind.Modified, 1, sequence % 3),
            .. amend ? new[] { new DemoChangedFile("src/Demo/Amended.cs", DemoChangeKind.Modified, 2, 1) } : Array.Empty<DemoChangedFile>()
        ];
        var commit = new DemoCommit(hash, current.Name, author.Trim(), _now(), decoratedSubject, files);
        _commits.Insert(0, commit);
        _branches = _branches.Select(branch => branch.IsCurrent ? branch with { TipHash = hash } : branch).ToArray();
        return commit;
    }

    public bool DeleteCommit(string hash)
    {
        int index = _commits.FindIndex(commit => commit.Hash == hash);
        if (index < 0) return false;
        _commits.RemoveAt(index);
        return true;
    }

    public DemoBranch CurrentBranch => _branches.First(branch => branch.IsCurrent);

    public bool SwitchBranch(string name)
    {
        if (!_branches.Any(branch => branch.Name == name)) return false;
        _branches = _branches.Select(branch => branch with { IsCurrent = branch.Name == name }).ToArray();
        return true;
    }

    public bool CreateBranch(string name, out string? error)
    {
        name = name.Trim();
        error = ValidateBranchName(name);
        if (error is not null) return false;
        if (_branches.Any(branch => string.Equals(branch.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            error = "A branch with that name already exists.";
            return false;
        }
        _branches = [.. _branches, new DemoBranch(name, CurrentBranch.TipHash)];
        return true;
    }

    public bool RenameBranch(string oldName, string newName, out string? error)
    {
        DemoBranch? branch = _branches.FirstOrDefault(value => value.Name == oldName);
        if (branch is null) { error = "Branch no longer exists."; return false; }
        if (branch.IsCurrent) { error = "Switch away from the current branch before renaming it."; return false; }
        if (branch.IsProtected) { error = "Unprotect this branch before renaming it."; return false; }
        newName = newName.Trim();
        error = ValidateBranchName(newName);
        if (error is not null) return false;
        if (_branches.Any(value => value.Name != oldName && string.Equals(value.Name, newName, StringComparison.OrdinalIgnoreCase)))
        { error = "A branch with that name already exists."; return false; }
        _branches = _branches.Select(value => value.Name == oldName ? value with { Name = newName } : value).ToArray();
        _commits = _commits.Select(commit => commit.Branch == oldName ? commit with { Branch = newName } : commit).ToList();
        return true;
    }

    public bool DeleteBranch(string name, out string? error)
    {
        DemoBranch? branch = _branches.FirstOrDefault(value => value.Name == name);
        if (branch is null) { error = "Branch no longer exists."; return false; }
        if (branch.IsCurrent) { error = "The current branch cannot be deleted."; return false; }
        if (branch.IsProtected) { error = "Unprotect this branch before deleting it."; return false; }
        _branches = _branches.Where(value => value.Name != name).ToArray();
        error = null;
        return true;
    }

    public bool ToggleBranchProtection(string name)
    {
        if (!_branches.Any(branch => branch.Name == name)) return false;
        _branches = _branches.Select(branch => branch.Name == name ? branch with { IsProtected = !branch.IsProtected } : branch).ToArray();
        return true;
    }

    public void UpdateSettings(DemoSettings settings) => Settings = settings;

    public void ToggleMerges() => Settings = Settings with { ShowMerges = !Settings.ShowMerges };
    public void ToggleDates() => Settings = Settings with { CompactDates = !Settings.CompactDates };

    private static string? ValidateBranchName(string name)
    {
        if (name.Length == 0) return "Branch name is required.";
        if (name.Any(char.IsWhiteSpace)) return "Branch names cannot contain spaces.";
        if (name.Contains("..", StringComparison.Ordinal) || name.StartsWith('-') || name.EndsWith('/') || name.Contains('\\'))
            return "Use a simple Git-style branch name.";
        return null;
    }

    private static IReadOnlyDictionary<string, CheckState> InitialProtectionRules() => new Dictionary<string, CheckState>
    {
        ["push:maintainers"] = CheckState.Checked,
        ["push:contributors"] = CheckState.Indeterminate,
        ["push:guests"] = CheckState.Unchecked,
        ["merge:maintainers"] = CheckState.Checked,
        ["merge:contributors"] = CheckState.Checked,
        ["merge:guests"] = CheckState.Unchecked,
        ["force:maintainers"] = CheckState.Indeterminate,
        ["force:contributors"] = CheckState.Unchecked,
        ["force:guests"] = CheckState.Unchecked,
    };
}

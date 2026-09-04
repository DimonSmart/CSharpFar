using CSharpFar.Ui;

namespace CSharpFar.Ui.Demo;

internal sealed record CommitSearch(string Text, string Author, string Branch, bool CaseSensitive, bool IncludeMerges)
{
    public bool IsActive => Text.Length > 0 || Author != "Any author" || Branch != "All branches" || CaseSensitive || !IncludeMerges;
}

internal sealed class RepositoryWorkflows
{
    private static readonly string[] PullStrategies = ["Merge", "Rebase", "Fast-forward only"];
    private readonly DialogService _dialogs;
    private readonly FormFieldFactory _fields;
    private readonly DemoRepository _repository;

    public RepositoryWorkflows(DialogService dialogs, FormFieldFactory fields, DemoRepository repository)
    {
        _dialogs = dialogs;
        _fields = fields;
        _repository = repository;
    }

    public DemoBranch? SwitchBranch()
    {
        int selected = _repository.Branches.ToList().FindIndex(branch => branch.IsCurrent);
        SelectionListDialogResult<DemoBranch> result = _dialogs.Select(new SelectionDialogOptions<DemoBranch>
        {
            Title = "Switch Branch",
            Items = _repository.Branches,
            ItemText = branch => $"{(branch.IsCurrent ? "*" : " ")} {branch.Name}{(branch.IsProtected ? "  [protected]" : string.Empty)}",
            SelectedIndex = Math.Max(0, selected),
            Presentation = new SelectionDialogPresentation(72, 14),
            DoubleBorder = true,
        });
        return result.IsConfirmed ? result.SelectedItem : null;
    }

    public void ManageBranches()
    {
        _dialogs.List(new ListDialogOptions<DemoBranch, bool>
        {
            Title = "Manage Branches",
            Items = () => _repository.Branches,
            ItemText = branch => $"{(branch.IsCurrent ? '*' : ' ')} {(branch.IsProtected ? "Protected" : "Open"),-9} {branch.Name,-30} {branch.TipHash}",
            Actions =
            [
                DialogButton.Default("switch", "Switch", 'S'), DialogButton.Action("create", "Create", 'C'),
                DialogButton.Action("rename", "Rename", 'R'), DialogButton.Action("protect", "Protect", 'P'),
                DialogButton.Action("delete", "Delete", 'D'), DialogButton.Cancel("Close", 'L')
            ],
            DefaultItemActionId = "switch",
            DeleteActionId = "delete",
            EmptyText = "No branches",
            DialogWidth = 82,
            MaxVisibleRows = 12,
            Cancel = () => false,
            HandleAction = HandleBranchAction,
        });
    }

    public CommitSearch? Search(CommitSearch current)
    {
        TextField text = _fields.Text(new TextFieldOptions(InitialText: current.Text, SubmitOnEnter: true));
        string[] authors = ["Any author", .. _repository.Commits.Select(commit => commit.Author).Distinct().Order()];
        string[] branches = ["All branches", .. _repository.Branches.Select(branch => branch.Name)];
        DropdownSelectFormRow<string> author = FormControls.Dropdown("&Author", authors, value => value, authors.Contains(current.Author) ? current.Author : authors[0]);
        DropdownSelectFormRow<string> branch = FormControls.Dropdown("&Branch", branches, value => value, branches.Contains(current.Branch) ? current.Branch : branches[0]);
        CheckBoxRow caseSensitive = FormControls.CheckBox("&Case-sensitive", current.CaseSensitive);
        CheckBoxRow merges = FormControls.CheckBox("Include &merge commits", current.IncludeMerges);

        return _dialogs.Form(
            new FormDialogOptions("Search Commits", 66, 13, SubmitOnEnter: true) { InitialFocus = text, ResizeMode = DialogResizeMode.Both },
            () => [FormControls.Text("&Text", text), author, branch, FormControls.Separator(), caseSensitive, merges, FormControls.Spacer()],
            () => [FormControls.Buttons(DialogButton.Default("apply", "Apply", 'A'), DialogButton.Cancel())],
            submit: () => FormSubmit.Success(new CommitSearch(text.TrimmedText, author.Value, branch.Value, caseSensitive.Value, merges.Value)));
    }

    public bool EditSettings()
    {
        DemoSettings original = _repository.Settings;
        TextField name = _fields.Text(new TextFieldOptions(InitialText: original.RepositoryName));
        TextField author = _fields.Text(new TextFieldOptions(InitialText: original.DefaultAuthor));
        var strategy = FormControls.Dropdown("Pull &strategy", PullStrategies, value => value, original.PullStrategy);
        string[] branches = _repository.Branches.Select(branch => branch.Name).ToArray();
        var defaultBranch = FormControls.Dropdown("Default &branch", branches, value => value,
            branches.Contains(original.DefaultBranch) ? original.DefaultBranch : _repository.CurrentBranch.Name);
        CheckBoxRow autoFetch = FormControls.CheckBox("&Automatically fetch on startup", original.AutoFetch);
        CheckBoxRow prune = FormControls.CheckBox("&Prune deleted remote branches", original.PruneOnFetch);
        CheckBoxRow confirm = FormControls.CheckBox("&Confirm destructive actions", original.ConfirmDestructiveActions);
        IReadOnlyDictionary<string, CheckState> rules = original.ProtectionRules ?? new Dictionary<string, CheckState>();
        var matrix = FormControls.TriStateMatrix(
            [new("maintainers", "Maintainers"), new("contributors", "Contributors"), new("guests", "Guests")],
            new[] { MatrixRow("push", "Push", rules), MatrixRow("merge", "Merge", rules), MatrixRow("force", "Force push", rules) });

        bool? accepted = _dialogs.Form(
            new FormDialogOptions("Repository Settings", 78, 22, MinHeight: 10, SubmitOnEnter: false) { ResizeMode = DialogResizeMode.Both },
            () =>
            [
                FormControls.Label("Identity"), FormControls.Text("Repository &name", name), FormControls.Text("Default &author", author),
                FormControls.Separator(), FormControls.Label("Integration"), strategy, defaultBranch, autoFetch, prune, confirm,
                FormControls.Separator(), FormControls.Label("Branch protection (Space cycles inherited/allowed/denied)"), matrix,
                FormControls.Separator(), FormControls.Label("Demonstration notes"),
                FormControls.Label("Settings are kept only in memory."), FormControls.Label("Resize this dialog or use the mouse wheel to exercise scrolling."),
                FormControls.Label("No Git installation, network, or configuration file is used."), FormControls.Spacer(2),
            ],
            () => [FormControls.Buttons(DialogButton.Default("save", "Save", 'S'), DialogButton.Cancel())],
            submit: () => string.IsNullOrWhiteSpace(name.Text)
                ? FormSubmit.Invalid<bool>("Repository name is required.", name)
                : string.IsNullOrWhiteSpace(author.Text)
                    ? FormSubmit.Invalid<bool>("Default author is required.", author)
                    : FormSubmit.Success(true));
        if (accepted != true) return false;

        var updatedRules = new Dictionary<string, CheckState>();
        foreach (string row in new[] { "push", "merge", "force" })
            foreach (string column in new[] { "maintainers", "contributors", "guests" })
                updatedRules[$"{row}:{column}"] = matrix.GetValue(row, column);
        _repository.UpdateSettings(original with
        {
            RepositoryName = name.TrimmedText,
            DefaultAuthor = author.TrimmedText,
            PullStrategy = strategy.Value,
            DefaultBranch = defaultBranch.Value,
            AutoFetch = autoFetch.Value,
            PruneOnFetch = prune.Value,
            ConfirmDestructiveActions = confirm.Value,
            ProtectionRules = updatedRules,
        });
        return true;
    }

    private DialogOutcome<bool> HandleBranchAction(ListDialogActionContext<DemoBranch> action)
    {
        DemoBranch? branch = action.SelectedItem;
        if (action.ActionId == "create")
        {
            string? name = PromptBranchName("Create Branch", "New branch name", string.Empty);
            if (name is null) return DialogOutcome<bool>.ContinueOpen();
            if (!_repository.CreateBranch(name, out string? error)) _dialogs.Message("Cannot Create Branch", error!);
            return DialogOutcome<bool>.RefreshOpen();
        }
        if (branch is null) return DialogOutcome<bool>.ContinueOpen();
        switch (action.ActionId)
        {
            case "switch": _repository.SwitchBranch(branch.Name); return DialogOutcome<bool>.RefreshOpen();
            case "rename":
                if (branch.IsCurrent || branch.IsProtected)
                { _dialogs.Message("Cannot Rename Branch", branch.IsCurrent ? "Switch away from the current branch first." : "Unprotect this branch first."); return DialogOutcome<bool>.ContinueOpen(); }
                string? renamed = PromptBranchName("Rename Branch", "Branch name", branch.Name);
                if (renamed is not null && !_repository.RenameBranch(branch.Name, renamed, out string? renameError)) _dialogs.Message("Cannot Rename Branch", renameError!);
                return DialogOutcome<bool>.RefreshOpen();
            case "protect": _repository.ToggleBranchProtection(branch.Name); return DialogOutcome<bool>.RefreshOpen();
            case "delete":
                if (branch.IsCurrent || branch.IsProtected)
                { _dialogs.Message("Cannot Delete Branch", branch.IsCurrent ? "The current branch cannot be deleted." : "Unprotect this branch before deleting it."); return DialogOutcome<bool>.ContinueOpen(); }
                if (_dialogs.Confirm("Delete Branch", "Delete this in-memory branch?", branch.Name))
                    _repository.DeleteBranch(branch.Name, out _);
                return DialogOutcome<bool>.RefreshOpen();
            default: return DialogOutcome<bool>.ContinueOpen();
        }
    }

    private string? PromptBranchName(string title, string prompt, string initial) => _dialogs.Input(new SingleLineInputDialogOptions
    {
        Title = title,
        Prompt = prompt,
        InitialText = initial,
        Validate = value => string.IsNullOrWhiteSpace(value) ? "Branch name is required." : null,
    });

    private static TriStateMatrixRow MatrixRow(string id, string label, IReadOnlyDictionary<string, CheckState> values) =>
        new(id, label, new[] { Value(id, "maintainers", values), Value(id, "contributors", values), Value(id, "guests", values) });

    private static CheckState Value(string row, string column, IReadOnlyDictionary<string, CheckState> values) =>
        values.TryGetValue($"{row}:{column}", out CheckState value) ? value : CheckState.Indeterminate;
}

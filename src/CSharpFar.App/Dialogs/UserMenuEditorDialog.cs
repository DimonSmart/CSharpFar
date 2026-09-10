using CSharpFar.Core.Models;
using CSharpFar.Ui;

namespace CSharpFar.App.Dialogs;

internal sealed record UserMenuEditorDialogResult(
    bool Changed,
    IReadOnlyList<UserMenuItem> Items);

internal sealed class UserMenuEditorDialog
{
    private readonly DialogService _dialogs;
    private readonly FormFieldFactory _fields;

    public UserMenuEditorDialog(DialogService dialogs, FormFieldFactory fields)
    {
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _fields = fields ?? throw new ArgumentNullException(nameof(fields));
    }

    public UserMenuEditorDialogResult Show(IReadOnlyList<UserMenuItem> currentItems)
    {
        List<UserMenuItem> items = CloneItems(currentItems).ToList();
        UserMenuItem[] initialItems = CloneItems(currentItems);

        return _dialogs.List(new ListDialogOptions<UserMenuItem, UserMenuEditorDialogResult>
        {
            Title = "User menu",
            Items = () => items,
            ItemText = static item => $"{item.Title}  {item.Command}",
            Actions =
            [
                DialogButton.Action("add", "Add", 'A'),
                DialogButton.Default("edit", "Edit", 'E'),
                DialogButton.Action("delete", "Delete", 'D'),
                DialogButton.Action("up", "Up", 'U'),
                DialogButton.Action("down", "Down", 'O'),
                DialogButton.Action("close", "Close", 'C'),
            ],
            DialogWidth = 78,
            MinDialogWidth = 48,
            MaxVisibleRows = 12,
            DefaultItemActionId = "edit",
            DeleteActionId = "delete",
            EmptyText = "No user menu items.",
            Cancel = () => Result(initialItems, items),
            HandleAction = action => HandleAction(action, initialItems, items),
        })!;
    }

    private DialogOutcome<UserMenuEditorDialogResult> HandleAction(
        ListDialogActionContext<UserMenuItem> action,
        IReadOnlyList<UserMenuItem> initialItems,
        List<UserMenuItem> items)
    {
        return action.ActionId switch
        {
            "add" => Add(items),
            "edit" => Edit(items, action.SelectedIndex),
            "delete" => Delete(items, action.SelectedIndex),
            "up" => Move(items, action.SelectedIndex, -1),
            "down" => Move(items, action.SelectedIndex, 1),
            "close" => DialogOutcome<UserMenuEditorDialogResult>.Complete(Result(initialItems, items)),
            _ => DialogOutcome<UserMenuEditorDialogResult>.ContinueOpen(),
        };
    }

    private DialogOutcome<UserMenuEditorDialogResult> Add(List<UserMenuItem> items)
    {
        UserMenuItem? item = new UserMenuItemEditDialog(_dialogs, _fields).Show(null);
        if (item is null)
            return DialogOutcome<UserMenuEditorDialogResult>.ContinueOpen();

        items.Add(item);
        return DialogOutcome<UserMenuEditorDialogResult>.RefreshOpen(items.Count - 1);
    }

    private DialogOutcome<UserMenuEditorDialogResult> Edit(List<UserMenuItem> items, int selectedIndex)
    {
        if (selectedIndex < 0 || selectedIndex >= items.Count)
            return DialogOutcome<UserMenuEditorDialogResult>.ContinueOpen();

        UserMenuItem current = items[selectedIndex];
        UserMenuItem? edited = new UserMenuItemEditDialog(_dialogs, _fields).Show(current);
        if (edited is null || SameItem(current, edited))
            return DialogOutcome<UserMenuEditorDialogResult>.ContinueOpen();

        items[selectedIndex] = edited;
        return DialogOutcome<UserMenuEditorDialogResult>.RefreshOpen(selectedIndex);
    }

    private DialogOutcome<UserMenuEditorDialogResult> Delete(List<UserMenuItem> items, int selectedIndex)
    {
        if (selectedIndex < 0 || selectedIndex >= items.Count)
            return DialogOutcome<UserMenuEditorDialogResult>.ContinueOpen();

        UserMenuItem item = items[selectedIndex];
        if (!_dialogs.Confirm("User menu", "Delete user menu item?", item.Title))
            return DialogOutcome<UserMenuEditorDialogResult>.ContinueOpen();

        items.RemoveAt(selectedIndex);
        int nextIndex = items.Count == 0 ? 0 : Math.Min(selectedIndex, items.Count - 1);
        return DialogOutcome<UserMenuEditorDialogResult>.RefreshOpen(nextIndex);
    }

    private static DialogOutcome<UserMenuEditorDialogResult> Move(
        List<UserMenuItem> items,
        int selectedIndex,
        int offset)
    {
        if (selectedIndex < 0 || selectedIndex >= items.Count)
            return DialogOutcome<UserMenuEditorDialogResult>.ContinueOpen();

        int targetIndex = selectedIndex + offset;
        if (targetIndex < 0 || targetIndex >= items.Count)
            return DialogOutcome<UserMenuEditorDialogResult>.ContinueOpen();

        (items[selectedIndex], items[targetIndex]) = (items[targetIndex], items[selectedIndex]);
        return DialogOutcome<UserMenuEditorDialogResult>.RefreshOpen(targetIndex);
    }

    private static UserMenuEditorDialogResult Result(
        IReadOnlyList<UserMenuItem> initialItems,
        IReadOnlyList<UserMenuItem> items)
    {
        UserMenuItem[] snapshot = CloneItems(items);
        bool changed = initialItems.Count != snapshot.Length ||
            initialItems.Where((item, index) => index < snapshot.Length)
                .Any((item, index) => !SameItem(item, snapshot[index]));
        return new UserMenuEditorDialogResult(changed, snapshot);
    }

    private static bool SameItem(UserMenuItem left, UserMenuItem right) =>
        left.Title == right.Title && left.Command == right.Command;

    private static UserMenuItem[] CloneItems(IEnumerable<UserMenuItem> items) =>
        items.Select(item => new UserMenuItem
        {
            Title = item.Title,
            Command = item.Command,
        }).ToArray();
}

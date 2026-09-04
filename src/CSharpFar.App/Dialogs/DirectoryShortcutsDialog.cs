using CSharpFar.App.DirectoryShortcuts;
using CSharpFar.Core.Models;
using CSharpFar.Ui;

namespace CSharpFar.App.Dialogs;

internal sealed record DirectoryShortcutsDialogResult(
    bool Changed,
    IReadOnlyList<AppSettings.DirectoryShortcutItem> Items);

internal sealed class DirectoryShortcutsDialog
{
    private readonly DialogService _dialogs;
    private readonly FormFieldFactory _fields;

    public DirectoryShortcutsDialog(DialogService dialogs, FormFieldFactory fields)
    {
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _fields = fields ?? throw new ArgumentNullException(nameof(fields));
    }

    public DirectoryShortcutsDialogResult Show(
        IReadOnlyList<AppSettings.DirectoryShortcutItem> currentItems,
        string activePanelPath)
    {
        var items = currentItems.ToDictionary(item => item.Number);
        var initialItems = CloneItems(items);

        return _dialogs.List(new ListDialogOptions<int, DirectoryShortcutsDialogResult>
        {
            Title = "Directory shortcuts",
            Items = () => DirectoryShortcutNormalizer.DisplayOrder,
            ItemText = number => FormatShortcut(number, items),
            Actions =
            [
                DialogButton.Default("edit", "Edit", 'E'),
                DialogButton.Action("delete", "Delete", 'D'),
                DialogButton.Action("close", "Close", 'C'),
            ],
            DialogWidth = 68,
            MinDialogWidth = 40,
            MaxVisibleRows = 10,
            DefaultItemActionId = "edit",
            DeleteActionId = "delete",
            Cancel = () => Result(initialItems, items),
            HandleAction = action =>
            {
                if (action.ActionId == "close")
                    return DialogOutcome<DirectoryShortcutsDialogResult>.Complete(Result(initialItems, items));

                if (action.ActionId == "delete" && action.SelectedItem is int deleteNumber)
                    return Delete(items, deleteNumber)
                        ? DialogOutcome<DirectoryShortcutsDialogResult>.RefreshOpen()
                        : DialogOutcome<DirectoryShortcutsDialogResult>.ContinueOpen();

                return action.ActionId == "edit" && action.SelectedItem is int number && Edit(items, number, activePanelPath)
                    ? DialogOutcome<DirectoryShortcutsDialogResult>.RefreshOpen()
                    : DialogOutcome<DirectoryShortcutsDialogResult>.ContinueOpen();
            },
        })!;
    }

    private bool Delete(
        IDictionary<int, AppSettings.DirectoryShortcutItem> items,
        int number)
    {
        if (!items.TryGetValue(number, out var item))
            return false;

        string itemText = string.IsNullOrWhiteSpace(item.Name)
            ? item.Path
            : $"{item.Name} — {item.Path}";
        if (!_dialogs.Confirm("Directory shortcuts", $"Delete directory shortcut {number}?", itemText))
            return false;

        return items.Remove(number);
    }

    private bool Edit(
        IDictionary<int, AppSettings.DirectoryShortcutItem> items,
        int number,
        string activePanelPath)
    {
        items.TryGetValue(number, out var currentItem);
        DirectoryShortcutEditResult? result = new DirectoryShortcutEditDialog(_dialogs, _fields)
            .Show(number, currentItem, activePanelPath);
        if (result is null)
            return false;

        if (result.Item is null)
            return items.Remove(number);
        else
        {
            bool changed = !items.TryGetValue(number, out var previous) ||
                previous.Name != result.Item.Name || previous.Path != result.Item.Path;
            items[number] = result.Item;
            return changed;
        }
    }

    private static string FormatShortcut(int number, IReadOnlyDictionary<int, AppSettings.DirectoryShortcutItem> items)
    {
        items.TryGetValue(number, out var item);
        return $"{number}  {item?.Name ?? string.Empty,-8}  {item?.Path ?? string.Empty}";
    }

    private static DirectoryShortcutsDialogResult Result(
        IReadOnlyDictionary<int, AppSettings.DirectoryShortcutItem> initialItems,
        IReadOnlyDictionary<int, AppSettings.DirectoryShortcutItem> items)
    {
        var normalizedItems = CloneItems(items);
        bool changed = initialItems.Count != normalizedItems.Count || initialItems.Any(pair =>
            !normalizedItems.TryGetValue(pair.Key, out var item) ||
            pair.Value.Name != item.Name || pair.Value.Path != item.Path);
        return new DirectoryShortcutsDialogResult(
            changed,
            DirectoryShortcutNormalizer.DisplayOrder
                .Where(normalizedItems.ContainsKey)
                .Select(number => normalizedItems[number])
                .ToArray());
    }

    private static Dictionary<int, AppSettings.DirectoryShortcutItem> CloneItems(
        IReadOnlyDictionary<int, AppSettings.DirectoryShortcutItem> items) =>
        items.ToDictionary(pair => pair.Key, pair => new AppSettings.DirectoryShortcutItem
        {
            Number = pair.Value.Number,
            Name = pair.Value.Name,
            Path = pair.Value.Path,
        });
}

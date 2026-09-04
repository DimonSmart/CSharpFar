using CSharpFar.App.Dialogs;
using CSharpFar.Console;
using CSharpFar.Core.History;
using CSharpFar.Core.Models;
using CSharpFar.Tests.Fakes;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

public sealed class DirectoryShortcutsDialogTests : IDisposable
{
    private readonly string _root;
    private readonly string _target;

    public DirectoryShortcutsDialogTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"CSharpFarShortcutDialog_{Guid.NewGuid():N}");
        _target = Path.Combine(_root, "target");
        Directory.CreateDirectory(_target);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void DeleteConfiguredShortcut_ConfirmsRemovesAndRefreshesSlot()
    {
        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(Key(ConsoleKey.DownArrow));
        driver.EnqueueKey(Key(ConsoleKey.Tab));
        driver.EnqueueKey(new ConsoleKeyInfo('d', ConsoleKey.D, shift: false, alt: false, control: false));
        driver.EnqueueKey(Key(ConsoleKey.Enter));
        driver.EnqueueKey(Key(ConsoleKey.Escape));

        DirectoryShortcutsDialogResult result = Show(
            driver,
            [Shortcut(2, "Work", _target)]);

        Assert.True(result.Changed);
        Assert.Empty(result.Items);
        Assert.Contains(driver.WriteRecords, write =>
            write.Text.Contains("Delete directory shortcut 2?", StringComparison.Ordinal));
        Assert.Contains(driver.WriteRecords, write =>
            write.Text.Contains("Work —", StringComparison.Ordinal));
        var slotWrites = driver.WriteRecords
            .Where(write => write.Text.StartsWith("2  ", StringComparison.Ordinal))
            .ToArray();
        Assert.NotEmpty(slotWrites);
        Assert.DoesNotContain("Work", slotWrites[^1].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void CancelDeleteConfirmation_KeepsShortcutAndParentOpen()
    {
        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(Key(ConsoleKey.Delete));
        driver.EnqueueKey(Key(ConsoleKey.Escape));
        driver.EnqueueKey(Key(ConsoleKey.Escape));
        var original = Shortcut(1, "Work", _target);

        DirectoryShortcutsDialogResult result = Show(driver, [original]);

        Assert.False(result.Changed);
        AppSettings.DirectoryShortcutItem item = Assert.Single(result.Items);
        Assert.Equal(1, item.Number);
        Assert.Equal("Work", item.Name);
        Assert.Equal(_target, item.Path);
    }

    [Fact]
    public void DeleteEmptySlot_IsNoOpWithoutConfirmation()
    {
        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(Key(ConsoleKey.DownArrow));
        driver.EnqueueKey(Key(ConsoleKey.Delete));
        driver.EnqueueKey(Key(ConsoleKey.Escape));

        DirectoryShortcutsDialogResult result = Show(driver, []);

        Assert.False(result.Changed);
        Assert.Empty(result.Items);
        Assert.DoesNotContain(driver.WriteRecords, write =>
            write.Text.Contains("Delete directory shortcut", StringComparison.Ordinal));
    }

    [Fact]
    public void DeleteKey_UsesDeleteAction()
    {
        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(Key(ConsoleKey.Delete));
        driver.EnqueueKey(Key(ConsoleKey.Enter));
        driver.EnqueueKey(Key(ConsoleKey.Escape));

        DirectoryShortcutsDialogResult result = Show(
            driver,
            [Shortcut(1, "Work", _target)]);

        Assert.True(result.Changed);
        Assert.Empty(result.Items);
    }

    [Fact]
    public void DeleteConfiguredShortcut_KeepsSelectionOnSameSlot()
    {
        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(Key(ConsoleKey.DownArrow));
        driver.EnqueueKey(Key(ConsoleKey.Delete));
        driver.EnqueueKey(Key(ConsoleKey.Enter));
        driver.EnqueueKey(Key(ConsoleKey.Delete));
        driver.EnqueueKey(Key(ConsoleKey.Escape));
        driver.EnqueueKey(Key(ConsoleKey.Escape));

        DirectoryShortcutsDialogResult result = Show(
            driver,
            [Shortcut(1, "Keep", _target), Shortcut(2, "Work", _target)]);

        Assert.True(result.Changed);
        AppSettings.DirectoryShortcutItem item = Assert.Single(result.Items);
        Assert.Equal(1, item.Number);
        Assert.Equal("Keep", item.Name);
        Assert.Equal(1, driver.WriteRecords.Count(write =>
            write.Text.Contains("Delete directory shortcut", StringComparison.Ordinal)));
    }

    private DirectoryShortcutsDialogResult Show(
        FakeConsoleDriver driver,
        IReadOnlyList<AppSettings.DirectoryShortcutItem> items)
    {
        var fields = new FormFieldFactory(TextFieldHistoryTestProvider.Create());
        var dialogs = new DialogService(ModalTestHost.Create(driver), fields);
        return new DirectoryShortcutsDialog(dialogs, fields).Show(items, _target);
    }

    private static AppSettings.DirectoryShortcutItem Shortcut(int number, string name, string path) =>
        new()
        {
            Number = number,
            Name = name,
            Path = path,
        };

    private static ConsoleKeyInfo Key(ConsoleKey key) =>
        new('\0', key, shift: false, alt: false, control: false);
}

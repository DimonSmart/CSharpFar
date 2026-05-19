using System.Globalization;
using System.Runtime.InteropServices;
using CSharpFar.Core.Models;
using FarNet;

namespace CSharpFar.FarNetHost;

internal sealed class CSharpFarFarNetApi : IFar, IFarNetPanelHost
{
    private static readonly IWindow PanelsWindow = new FarNetPanelsWindow();

    private readonly FarNetModuleHostServices _services;
    private readonly IReadOnlyDictionary<Guid, IModuleAction> _actions;
    private readonly IReadOnlyDictionary<string, FarNetModuleManager> _managersByName;
    private readonly IReadOnlyDictionary<string, FarNetModuleManager> _managersByAssemblyPath;
    private readonly FarNetModuleHostOptions _options;
    private Panel? _pendingPanel;

    public CSharpFarFarNetApi(
        FarNetModuleHostServices services,
        IReadOnlyDictionary<Guid, IModuleAction> actions,
        IReadOnlyDictionary<string, FarNetModuleManager> managersByName,
        FarNetModuleHostOptions options)
    {
        _services = services;
        _actions = actions;
        _managersByName = managersByName;
        _options = options;
        _managersByAssemblyPath = managersByName.Values.ToDictionary(
            manager => manager.Assembly.Location,
            manager => manager,
            StringComparer.OrdinalIgnoreCase);
    }

    public override Version FarVersion => new(0, 1, 0);

    public override Version FarNetVersion => FarNetAssemblyCompatibility.SupportedVersion;

    public override IModuleAction? GetModuleAction(Guid id) =>
        _actions.TryGetValue(id, out var action) ? action : null;

    public override int Message(MessageArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);

        string title = string.IsNullOrWhiteSpace(args.Caption) ? "FarNet" : args.Caption!;
        string text = args.Text ?? string.Empty;
        var buttons = GetButtons(args);
        return _services.Ui.ShowMessage(title, text, buttons);
    }

    public override string? Input(string? prompt, string? history, string? title, string? text)
    {
        _ = history;
        return _services.Ui.Input(
            string.IsNullOrWhiteSpace(title) ? "FarNet" : title!,
            prompt ?? string.Empty,
            text);
    }

    public override void ShowError(string? title, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        _services.Ui.ShowMessage(
            string.IsNullOrWhiteSpace(title) ? "FarNet" : title!,
            exception.Message,
            ["OK"]);
    }

    public override string CurrentDirectory =>
        _services.GetPanelState(_services.GetActivePanelSide()).CurrentDirectory;

    public override string TempName(string? prefix)
    {
        string filePrefix = string.IsNullOrWhiteSpace(prefix) ? "FAR" : prefix!;
        return Path.Combine(Path.GetTempPath(), filePrefix + Guid.NewGuid().ToString("N") + ".tmp");
    }

    public override IModuleManager GetModuleManager(string name)
    {
        if (_managersByName.TryGetValue(name, out var manager))
            return manager;

        throw new ArgumentException($"Cannot find FarNet module '{name}'.", nameof(name));
    }

    public override IModuleManager GetModuleManager(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        if (_managersByAssemblyPath.TryGetValue(type.Assembly.Location, out var manager))
            return manager;

        return base.GetModuleManager(type);
    }

    public override CultureInfo GetCurrentUICulture(bool update)
    {
        _ = update;
        return CultureInfo.CurrentUICulture;
    }

    public override IMenu CreateMenu() => new FarNetMenu(_services);

    public override string? PasteFromClipboard() =>
        Clipboard.TryGetText(out string? text) ? text : string.Empty;

    public override void CopyToClipboard(string text) =>
        Clipboard.SetText(text ?? string.Empty);

    public override IWindow Window => PanelsWindow;

    public void OpenPanel(Panel panel)
    {
        ArgumentNullException.ThrowIfNull(panel);

        if (!_options.EnablePanelTools)
            throw new FarNetUnsupportedApiException("Panel.Open");

        panel.Explorer.EnterPanel(panel);
        if (string.IsNullOrWhiteSpace(panel.CurrentLocation))
            panel.CurrentLocation = string.IsNullOrWhiteSpace(panel.Explorer.Location)
                ? "."
                : panel.Explorer.Location;
        if (string.IsNullOrWhiteSpace(panel.CurrentDirectory))
            panel.CurrentDirectory = panel.CurrentLocation;

        _pendingPanel = panel;
    }

    public Panel? ConsumePendingPanel()
    {
        var panel = _pendingPanel;
        _pendingPanel = null;
        return panel;
    }

    public override void ShowHelp(string path, string topic, HelpOptions options) =>
        throw new FarNetUnsupportedApiException(nameof(ShowHelp));

    private static IReadOnlyList<string> GetButtons(MessageArgs args)
    {
        if (args.Buttons is { Length: > 0 })
            return args.Buttons;

        return args.Options switch
        {
            var options when HasButtonGroup(options, MessageOptions.OkCancel) => ["OK", "Cancel"],
            var options when HasButtonGroup(options, MessageOptions.AbortRetryIgnore) => ["Abort", "Retry", "Ignore"],
            var options when HasButtonGroup(options, MessageOptions.YesNo) => ["Yes", "No"],
            var options when HasButtonGroup(options, MessageOptions.YesNoCancel) => ["Yes", "No", "Cancel"],
            var options when HasButtonGroup(options, MessageOptions.RetryCancel) => ["Retry", "Cancel"],
            _ => ["OK"],
        };
    }

    private static bool HasButtonGroup(MessageOptions options, MessageOptions buttonGroup) =>
        ((int)options & 0x70000) == (int)buttonGroup;

    private sealed class FarNetPanelsWindow : IWindow
    {
        public WindowKind Kind => WindowKind.Panels;
        public bool IsModal => false;
    }

    private static class Clipboard
    {
        private const uint CfUnicodeText = 13;
        private const uint GmemMoveable = 0x0002;

        public static bool TryGetText(out string? text)
        {
            text = null;
            if (!OperatingSystem.IsWindows())
                return false;

            if (!OpenClipboard(IntPtr.Zero))
                return false;

            try
            {
                IntPtr handle = GetClipboardData(CfUnicodeText);
                if (handle == IntPtr.Zero)
                    return false;

                IntPtr pointer = GlobalLock(handle);
                if (pointer == IntPtr.Zero)
                    return false;

                try
                {
                    text = Marshal.PtrToStringUni(pointer);
                    return true;
                }
                finally
                {
                    GlobalUnlock(handle);
                }
            }
            finally
            {
                CloseClipboard();
            }
        }

        public static void SetText(string text)
        {
            if (!OperatingSystem.IsWindows())
                return;

            if (!OpenClipboard(IntPtr.Zero))
                return;

            try
            {
                EmptyClipboard();
                byte[] bytes = System.Text.Encoding.Unicode.GetBytes(text + '\0');
                IntPtr handle = GlobalAlloc(GmemMoveable, (nuint)bytes.Length);
                if (handle == IntPtr.Zero)
                    return;

                IntPtr pointer = GlobalLock(handle);
                if (pointer == IntPtr.Zero)
                    return;

                try
                {
                    Marshal.Copy(bytes, 0, pointer, bytes.Length);
                }
                finally
                {
                    GlobalUnlock(handle);
                }

                _ = SetClipboardData(CfUnicodeText, handle);
            }
            finally
            {
                CloseClipboard();
            }
        }

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool OpenClipboard(IntPtr hWndNewOwner);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseClipboard();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetClipboardData(uint uFormat);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EmptyClipboard();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalAlloc(uint uFlags, nuint dwBytes);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalLock(IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalUnlock(IntPtr hMem);
    }

    private sealed class FarNetMenu : IMenu
    {
        private readonly FarNetModuleHostServices _services;
        private readonly Dictionary<(int VirtualKeyCode, ControlKeyStates State), EventHandler<MenuEventArgs>?> _keys = [];

        public FarNetMenu(FarNetModuleHostServices services)
        {
            _services = services;
        }

        public int X { get; set; }
        public int Y { get; set; }
        public int MaxHeight { get; set; }
        public string? Title { get; set; }
        public string? Bottom { get; set; }
        public IList<FarItem> Items { get; } = [];
        public int Selected { get; set; }
        public object? SelectedData => Selected >= 0 && Selected < Items.Count ? Items[Selected].Data : null;
        public string? HelpTopic { get; set; }
        public bool SelectLast { get; set; }
        public object? Sender { get; set; }
        public bool ShowAmpersands { get; set; }
        public bool WrapCursor { get; set; } = true;
        public bool AutoAssignHotkeys { get; set; }
        public bool NoShadow { get; set; }
        public KeyData Key { get; private set; } = KeyData.Empty;
        public bool ReverseAutoAssign { get; set; }
        public bool ChangeConsoleTitle { get; set; }
        public bool NoBox { get; set; }
        public bool NoMargin { get; set; }
        public bool SingleBox { get; set; }

        public bool Show()
        {
            var selectable = Items
                .Select((item, index) => new { Item = item, Index = index })
                .Where(candidate => !candidate.Item.Hidden && !candidate.Item.Disabled && !candidate.Item.IsSeparator)
                .ToArray();
            if (selectable.Length == 0)
                return false;

            int selected = Array.FindIndex(selectable, candidate => candidate.Index == Selected);
            if (selected < 0)
                selected = SelectLast ? selectable.Length - 1 : 0;

            var texts = selectable
                .Select(candidate => CleanText(candidate.Item.Text ?? string.Empty))
                .ToArray();
            int? chosen = _services.Ui.ShowMenu(Title ?? "FarNet", texts, selected);
            if (chosen is null)
                return false;

            var chosenItem = selectable[chosen.Value].Item;
            Selected = selectable[chosen.Value].Index;
            var args = new MenuEventArgs(chosenItem);
            chosenItem.Click?.Invoke(Sender ?? this, args);
            return !args.Ignore;
        }

        public FarItem Add(string text)
        {
            var item = new SetItem { Text = text };
            Items.Add(item);
            return item;
        }

        public FarItem Add(string text, EventHandler<MenuEventArgs> click)
        {
            var item = new SetItem { Text = text, Click = click };
            Items.Add(item);
            return item;
        }

        public void AddKey(int virtualKeyCode) =>
            AddKey(virtualKeyCode, ControlKeyStates.None, null);

        public void AddKey(int virtualKeyCode, ControlKeyStates controlKeyState) =>
            AddKey(virtualKeyCode, controlKeyState, null);

        public void AddKey(
            int virtualKeyCode,
            ControlKeyStates controlKeyState,
            EventHandler<MenuEventArgs>? handler)
        {
            _keys[(virtualKeyCode, controlKeyState)] = handler;
        }

        public void Lock()
        {
        }

        public void Unlock()
        {
        }

        private static string CleanText(string text) =>
            text.Replace("&", string.Empty, StringComparison.Ordinal);
    }
}

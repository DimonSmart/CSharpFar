using System.Globalization;
using CSharpFar.Core.Models;
using FarNet;

namespace CSharpFar.FarNetHost;

internal sealed class CSharpFarFarNetApi : IFar, IFarNetPanelHost
{
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
}

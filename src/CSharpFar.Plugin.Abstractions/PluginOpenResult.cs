namespace CSharpFar.Plugin.Abstractions;

public sealed record PluginOpenResult
{
    private PluginOpenResult(
        PluginOpenResultKind kind,
        string? message,
        IPluginPanel? panel)
    {
        Kind = kind;
        Message = message;
        Panel = panel;
    }

    public PluginOpenResultKind Kind { get; }
    public string? Message { get; }
    public IPluginPanel? Panel { get; }

    public static PluginOpenResult Completed() =>
        new(PluginOpenResultKind.Completed, null, null);

    public static PluginOpenResult NoPanel() =>
        new(PluginOpenResultKind.NoPanel, null, null);

    public static PluginOpenResult Failed(string message) =>
        new(PluginOpenResultKind.Failed, message, null);

    public static PluginOpenResult OpenedPanel(IPluginPanel panel) =>
        new(PluginOpenResultKind.OpenedPanel, null, panel);
}

public enum PluginOpenResultKind
{
    Completed,
    NoPanel,
    Failed,
    OpenedPanel,
}

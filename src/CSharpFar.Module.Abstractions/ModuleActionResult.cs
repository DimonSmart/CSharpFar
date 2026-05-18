namespace CSharpFar.Module.Abstractions;

public sealed record ModuleActionResult
{
    private ModuleActionResult(
        ModuleActionResultKind kind,
        string? message,
        IModulePanel? panel)
    {
        Kind = kind;
        Message = message;
        Panel = panel;
    }

    public ModuleActionResultKind Kind { get; }
    public string? Message { get; }
    public IModulePanel? Panel { get; }

    public static ModuleActionResult Completed() =>
        new(ModuleActionResultKind.Completed, null, null);

    public static ModuleActionResult NoPanel() =>
        new(ModuleActionResultKind.NoPanel, null, null);

    public static ModuleActionResult Failed(string message) =>
        new(ModuleActionResultKind.Failed, message, null);

    public static ModuleActionResult OpenedPanel(IModulePanel panel) =>
        new(ModuleActionResultKind.OpenedPanel, null, panel);
}

public enum ModuleActionResultKind
{
    Completed,
    NoPanel,
    Failed,
    OpenedPanel,
}

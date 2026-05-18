using CSharpFar.Module.Abstractions;

namespace CSharpFar.FarNetHost;

public sealed record FarNetModuleOpenResult
{
    private FarNetModuleOpenResult(
        FarNetModuleOpenResultKind kind,
        string? message,
        IModulePanel? panel)
    {
        Kind = kind;
        Message = message;
        Panel = panel;
    }

    public FarNetModuleOpenResultKind Kind { get; }
    public string? Message { get; }
    public IModulePanel? Panel { get; }

    public static FarNetModuleOpenResult Completed() =>
        new(FarNetModuleOpenResultKind.Completed, null, null);

    public static FarNetModuleOpenResult NoPanel() =>
        new(FarNetModuleOpenResultKind.NoPanel, null, null);

    public static FarNetModuleOpenResult Failed(string message) =>
        new(FarNetModuleOpenResultKind.Failed, message, null);

    public static FarNetModuleOpenResult OpenedPanel(IModulePanel panel) =>
        new(FarNetModuleOpenResultKind.OpenedPanel, null, panel);
}

public enum FarNetModuleOpenResultKind
{
    Completed,
    NoPanel,
    Failed,
    OpenedPanel,
}

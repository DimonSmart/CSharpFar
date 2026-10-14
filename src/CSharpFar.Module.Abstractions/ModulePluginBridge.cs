using CSharpFar.Core.Models;
using CSharpFar.Plugin.Abstractions;

namespace CSharpFar.Module.Abstractions;

/// <summary>Retains the native module's panel metadata without changing its connection or dialog logic.</summary>
public static class ModulePluginBridge
{
    public static PluginCommandResult ToCommandResult(ModuleActionResult result) =>
        result.Kind switch
        {
            ModuleActionResultKind.Failed => PluginCommandResult.Failed(result.Message ?? "Module operation failed."),
            ModuleActionResultKind.OpenedPanel => PluginCommandResult.OpenPanel(ToPanelRequest(result.Panel!)),
            _ => PluginCommandResult.Completed(),
        };

    public static PluginPanelOpenRequest ToPanelRequest(IModulePanel panel)
    {
        ArgumentNullException.ThrowIfNull(panel);
        var info = panel.GetOpenPanelInfo();
        return new PluginPanelOpenRequest
        {
            Source = panel,
            InitialDirectory = info.CurrentDirectory,
            Title = info.Title,
            Format = info.Format,
            ShortcutData = info.ShortcutData,
            FreeSize = info.FreeSize,
            KeyBarLabels = info.KeyBarLabels,
        };
    }
}

/// <summary>Allows the same manager command to expose its panel source through the provider API.</summary>
public sealed class ModuleConnectionPanelProvider(
    string id, string title, Func<PanelSide, ModuleActionResult> open) : IPanelProvider
{
    public string Id { get; } = id;
    public string Title { get; } = title;

    public ValueTask<PluginPanelOpenRequest> OpenAsync(
        IPanelProviderContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = open(context.TargetPanelSide);
        return result.Kind switch
        {
            ModuleActionResultKind.OpenedPanel =>
                ValueTask.FromResult(ModulePluginBridge.ToPanelRequest(result.Panel!)),
            ModuleActionResultKind.Failed =>
                throw new InvalidOperationException(result.Message ?? "Unable to open module panel."),
            _ => throw new OperationCanceledException("Panel opening was cancelled.", cancellationToken),
        };
    }
}

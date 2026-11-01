using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;

namespace CSharpFar.Plugin.Abstractions;

/// <summary>Optional plugin extension for Enter on an existing file. IDs are unique within a plugin.</summary>
public interface IFileOpenHandler
{
    string Id { get; }
    bool CanOpen(FilePanelItem item);
    ValueTask<PluginFileOpenResult> OpenAsync(FileOpenContext context, CancellationToken cancellationToken);
}

public sealed record FileOpenContext
{
    public required FilePanelItem Item { get; init; }
    public required PanelLocation Location { get; init; }
    public required IFilePanelSource Source { get; init; }
    public required PanelSide TargetPanelSide { get; init; }
    public required IPluginApplicationContext Application { get; init; }
    public required IPluginSettingsService Settings { get; init; }
    public required IPluginCredentialService Credentials { get; init; }
    public required IPluginUiService Ui { get; init; }
}

public enum PluginFileOpenResultKind { NotHandled, OpenPanel, Cancelled, Failed }

public sealed record PluginFileOpenResult
{
    private PluginFileOpenResult(PluginFileOpenResultKind kind, PluginPanelOpenRequest? panel, string? error)
        => (Kind, Panel, Error) = (kind, panel, error);

    public PluginFileOpenResultKind Kind { get; }
    public PluginPanelOpenRequest? Panel { get; }
    public string? Error { get; }
    public static PluginFileOpenResult NotHandled() => new(PluginFileOpenResultKind.NotHandled, null, null);
    public static PluginFileOpenResult OpenPanel(PluginPanelOpenRequest panel) =>
        new(PluginFileOpenResultKind.OpenPanel, panel ?? throw new ArgumentNullException(nameof(panel)), null);
    public static PluginFileOpenResult Cancelled() => new(PluginFileOpenResultKind.Cancelled, null, null);
    public static PluginFileOpenResult Failed(string error) =>
        new(PluginFileOpenResultKind.Failed, null, error);
}

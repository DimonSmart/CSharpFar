using CSharpFar.Core.Models;

namespace CSharpFar.Plugin.Abstractions;

public sealed record PluginOpenInfo
{
    public required PluginOpenFrom OpenFrom { get; init; }
    public Guid? SelectedItemId { get; init; }
    public PanelSide? PanelSide { get; init; }
    public string? CommandLineText { get; init; }
    public object? Payload { get; init; }
}

public enum PluginOpenFrom
{
    PluginMenu,
    LeftDiskMenu,
    RightDiskMenu,
    CommandLine,
    Editor,
    Viewer,
    FilePanel,
}

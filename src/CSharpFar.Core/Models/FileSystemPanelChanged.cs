namespace CSharpFar.Core.Models;

public sealed record FileSystemPanelChanged(
    PanelSide PanelSide,
    string DirectoryPath,
    FileSystemChangeKind Kind);

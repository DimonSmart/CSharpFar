using CSharpFar.Core.Models;
using CSharpFar.Ui;

namespace CSharpFar.FarNetHost;

public sealed record FarNetModuleHostServices
{
    public required ModuleUiServices Ui { get; init; }

    public required string DataRoot { get; init; }

    public required Func<PanelSide> GetActivePanelSide { get; init; }

    public required Func<PanelSide, FilePanelState> GetPanelState { get; init; }
}

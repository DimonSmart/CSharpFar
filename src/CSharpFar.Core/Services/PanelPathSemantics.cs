using CSharpFar.Core.Abstractions;

namespace CSharpFar.Core.Services;

public static class PanelPathSemantics
{
    public static IPanelPathSemantics Current { get; } = OperatingSystem.IsWindows()
        ? new WindowsPanelPathSemantics()
        : new UnixPanelPathSemantics();
}

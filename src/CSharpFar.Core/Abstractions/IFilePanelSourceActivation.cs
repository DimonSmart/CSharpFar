namespace CSharpFar.Core.Abstractions;

/// <summary>Optional post-load callback for actions that must not run before a panel is opened.</summary>
public interface IFilePanelSourceActivation
{
    void OnPanelOpened();
}

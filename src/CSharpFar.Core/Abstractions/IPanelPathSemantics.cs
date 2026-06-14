namespace CSharpFar.Core.Abstractions;

public interface IPanelPathSemantics
{
    bool IsRoot(string path);
    string? GetParentPath(string path);
    string GetFileName(string path);
    string TrimTrailingSeparators(string path);
}

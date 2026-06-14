namespace CSharpFar.Shell;

public interface IUnixAssociationLauncher
{
    bool TryOpen(string fullPath, string workingDirectory, out string? error);
}

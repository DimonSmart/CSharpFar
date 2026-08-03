namespace CSharpFar.Shell;

internal interface IWindowsAssociationLauncher
{
    void OpenDetached(WindowsAssociationLaunchRequest request);
}

internal sealed record WindowsAssociationLaunchRequest(
    string FullPath,
    string WorkingDirectory,
    string Verb,
    bool SuppressConsole);

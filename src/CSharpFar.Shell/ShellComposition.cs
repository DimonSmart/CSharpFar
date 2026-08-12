namespace CSharpFar.Shell;

public static class ShellComposition
{
    public static ShellRegistry CreateRegistry() =>
        new(new ShellProfile(
            "powershell",
            ["ps", "pwsh", "powershell"],
            new PowerShellCommandLineBuilder()));
}

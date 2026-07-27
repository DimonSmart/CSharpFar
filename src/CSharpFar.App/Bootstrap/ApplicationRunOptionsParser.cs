namespace CSharpFar.App.Bootstrap;

public static class ApplicationRunOptionsParser
{
    public static bool TryParse(
        IReadOnlyList<string> args,
        out ApplicationRunOptions options,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Count == 0)
        {
            options = ApplicationRunOptions.Normal;
            error = null;
            return true;
        }

        if (args.Count == 2 && string.Equals(args[0], "--demo", StringComparison.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(args[1]))
            {
                options = ApplicationRunOptions.Normal;
                error = "Demo mode requires a fixture directory path.\nUsage: csharpfar --demo <root-path>";
                return false;
            }

            options = new ApplicationRunOptions(ApplicationRunMode.Demo, args[1]);
            error = null;
            return true;
        }

        if (args.Count == 1 && string.Equals(args[0], "--demo", StringComparison.Ordinal))
        {
            options = ApplicationRunOptions.Normal;
            error = "Demo mode requires a fixture directory path.\nUsage: csharpfar --demo <root-path>";
            return false;
        }

        options = ApplicationRunOptions.Normal;
        error = "Unknown command-line arguments.";
        return false;
    }
}

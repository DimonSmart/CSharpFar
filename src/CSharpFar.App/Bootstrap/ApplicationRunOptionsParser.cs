namespace CSharpFar.App.Bootstrap;

public static class ApplicationRunOptionsParser
{
    public static bool TryParse(
        IReadOnlyList<string> args,
        out ApplicationRunOptions options,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(args);

        ApplicationRunMode mode = ApplicationRunMode.Normal;
        string? demoRootPath = null;
        bool diagnosticsEnabled = false;

        for (int index = 0; index < args.Count; index++)
        {
            string argument = args[index];

            if (string.Equals(argument, "--diagnostics", StringComparison.Ordinal))
            {
                if (diagnosticsEnabled)
                    return FailUnknown(out options, out error);

                diagnosticsEnabled = true;
                continue;
            }

            if (string.Equals(argument, "--demo", StringComparison.Ordinal))
            {
                if (mode == ApplicationRunMode.Demo)
                    return FailUnknown(out options, out error);

                if (index + 1 >= args.Count ||
                    string.IsNullOrWhiteSpace(args[index + 1]) ||
                    args[index + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    options = ApplicationRunOptions.Normal;
                    error = "Demo mode requires a fixture directory path.\nUsage: csharpfar --demo <root-path>";
                    return false;
                }

                mode = ApplicationRunMode.Demo;
                demoRootPath = args[++index];
                continue;
            }

            return FailUnknown(out options, out error);
        }

        options = new ApplicationRunOptions(mode, demoRootPath, diagnosticsEnabled);
        error = null;
        return true;
    }

    private static bool FailUnknown(
        out ApplicationRunOptions options,
        out string? error)
    {
        options = ApplicationRunOptions.Normal;
        error = "Unknown command-line arguments.";
        return false;
    }
}

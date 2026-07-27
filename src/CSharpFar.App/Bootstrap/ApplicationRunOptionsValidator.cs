namespace CSharpFar.App.Bootstrap;

public static class ApplicationRunOptionsValidator
{
    public static bool TryValidate(
        ApplicationRunOptions options,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.Mode != ApplicationRunMode.Demo)
        {
            error = null;
            return true;
        }

        if (string.IsNullOrWhiteSpace(options.DemoRootPath))
        {
            error = "Demo mode requires a fixture directory path.\nUsage: csharpfar --demo <root-path>";
            return false;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(options.DemoRootPath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            error = ex.Message;
            return false;
        }

        if (!Directory.Exists(fullPath))
        {
            error = $"Demo fixture directory does not exist: {fullPath}";
            return false;
        }

        error = null;
        return true;
    }
}

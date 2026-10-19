using System.Diagnostics;
using System.Text;

namespace CSharpFar.Shell;

internal sealed record MacOsUserShellPathResult(
    string? Path, string Shell, bool Success, string? FailureReason, long ElapsedMilliseconds);

internal sealed record MacOsShellProbeResult(bool Success, string? Output, string? FailureReason);

/// <summary>Reads PATH once from the user's login interactive shell, never from each command.</summary>
internal sealed class MacOsUserShellPathResolver
{
    internal static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(3);
    private const int MaxCapturedCharacters = 64 * 1024;

    private readonly Func<string?> _readShell;
    private readonly Func<ProcessStartInfo, string, string, MacOsShellProbeResult> _runProbe;

    internal MacOsUserShellPathResolver(
        Func<string?>? readShell = null,
        Func<ProcessStartInfo, string, string, MacOsShellProbeResult>? runProbe = null)
    {
        _readShell = readShell ?? (() => Environment.GetEnvironmentVariable("SHELL"));
        _runProbe = runProbe ?? RunProbe;
    }

    internal MacOsUserShellPathResult Resolve()
    {
        var watch = Stopwatch.StartNew();
        string shell;
        try
        {
            shell = _readShell() is { Length: > 0 } configured
                ? configured : "/bin/zsh";
        }
        catch (Exception ex)
        {
            return Failure("login shell lookup failed (" + ex.GetType().Name + ")",
                "unknown", watch);
        }

        if (!shell.StartsWith("/", StringComparison.Ordinal) ||
            Path.GetFileName(shell) is not ("zsh" or "bash"))
            return Failure("unsupported login shell", shell, watch);

        // This random token prevents startup banner / theme output from being mistaken for PATH.
        string token = Guid.NewGuid().ToString("N");
        string start = "__CSHARPFAR_PATH_START_" + token + "__";
        string end = "__CSHARPFAR_PATH_END_" + token + "__";
        var info = new ProcessStartInfo(shell)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        info.ArgumentList.Add("-lic");
        info.ArgumentList.Add($"printf '\\n%s\\n%s\\n%s\\n' '{start}' \"$PATH\" '{end}'");

        try
        {
            MacOsShellProbeResult probe = _runProbe(info, start, end);
            if (!probe.Success)
                return Failure(probe.FailureReason ?? "shell probe failed", shell, watch);

            string output = probe.Output ?? "";
            string beginDelimiter = "\n" + start + "\n";
            string endDelimiter = "\n" + end + "\n";
            int begin = output.LastIndexOf(beginDelimiter, StringComparison.Ordinal);
            if (begin < 0)
                return Failure("missing PATH start marker", shell, watch);
            begin += beginDelimiter.Length;
            int finish = output.IndexOf(endDelimiter, begin, StringComparison.Ordinal);
            if (finish < 0)
                return Failure("missing PATH end marker", shell, watch);

            string path = output[begin..finish];
            if (string.IsNullOrWhiteSpace(path) || path.Length > MaxCapturedCharacters ||
                path.Any(char.IsControl) ||
                path.Split(':').All(static segment => segment.Length == 0))
                return Failure("invalid or empty shell PATH", shell, watch);

            return new MacOsUserShellPathResult(path, shell, true, null,
                watch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            return Failure("shell probe error (" + ex.GetType().Name + ")", shell, watch);
        }
    }

    private static MacOsUserShellPathResult Failure(string reason, string shell, Stopwatch watch) =>
        new(null, shell, false, reason, watch.ElapsedMilliseconds);

    internal static MacOsShellProbeResult RunProbe(ProcessStartInfo info, string _, string __)
    {
        using var process = new Process { StartInfo = info };
        using var timeout = new CancellationTokenSource(ProbeTimeout);
        try
        {
            if (!process.Start())
                return new(false, null, "shell process did not start");
            process.StandardInput.Close();

            // Both redirected streams are drained concurrently, with bounded buffers.
            Task<string> stdout = ReadBoundedAsync(process.StandardOutput, timeout.Token);
            Task<string> stderr = ReadBoundedAsync(process.StandardError, timeout.Token);
            Task exit = process.WaitForExitAsync(timeout.Token);
            Task.WhenAll(stdout, stderr, exit).GetAwaiter().GetResult();
            return process.ExitCode == 0
                ? new(true, stdout.Result, null)
                : new(false, null, "shell exited with code " + process.ExitCode);
        }
        catch (OperationCanceledException)
        {
            return new(false, null, "shell startup timed out");
        }
        catch (InvalidDataException)
        {
            return new(false, null, "shell startup output exceeded limit");
        }
        catch (Exception ex)
        {
            return new(false, null, "shell process error (" + ex.GetType().Name + ")");
        }
        finally
        {
            try
            {
                if (process.Id != 0 && !process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch (Exception)
            {
                // A shell or child may have exited before the kill request.
            }
        }
    }

    private static async Task<string> ReadBoundedAsync(
        StreamReader reader, CancellationToken cancellationToken)
    {
        char[] buffer = new char[4096];
        var output = new StringBuilder();
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken)
                   .ConfigureAwait(false)) != 0)
        {
            if (output.Length + count > MaxCapturedCharacters)
                throw new InvalidDataException("shell probe output limit");
            output.Append(buffer, 0, count);
        }
        return output.ToString();
    }
}

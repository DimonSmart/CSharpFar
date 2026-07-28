using System.Diagnostics;

namespace CSharpFar.DemoRecorder;

internal static class FfmpegArtifactBuilder
{
    public static async Task BuildAsync(
        string framesDirectory,
        string gifPath,
        string mp4Path,
        int framesPerSecond,
        CancellationToken cancellationToken)
    {
        string inputPattern = Path.Combine(framesDirectory, "%06d.png");
        string palettePath = Path.Combine(framesDirectory, $"palette-{Guid.NewGuid():N}.png");

        await RunAsync(
            $"-nostdin -loglevel error -y -framerate {framesPerSecond} -i \"{inputPattern}\" -vf \"palettegen=stats_mode=diff\" -frames:v 1 -update 1 \"{palettePath}\"",
            cancellationToken);

        await RunAsync(
            $"-nostdin -loglevel error -y -framerate {framesPerSecond} -i \"{inputPattern}\" -i \"{palettePath}\" -lavfi \"paletteuse=dither=bayer:bayer_scale=3\" \"{gifPath}\"",
            cancellationToken);

        await RunAsync(
            $"-nostdin -loglevel error -y -framerate {framesPerSecond} -i \"{inputPattern}\" -c:v libx264 -pix_fmt yuv420p -movflags +faststart \"{mp4Path}\"",
            cancellationToken);

        TryDeletePalette(palettePath);
    }

    private static async Task RunAsync(string arguments, CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = arguments,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };

        process.Start();
        Task<string> stdOutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> stdErrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await Task.WhenAll(stdOutTask, stdErrTask, process.WaitForExitAsync(cancellationToken));
        string stdOut = stdOutTask.Result;
        string stdErr = stdErrTask.Result;

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"ffmpeg failed with exit code {process.ExitCode}.{Environment.NewLine}{stdOut}{Environment.NewLine}{stdErr}");
        }
    }

    private static void TryDeletePalette(string palettePath)
    {
        try
        {
            if (File.Exists(palettePath))
                File.Delete(palettePath);
        }
        catch
        {
        }
    }
}

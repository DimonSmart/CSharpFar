using CSharpFar.Console.Models;

namespace CSharpFar.DemoRecorder;

internal sealed class FrameSequenceWriter
{
    private readonly string _framesDirectory;
    private int _frameIndex;

    public FrameSequenceWriter(string framesDirectory)
    {
        _framesDirectory = framesDirectory;
    }

    public void Append(
        ScreenSnapshot snapshot,
        int cursorX,
        int cursorY,
        bool cursorVisible,
        int holdMs,
        SnapshotRasterizer rasterizer,
        int framesPerSecond)
    {
        int frameCount = Math.Max(1, (int)Math.Ceiling(holdMs / 1000d * framesPerSecond));
        string tempPath = Path.Combine(_framesDirectory, "_template.png");
        rasterizer.SaveFrame(snapshot, cursorX, cursorY, cursorVisible, tempPath);

        for (int i = 0; i < frameCount; i++)
        {
            _frameIndex++;
            string target = Path.Combine(_framesDirectory, $"{_frameIndex:D6}.png");
            File.Copy(tempPath, target, overwrite: true);
        }

        File.Delete(tempPath);
    }
}

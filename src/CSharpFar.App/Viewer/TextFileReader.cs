using System.Text;
using CSharpFar.Core.Text;

namespace CSharpFar.App.Viewer;

/// <summary>
/// Reads text files with automatic encoding detection.
/// Uses the shared bounded-sample detector before reading the editor/Quick View content.
/// </summary>
public static class TextFileReader
{
    public const long MaxFileSizeBytes = 10L * 1024 * 1024; // 10 MB
    private const int MaxEncodingSampleBytes = 64 * 1024;

    /// <summary>Reads all lines from a text file.</summary>
    /// <exception cref="IOException">Thrown if the file cannot be read.</exception>
    public static string[] ReadLines(string filePath) =>
        ReadLinesAndEncoding(filePath).Lines;

    /// <summary>
    /// Reads all lines and returns the encoding that was used.
    /// The encoding can be passed back to <see cref="File.WriteAllText"/> when saving.
    /// </summary>
    public static (string[] Lines, Encoding Encoding) ReadLinesAndEncoding(string filePath)
    {
        byte[] sample = ReadSample(filePath);
        var detection = TextEncodingDetector.Detect(sample);
        byte[] bytes = File.ReadAllBytes(filePath);

        using var stream = new MemoryStream(
            bytes,
            detection.ContentStartLength,
            bytes.Length - detection.ContentStartLength,
            writable: false);
        using var reader = new StreamReader(
            stream,
            detection.Encoding,
            detectEncodingFromByteOrderMarks: false);

        var lines = new List<string>();
        while (reader.ReadLine() is { } line)
            lines.Add(line);

        return ([.. lines], detection.Encoding);
    }

    private static byte[] ReadSample(string filePath)
    {
        using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        int sampleLength = (int)Math.Min(stream.Length, MaxEncodingSampleBytes);
        var sample = new byte[sampleLength];
        int totalRead = 0;
        while (totalRead < sample.Length)
        {
            int read = stream.Read(sample, totalRead, sample.Length - totalRead);
            if (read == 0)
                break;

            totalRead += read;
        }

        if (totalRead == sample.Length)
            return sample;

        Array.Resize(ref sample, totalRead);
        return sample;
    }
}

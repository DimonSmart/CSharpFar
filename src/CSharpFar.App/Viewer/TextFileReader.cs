using System.Text;

namespace CSharpFar.App.Viewer;

/// <summary>
/// Reads text files with automatic encoding detection.
/// Detects UTF-8/UTF-16 BOMs; falls back to the system default encoding
/// if the content is not valid UTF-8.
/// </summary>
public static class TextFileReader
{
    public const long MaxFileSizeBytes = 10L * 1024 * 1024; // 10 MB

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
        try
        {
            using var reader = new StreamReader(
                filePath,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
                detectEncodingFromByteOrderMarks: true);

            var lines = new List<string>();
            while (reader.ReadLine() is { } line)
                lines.Add(line);

            // CurrentEncoding reflects BOM detection (UTF-16 LE/BE, or UTF-8 BOM → Encoding.UTF8)
            return ([.. lines], reader.CurrentEncoding);
        }
        catch (DecoderFallbackException)
        {
            var enc = Encoding.Default;
            return (File.ReadAllLines(filePath, enc), enc);
        }
    }
}

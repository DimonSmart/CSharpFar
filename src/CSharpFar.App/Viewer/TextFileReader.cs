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

    /// <summary>
    /// Reads all lines from a text file.
    /// </summary>
    /// <exception cref="IOException">Thrown if the file cannot be read.</exception>
    public static string[] ReadLines(string filePath)
    {
        try
        {
            // detectEncodingFromByteOrderMarks: true handles UTF-8, UTF-16 LE/BE BOMs.
            // The fallback encoding (UTF-8 strict) throws on invalid bytes.
            using var reader = new StreamReader(
                filePath,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
                detectEncodingFromByteOrderMarks: true);

            var lines = new List<string>();
            while (reader.ReadLine() is { } line)
                lines.Add(line);

            return [.. lines];
        }
        catch (DecoderFallbackException)
        {
            // Not valid UTF-8: fall back to the system ANSI code page (Windows-1252 on en-US Windows)
            return File.ReadAllLines(filePath, Encoding.Default);
        }
    }
}

using System.Diagnostics;

namespace CSharpFar.App.History;

/// <summary>Serializes local processes by a persistent sidecar lock and replaces JSON atomically.</summary>
internal static class AtomicHistoryFile
{
    private const int LockAttempts = 50;
    private const int MoveAttempts = 5;
    private const int RetryDelayMilliseconds = 50;

    public static FileStream Acquire(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string lockPath = path + ".lock";
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (attempt < LockAttempts - 1)
            {
                Thread.Sleep(RetryDelayMilliseconds);
            }
        }
    }

    public static void Write(string path, string json, Action<string, string, bool>? moveFile = null)
    {
        string temporary = Path.Combine(Path.GetDirectoryName(path)!,
            $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporary, json);
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    if (moveFile is null)
                        File.Move(temporary, path, true);
                    else
                        moveFile(temporary, path, true);
                    return;
                }
                catch (IOException) when (attempt < MoveAttempts - 1)
                {
                    Thread.Sleep(RetryDelayMilliseconds);
                }
            }
        }
        finally
        {
            try
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    public static void Report(string store, string operation, Exception error) =>
        Trace.TraceError($"History {store} {operation} failed ({error.GetType().Name}).");
}

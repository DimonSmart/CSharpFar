namespace CSharpFar.Plugin.AzureBlob;

public static class AzureBlobPath
{
    // A trailing slash explicitly identifies a virtual directory. Never fold case,
    // decode percent escapes, collapse repeated separators or reinterpret dot segments.
    public static string Normalize(string path)
    {
        if (string.IsNullOrEmpty(path) || path == "/") return "/";
        string result = path.StartsWith('/') ? path : "/" + path;
        string body = result[1..];
        if (body.EndsWith('/')) body = body[..^1];
        if (body.Split('/').Any(segment => segment.Length == 0 || segment is "." or ".." ||
            segment.Contains('\\') || segment.Any(char.IsControl)))
            throw new IOException("This blob name cannot be represented safely in a file panel.");
        return result;
    }

    public static string Prefix(string path)
    {
        var normalized = Normalize(path);
        return normalized == "/" ? "" : normalized[1..].TrimEnd('/') + "/";
    }

    public static string BlobName(string path)
    {
        var normalized = Normalize(path);
        if (normalized == "/" || normalized.EndsWith('/'))
            throw new IOException("Expected a file path.");
        return normalized[1..];
    }

    public static string? Parent(string path)
    {
        var normalized = Normalize(path);
        if (normalized == "/") return null;
        return normalized[..(normalized.TrimEnd('/').LastIndexOf('/') + 1)];
    }

    public static string Name(string path)
    {
        var normalized = Normalize(path).TrimEnd('/');
        return normalized[(normalized.LastIndexOf('/') + 1)..];
    }

    public static string Combine(string parent, string name)
    {
        if (string.IsNullOrEmpty(name) || name.Contains('/'))
            throw new IOException("Expected one file-name component.");
        return Normalize("/" + Prefix(parent) + name);
    }
}

using System.Text.Json;

namespace CSharpFar.Module.Ftp;

public sealed class FtpConnectionStore : IFtpConnectionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _path;

    public FtpConnectionStore(string configDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configDirectory);
        Directory.CreateDirectory(configDirectory);
        _path = Path.Combine(configDirectory, "ftp-connections.json");
    }

    public IReadOnlyList<FtpConnectionInfo> Load()
    {
        if (!File.Exists(_path))
            return [];

        using var stream = File.OpenRead(_path);
        return JsonSerializer.Deserialize<List<FtpConnectionInfo>>(stream) ?? [];
    }

    public void Save(IReadOnlyList<FtpConnectionInfo> connections)
    {
        ArgumentNullException.ThrowIfNull(connections);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);

        using var stream = File.Create(_path);
        JsonSerializer.Serialize(stream, connections, JsonOptions);
    }
}

using System.Text.Json;

namespace CSharpFar.Plugin.Sftp;

public sealed class SftpConnectionStore : ISftpConnectionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _path;

    public SftpConnectionStore(string configDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configDirectory);
        Directory.CreateDirectory(configDirectory);
        _path = Path.Combine(configDirectory, "connections.json");
    }

    public IReadOnlyList<SftpConnectionInfo> Load()
    {
        if (!File.Exists(_path))
            return [];

        using var stream = File.OpenRead(_path);
        return JsonSerializer.Deserialize<List<SftpConnectionInfo>>(stream) ?? [];
    }

    public void Save(IReadOnlyList<SftpConnectionInfo> connections)
    {
        ArgumentNullException.ThrowIfNull(connections);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);

        using var stream = File.Create(_path);
        JsonSerializer.Serialize(stream, connections, JsonOptions);
    }
}

using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace CSharpFar.Architecture.Tests;

public sealed class PlatformDependencyArchitectureTests
{
    private enum Zone { Neutral, Windows, Ansi, Linux, MacOs, HostWindows, HostLinux, HostMacOs, Tool, Package }

    private static readonly Dictionary<string, Zone> Zones = new(StringComparer.Ordinal)
    {
        ["CSharpFar.App"] = Zone.Neutral,
        ["CSharpFar.Console"] = Zone.Neutral,
        ["CSharpFar.Core"] = Zone.Neutral,
        ["CSharpFar.FileSystem"] = Zone.Neutral,
        ["CSharpFar.Module.Abstractions"] = Zone.Neutral,
        ["CSharpFar.Module.Archives"] = Zone.Neutral,
        ["CSharpFar.Module.Ftp"] = Zone.Neutral,
        ["CSharpFar.Module.NetworkTraffic"] = Zone.Neutral,
        ["CSharpFar.Module.ProcessesAndPorts"] = Zone.Neutral,
        ["CSharpFar.Module.Sftp"] = Zone.Neutral,
        ["CSharpFar.NetworkTraffic"] = Zone.Neutral,
        ["CSharpFar.Platform.Abstractions"] = Zone.Neutral,
        ["CSharpFar.Plugin.Abstractions"] = Zone.Neutral,
        ["CSharpFar.Shell"] = Zone.Neutral,
        ["CSharpFar.Ui"] = Zone.Neutral,
        ["CSharpFar.Console.Windows"] = Zone.Windows,
        ["CSharpFar.NetworkTraffic.Windows"] = Zone.Windows,
        ["CSharpFar.Platform.Windows"] = Zone.Windows,
        ["CSharpFar.Console.Ansi"] = Zone.Ansi,
        ["CSharpFar.Platform.Linux"] = Zone.Linux,
        ["CSharpFar.Platform.MacOs"] = Zone.MacOs,
        ["CSharpFar.Host.Windows"] = Zone.HostWindows,
        ["CSharpFar.Host.Linux"] = Zone.HostLinux,
        ["CSharpFar.Host.MacOs"] = Zone.HostMacOs,
        ["CSharpFar.DemoRecorder"] = Zone.Tool,
        ["DimonSmart.Terminal"] = Zone.Package,
        ["DimonSmart.Terminal.Ansi"] = Zone.Package,
        ["DimonSmart.Terminal.Windows"] = Zone.Package,
        ["DimonSmart.Terminal.Ui"] = Zone.Package,
    };

    private static readonly HashSet<string> ForbiddenPackages = new(StringComparer.OrdinalIgnoreCase)
    {
        "Microsoft.Diagnostics.Tracing.TraceEvent",
        "System.Security.Cryptography.ProtectedData",
    };

    private static readonly HashSet<string> WindowsLibraries = new(StringComparer.OrdinalIgnoreCase)
    {
        "kernel32", "user32", "advapi32", "iphlpapi",
    };

    [Fact]
    public void AllProductionProjectsHaveExplicitClassificationAndCorrectProjectClosure()
    {
        var graph = ReadGraph();
        Assert.Equal(Zones.Keys.Order(StringComparer.Ordinal), graph.Keys.Order(StringComparer.Ordinal));
        foreach (var (name, refs) in graph)
        {
            Assert.All(refs, dependency => Assert.True(graph.ContainsKey(dependency),
                $"Unknown ProjectReference in {name}: {dependency}"));

            var zone = Zones[name];
            if (zone is Zone.Package or Zone.Tool) continue;
            var allowed = Allowed(zone);
            var path = FindForbiddenPath(graph, name, x => !allowed.Contains(Zones[x]));
            Assert.True(path is null, $"{name} violates platform separation: {string.Join(" -> ", path ?? [])}");
        }
    }

    [Fact]
    public void NeutralProjectsHaveNoWindowsOnlyPackagesInRestoredClosure()
    {
        string root = RepositoryPaths.FindRoot();
        foreach (string name in Zones.Where(x => x.Value == Zone.Neutral).Select(x => x.Key))
        {
            string projectFile = Path.Combine(root, "src", name, name + ".csproj");
            var document = XDocument.Load(projectFile);
            foreach (var reference in document.Descendants("PackageReference"))
            {
                string package = (string?)reference.Attribute("Include") ?? "";
                Assert.False(IsForbiddenPackage(package), $"{name} directly references {package}");
            }

            string assetsFile = Path.Combine(root, "src", name, "obj", "project.assets.json");
            Assert.True(File.Exists(assetsFile), $"Build/restore {name} before architecture tests: {assetsFile}");
            using var assets = JsonDocument.Parse(File.ReadAllText(assetsFile));
            foreach (var item in assets.RootElement.GetProperty("libraries").EnumerateObject())
            {
                string id = item.Name.Split('/')[0];
                Assert.False(IsForbiddenPackage(id), $"{name} transitively resolves {item.Name}");
            }
        }
    }

    [Fact]
    public void NeutralProjectsDoNotDeclareWin32Imports()
    {
        string root = RepositoryPaths.FindRoot();
        var violations = new List<string>();
        foreach (string name in Zones.Where(x => x.Value == Zone.Neutral).Select(x => x.Key))
        {
            foreach (string source in Directory.EnumerateFiles(Path.Combine(root, "src", name), "*.cs", SearchOption.AllDirectories)
                .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")))
            {
                string code = File.ReadAllText(source);
                foreach (Match match in Regex.Matches(code, @"(?:DllImport|LibraryImport|NativeLibrary\s*\.\s*Load)\s*\(\s*""([^""]+)"""))
                {
                    if (IsWindowsLibrary(match.Groups[1].Value))
                        violations.Add($"{name}: {match.Groups[1].Value} in {source}");
                }
            }
        }
        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void ArchitectureChecksRejectSyntheticViolations()
    {
        var graph = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["CSharpFar.App"] = ["CSharpFar.Core"],
            ["CSharpFar.Core"] = ["CSharpFar.Platform.Windows"],
            ["CSharpFar.Platform.Windows"] = [],
        };
        Assert.Equal(
            ["CSharpFar.App", "CSharpFar.Core", "CSharpFar.Platform.Windows"],
            FindForbiddenPath(graph, "CSharpFar.App", x => x == "CSharpFar.Platform.Windows"));
        Assert.True(IsForbiddenPackage("Microsoft.Diagnostics.Tracing.TraceEvent"));
        Assert.True(IsForbiddenPackage("System.Security.Cryptography.ProtectedData"));
        Assert.False(IsForbiddenPackage("Microsoft.Extensions.Logging"));
        Assert.True(IsWindowsLibrary("kernel32.DLL"));
        Assert.True(IsWindowsLibrary("IPHLPAPI"));
        Assert.False(IsWindowsLibrary("libc.so.6"));
        Assert.DoesNotContain(Zone.MacOs, Allowed(Zone.Windows));
        Assert.Contains(Zone.Ansi, Allowed(Zone.MacOs));
    }

    private static bool IsForbiddenPackage(string id) => ForbiddenPackages.Contains(id);
    private static bool IsWindowsLibrary(string library)
    {
        string normalized = Path.GetFileName(library);
        if (normalized.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[..^4];
        return WindowsLibraries.Contains(normalized);
    }

    private static HashSet<Zone> Allowed(Zone zone) => zone switch
    {
        Zone.Neutral => [Zone.Neutral],
        Zone.Windows or Zone.HostWindows => [Zone.Neutral, Zone.Windows, Zone.HostWindows],
        Zone.Ansi => [Zone.Neutral, Zone.Ansi],
        Zone.Linux or Zone.HostLinux => [Zone.Neutral, Zone.Ansi, Zone.Linux, Zone.HostLinux],
        Zone.MacOs or Zone.HostMacOs => [Zone.Neutral, Zone.Ansi, Zone.MacOs, Zone.HostMacOs],
        _ => throw new InvalidOperationException($"No production dependency policy for {zone}"),
    };

    private static Dictionary<string, string[]> ReadGraph()
    {
        string root = Path.Combine(RepositoryPaths.FindRoot(), "src");
        var projects = Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories).ToArray();
        var graph = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (string project in projects)
        {
            string name = Path.GetFileNameWithoutExtension(project);
            Assert.True(Zones.ContainsKey(name), $"Classify new project explicitly: {name}");
            XDocument xml = XDocument.Load(project);
            graph.Add(name, xml.Descendants("ProjectReference")
                .Select(reference => (string?)reference.Attribute("Include"))
                .Where(reference => reference is not null)
                .Select(reference => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(project)!,
                    reference!.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar))))
                .Select(Path.GetFileNameWithoutExtension)
                .Select(name => name!)
                .ToArray());
        }
        return graph;
    }

    private static string[]? FindForbiddenPath(
        IReadOnlyDictionary<string, string[]> graph, string root, Func<string, bool> forbidden)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal) { root };
        var queue = new Queue<string[]>();
        queue.Enqueue([root]);
        while (queue.Count > 0)
        {
            var chain = queue.Dequeue();
            foreach (string next in graph[chain[^1]])
            {
                if (!visited.Add(next)) continue;
                var extended = chain.Append(next).ToArray();
                if (forbidden(next)) return extended;
                queue.Enqueue(extended);
            }
        }
        return null;
    }
}

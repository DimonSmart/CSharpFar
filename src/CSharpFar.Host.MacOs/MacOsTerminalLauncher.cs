using System.Diagnostics;
using System.Xml;
using System.Xml.Linq;
using CSharpFar.App.Settings;
using CSharpFar.Core.Models;

namespace CSharpFar.Host.MacOs;

/// <summary>Starts a terminal for a no-TTY CSharpFar.app launch, without touching CLI sessions.</summary>
internal sealed class MacOsTerminalLauncher
{
    private readonly Func<string, bool> _fileExists;
    private readonly Func<string, bool> _executable;
    private readonly Func<string, string?> _bundleIdentifier;
    private readonly Func<ProcessStartInfo, bool> _start;
    private readonly Func<string> _homeDirectory;
    private readonly Action<string> _error;

    internal MacOsTerminalLauncher(
        Func<string, bool>? fileExists = null,
        Func<string, bool>? executable = null,
        Func<string, string?>? bundleIdentifier = null,
        Func<ProcessStartInfo, bool>? start = null,
        Func<string>? homeDirectory = null,
        Action<string>? error = null)
    {
        _fileExists = fileExists ?? File.Exists;
        _executable = executable ?? IsExecutable;
        _bundleIdentifier = bundleIdentifier ?? ReadBundleIdentifier;
        _start = start ?? Start;
        _homeDirectory = homeDirectory ?? GetHomeDirectory;
        _error = error ?? System.Console.Error.WriteLine;
    }

    internal static bool TryGetBundlePaths(
        string? processPath,
        out string executablePath,
        out string resourcesDirectory)
    {
        executablePath = "";
        resourcesDirectory = "";

        if (string.IsNullOrEmpty(processPath))
            return false;

        try
        {
            string fullPath = Path.GetFullPath(processPath);
            string? resources = Path.GetDirectoryName(fullPath);
            string? contents = resources is null ? null : Path.GetDirectoryName(resources);
            string? app = contents is null ? null : Path.GetDirectoryName(contents);

            if (Path.GetFileName(fullPath) != "csharpfar" ||
                Path.GetFileName(resources) != "Resources" ||
                Path.GetFileName(contents) != "Contents" ||
                app is null || !app.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
                return false;

            executablePath = fullPath;
            resourcesDirectory = resources!;
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    /// <summary>0: launch request accepted, 1: launch failed, 2: not an app-bundle executable.</summary>
    internal int Launch(string? processPath)
    {
        if (!TryGetBundlePaths(processPath, out string executablePath, out string resources))
        {
            _error("--launch-from-finder is supported only inside CSharpFar.app.");
            return 2;
        }

        string home = _homeDirectory();
        if (string.IsNullOrWhiteSpace(home) || !Path.IsPathFullyQualified(home))
        {
            _error("Unable to determine a valid home directory for the app launch.");
            return 1;
        }

        string preference = JsonSettingsStore.ReadMacOsTerminalPreference(
            resources, _error);
        return LaunchWithPreference(preference, executablePath, resources, home);
    }

    /// <summary>Separate decision/launch path for deterministic tests.</summary>
    internal int LaunchWithPreference(
        string? preference,
        string executablePath,
        string resourcesDirectory,
        string homeDirectory)
    {
        string selected = MacOsTerminalPreference.Normalize(preference);
        if (selected != MacOsTerminalPreference.Terminal)
        {
            string? kitty = FindKitty(homeDirectory);
            if (kitty is not null && TryLaunchKitty(kitty, executablePath, homeDirectory))
                return 0;
        }

        return TryLaunchTerminal(Path.Combine(resourcesDirectory, "launch.command"), homeDirectory)
            ? 0
            : 1;
    }

    internal string? FindKitty(string homeDirectory)
    {
        string[] candidates =
        [
            "/Applications/kitty.app",
            Path.Combine(homeDirectory, "Applications", "kitty.app"),
        ];

        foreach (string candidate in candidates)
        {
            try
            {
                string infoPlist = Path.Combine(candidate, "Contents", "Info.plist");
                string executable = Path.Combine(candidate, "Contents", "MacOS", "kitty");
                if (_fileExists(infoPlist) &&
                    _bundleIdentifier(infoPlist) == "net.kovidgoyal.kitty" &&
                    _fileExists(executable) &&
                    _executable(executable))
                    return executable;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                       System.Security.SecurityException or ArgumentException or
                                       NotSupportedException or XmlException)
            {
                _error($"Unable to inspect a Kitty candidate ({ex.GetType().Name}).");
            }
        }

        return null;
    }

    private bool TryLaunchKitty(string kittyExecutable, string csharpfarExecutable, string home)
    {
        var info = new ProcessStartInfo(kittyExecutable)
        {
            UseShellExecute = false,
            WorkingDirectory = home,
        };
        info.ArgumentList.Add("--directory");
        info.ArgumentList.Add(home);
        info.ArgumentList.Add("--session");
        info.ArgumentList.Add("none");
        info.ArgumentList.Add(csharpfarExecutable);
        return TryLaunch(info, "Kitty");
    }

    private bool TryLaunchTerminal(string launchCommand, string home)
    {
        var info = new ProcessStartInfo("/usr/bin/open")
        {
            UseShellExecute = false,
            WorkingDirectory = home,
        };
        info.ArgumentList.Add("-a");
        info.ArgumentList.Add("Terminal");
        info.ArgumentList.Add(launchCommand);
        return TryLaunch(info, "Terminal.app");
    }

    private bool TryLaunch(ProcessStartInfo info, string provider)
    {
        try
        {
            if (_start(info))
                return true;
            _error($"{provider} rejected the launch request.");
        }
        catch (Exception ex)
        {
            _error($"{provider} launch failed ({ex.GetType().Name}).");
        }

        return false;
    }

    private static bool Start(ProcessStartInfo info)
    {
        using Process? process = Process.Start(info);
        if (process is null)
            return false;

        if (info.FileName == "/usr/bin/open")
        {
            process.WaitForExit();
            return process.ExitCode == 0;
        }

        // A successful Process.Start does not prove the GUI session is ready.
        // Do not probe/timeout and start a duplicate Terminal.app instance.
        return true;
    }

    private static bool IsExecutable(string path)
    {
        if (!File.Exists(path))
            return false;

        UnixFileMode permissions = File.GetUnixFileMode(path);
        return (permissions & (UnixFileMode.UserExecute |
                               UnixFileMode.GroupExecute |
                               UnixFileMode.OtherExecute)) != 0;
    }

    private static string? ReadBundleIdentifier(string infoPlist)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Ignore,
            XmlResolver = null,
        };
        using XmlReader reader = XmlReader.Create(infoPlist, settings);
        XElement? dictionary = XDocument.Load(reader).Root?.Element("dict");
        if (dictionary is null)
            return null;

        XElement[] entries = dictionary.Elements().ToArray();
        for (int i = 0; i + 1 < entries.Length; i++)
        {
            if (entries[i].Name.LocalName == "key" &&
                entries[i].Value == "CFBundleIdentifier" &&
                entries[i + 1].Name.LocalName == "string")
                return entries[i + 1].Value;
        }

        return null;
    }

    private static string GetHomeDirectory()
    {
        string? home = Environment.GetEnvironmentVariable("HOME");
        if (!string.IsNullOrWhiteSpace(home) && Path.IsPathFullyQualified(home))
            return home;

        return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }
}

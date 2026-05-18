using CSharpFar.Core.Models;
using CSharpFar.Module.Abstractions;
using FarNet;

namespace CSharpFar.FarNetHost;

internal sealed class FarNetPanelAdapter : IModulePanel, IFarNetPanelOperations
{
    private readonly Panel _panel;
    private readonly Dictionary<string, FarFile> _filesByPath = new(StringComparer.OrdinalIgnoreCase);

    public FarNetPanelAdapter(Panel panel)
    {
        _panel = panel;
        SourceId = PanelSourceId.Module(FarNetModuleIds.ModuleHostId, "farnet-" + Guid.NewGuid().ToString("N"));
    }

    public PanelSourceId SourceId { get; }

    public string DisplayName =>
        string.IsNullOrWhiteSpace(_panel.Title) ? _panel.GetType().Name : _panel.Title;

    public PanelProviderCapabilities Capabilities
    {
        get
        {
            var capabilities = PanelProviderCapabilities.Enumerate | PanelProviderCapabilities.Refresh;
            if (_panel.Explorer.CanGetContent &&
                (_panel.Explorer.CanSetText || _panel.Explorer.CanSetFile))
            {
                capabilities |= PanelProviderCapabilities.Edit;
            }

            if (_panel.Explorer.CanCreateFile)
                capabilities |= PanelProviderCapabilities.CreateDirectory;
            if (_panel.Explorer.CanDeleteFiles)
                capabilities |= PanelProviderCapabilities.Delete;
            return capabilities;
        }
    }

    public ModulePanelInfo GetOpenPanelInfo() =>
        new()
        {
            Format = "FarNet",
            Title = DisplayName,
            CurrentDirectory = NormalizePath(_panel.CurrentDirectory),
        };

    public string NormalizePath(string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || sourcePath == ".")
            return "/";

        string path = sourcePath.Replace('\\', '/');
        if (!path.StartsWith("/", StringComparison.Ordinal))
            path = "/" + path;

        while (path.Length > 1 && path.EndsWith("/", StringComparison.Ordinal))
            path = path[..^1];

        return path;
    }

    public bool IsRootPath(string sourcePath) =>
        NormalizePath(sourcePath) == "/";

    public string? GetParentPath(string sourcePath)
    {
        string path = NormalizePath(sourcePath);
        if (path == "/")
            return null;

        int slash = path.LastIndexOf('/');
        return slash <= 0 ? "/" : path[..slash];
    }

    public IReadOnlyList<FilePanelItem> EnumerateDirectory(
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsRootPath(sourcePath))
            return [];

        var args = new GetFilesEventArgs(ExplorerModes.None, _panel, 0, 0, _panel.NeedsNewFiles);
        var files = _panel.Explorer.GetFiles(args).ToArray();
        _panel.NeedsNewFiles = false;
        _panel.Files.Clear();
        _filesByPath.Clear();

        var items = new List<FilePanelItem>(files.Length);
        foreach (FarFile file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _panel.Files.Add(file);
            string itemPath = NormalizePath(file.Name);
            _filesByPath[itemPath] = file;
            items.Add(ToFilePanelItem(file, itemPath));
        }

        return items;
    }

    public FilePanelItem? GetItem(
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string path = NormalizePath(sourcePath);
        if (path == "/")
            return null;

        if (!_filesByPath.TryGetValue(path, out FarFile? file))
            EnumerateDirectory("/", cancellationToken);

        return _filesByPath.TryGetValue(path, out file)
            ? ToFilePanelItem(file, path)
            : null;
    }

    public ModuleActionResult OpenItem(string sourcePath)
    {
        if (!TryGetFarFile(sourcePath, out FarFile? file) || file is null)
            return ModuleActionResult.Completed();

        if (!file.IsDirectory && !_panel.Explorer.CanOpenFile)
            return ModuleActionResult.Completed();

        try
        {
            var explorer = _panel.UIOpenFile(new OpenFileEventArgs(file));
            if (explorer is null)
                return ModuleActionResult.Completed();

            var panel = explorer.CreatePanel();
            panel.OpenChild(_panel);

            var openedPanel = Far.Api is IFarNetPanelHost host
                ? host.ConsumePendingPanel() ?? panel
                : panel;
            return ModuleActionResult.OpenedPanel(new FarNetPanelAdapter(openedPanel));
        }
        catch (FarNetUnsupportedApiException)
        {
            return ModuleActionResult.Completed();
        }
        catch (Exception ex)
        {
            return ModuleActionResult.Failed(ex.Message);
        }
    }

    public bool TryGetEditableText(string sourcePath, out string text, out string? error)
    {
        text = string.Empty;
        error = null;

        if (!TryGetFarFile(sourcePath, out FarFile? file) || file is null)
        {
            error = "FarNet panel item was not found.";
            return false;
        }

        if (!_panel.Explorer.CanGetContent)
        {
            error = "FarNet panel item editing is not supported by this explorer.";
            return false;
        }

        string tempPath = Path.Combine(Path.GetTempPath(), "CSharpFar", "FarNet", Guid.NewGuid().ToString("N"));
        try
        {
            var args = new GetContentEventArgs(ExplorerModes.None, file, tempPath);
            _panel.UIGetContent(args);

            if (args.UseText is not null)
            {
                text = args.UseText.ToString() ?? string.Empty;
                return true;
            }

            string? contentPath = !string.IsNullOrWhiteSpace(args.UseFileName)
                ? args.UseFileName
                : File.Exists(tempPath)
                    ? tempPath
                    : null;
            if (contentPath is null)
            {
                error = "FarNet panel did not provide editable content.";
                return false;
            }

            text = File.ReadAllText(contentPath);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
            catch
            {
            }
        }
    }

    public ModuleActionResult SetEditedText(string sourcePath, string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (!TryGetFarFile(sourcePath, out FarFile? file) || file is null)
            return ModuleActionResult.Failed("FarNet panel item was not found.");

        try
        {
            if (_panel.Explorer.CanSetText)
            {
                _panel.UISetText(new SetTextEventArgs(ExplorerModes.None, file, text));
            }
            else if (_panel.Explorer.CanSetFile)
            {
                string tempPath = Path.Combine(Path.GetTempPath(), "CSharpFar", "FarNet", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Path.GetDirectoryName(tempPath)!);
                try
                {
                    File.WriteAllText(tempPath, text);
                    _panel.UISetFile(new SetFileEventArgs(ExplorerModes.None, file, tempPath));
                }
                finally
                {
                    try { if (File.Exists(tempPath)) File.Delete(tempPath); }
                    catch { }
                }
            }
            else
            {
                return ModuleActionResult.Failed("FarNet panel item editing is not supported by this explorer.");
            }

            _panel.NeedsNewFiles = true;
            return ModuleActionResult.Completed();
        }
        catch (Exception ex)
        {
            return ModuleActionResult.Failed(ex.Message);
        }
    }

    public Task<Stream> OpenReadAsync(
        string sourcePath,
        CancellationToken cancellationToken = default) =>
        Task.FromException<Stream>(new NotSupportedException("FarNet panel file reading is not supported."));

    public Task<Stream> OpenWriteAsync(
        string sourcePath,
        bool overwrite,
        CancellationToken cancellationToken = default) =>
        Task.FromException<Stream>(new NotSupportedException("FarNet panel file writing is not supported."));

    public Task CreateDirectoryAsync(
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_panel.Explorer.CanCreateFile)
            throw new NotSupportedException("FarNet panel create is not supported by this explorer.");

        _panel.Explorer.CreateFile(new CreateFileEventArgs(ExplorerModes.None)
        {
            Data = Path.GetFileName(NormalizePath(sourcePath)),
            PostName = Path.GetFileName(NormalizePath(sourcePath)),
        });
        _panel.NeedsNewFiles = true;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(
        string sourcePath,
        bool recursive,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_panel.Explorer.CanDeleteFiles)
            throw new NotSupportedException("FarNet panel delete is not supported by this explorer.");

        var item = GetItem(sourcePath, cancellationToken);
        if (item is null || !_filesByPath.TryGetValue(NormalizePath(sourcePath), out FarFile? file))
            throw new FileNotFoundException("FarNet panel item was not found.", sourcePath);

        _panel.Explorer.DeleteFiles(new DeleteFilesEventArgs(ExplorerModes.None, [file], recursive));
        _panel.NeedsNewFiles = true;
        return Task.CompletedTask;
    }

    public Task RenameAsync(
        string sourcePath,
        string newSourcePath,
        CancellationToken cancellationToken = default) =>
        Task.FromException(new NotSupportedException("FarNet panel rename is not supported."));

    public void Dispose()
    {
    }

    private FilePanelItem ToFilePanelItem(FarFile file, string itemPath) =>
        new()
        {
            Name = file.Name,
            FullPath = itemPath,
            SourceId = SourceId,
            IsDirectory = file.IsDirectory,
            Size = file.IsDirectory ? null : file.Length,
            LastWriteTime = file.LastWriteTime == default ? DateTime.MinValue : file.LastWriteTime,
            Attributes = file.Attributes,
        };

    private bool TryGetFarFile(string sourcePath, out FarFile? file)
    {
        string path = NormalizePath(sourcePath);
        if (!_filesByPath.TryGetValue(path, out file))
            EnumerateDirectory("/");

        return _filesByPath.TryGetValue(path, out file);
    }
}

using System.Collections;
using System.Collections.ObjectModel;

namespace FarNet;

[Flags]
public enum MessageOptions
{
    None = 0,
    Warning = 0x1,
    Error = 0x2,
    KeepBackground = 0x4,
    AlignCenter = 0x8,
    Ok = 0x10000,
    OkCancel = 0x20000,
    AbortRetryIgnore = 0x30000,
    YesNo = 0x40000,
    YesNoCancel = 0x50000,
    RetryCancel = 0x60000,
    Gui = 0x00000040,
    GuiOnMacro = 0x00000080,
    Draw = 0x00000100,
}

public sealed class MessageArgs
{
    public string? Text { get; set; }
    public string? Caption { get; set; }
    public MessageOptions Options { get; set; }
    public string[]? Buttons { get; set; }
    public string? HelpTopic { get; set; }
}

public enum SpecialFolder
{
    LocalData = 0,
    RoamingData = 1,
}

[Flags]
public enum HelpOptions
{
    None = 0,
    Path = 1,
}

public enum OpenMode
{
    None = 0,
}

public enum Switching
{
    Disabled = 0,
    Enabled = 1,
}

public enum DeleteSource
{
    None = 0,
    File = 1,
}

public enum WindowKind
{
    None,
    Panels,
    Editor,
    Viewer,
    Dialog,
    Desktop,
}

public enum PanelSortMode
{
    Default = 0,
    Unsorted = 1,
    Name = 2,
    Extension = 3,
    LastWriteTime = 4,
    CreationTime = 5,
    LastAccessTime = 6,
    Length = 7,
    Description = 8,
    Owner = 9,
    CompressedSize = 10,
    LinkCount = 11,
    StreamCount = 12,
    StreamSize = 13,
    FullName = 14,
    ChangeTime = 15,
    ChangeTimeReversed = -15,
    FullNameReversed = -14,
    StreamSizeReversed = -13,
    StreamCountReversed = -12,
    LinkCountReversed = -11,
    CompressedSizeReversed = -10,
    OwnerReversed = -9,
    DescriptionReversed = -8,
    LengthReversed = -7,
    LastAccessTimeReversed = -6,
    CreationTimeReversed = -5,
    LastWriteTimeReversed = -4,
    ExtensionReversed = -3,
    NameReversed = -2,
    UnsortedReversed = -1,
}

public enum PanelViewMode
{
    AlternativeFull = 0,
    Brief = 1,
    Medium = 2,
    Full = 3,
    Wide = 4,
    Detailed = 5,
    Descriptions = 6,
    LongDescriptions = 7,
    FileOwners = 8,
    FileLinks = 9,
    Undefined = -48,
}

public class FarColumn
{
    public virtual string? Name { get; set; }
    public virtual string? Kind { get; set; }
    public virtual int Width { get; set; }
    public virtual ReadOnlyCollection<string> DefaultColumnKinds { get; } = new(
    [
        "N",
        "S",
        "P",
        "D",
        "T",
        "DM",
        "DC",
        "DA",
        "Z",
        "O",
        "LN",
        "F",
    ]);
}

public sealed class SetColumn : FarColumn
{
    public override string? Name { get; set; }
    public override string? Kind { get; set; }
    public override int Width { get; set; }
}

public sealed class PanelPlan
{
    public FarColumn[] Columns { get; set; } = [];
    public FarColumn[] StatusColumns { get; set; } = [];
    public bool IsFullScreen { get; set; }
    public bool IsDetailedStatus { get; set; }
    public bool IsAlignedExtensions { get; set; }
    public bool IsCaseConversion { get; set; }

    public PanelPlan Clone() =>
        new()
        {
            Columns = [.. Columns],
            StatusColumns = [.. StatusColumns],
            IsFullScreen = IsFullScreen,
            IsDetailedStatus = IsDetailedStatus,
            IsAlignedExtensions = IsAlignedExtensions,
            IsCaseConversion = IsCaseConversion,
        };
}

[Flags]
public enum ExplorerFunctions
{
    None = 0,
    ExploreLocation = 1,
    AcceptFiles = 2,
    ImportFiles = 4,
    ExportFiles = 8,
    DeleteFiles = 16,
    CreateFile = 32,
    GetContent = 64,
    SetFile = 128,
    SetText = 256,
    OpenFile = 512,
    CloneFile = 1024,
    RenameFile = 2048,
}

[Flags]
public enum ExplorerModes
{
    None = 0,
    Find = 1 << 0,
    Silent = 1 << 1,
}

public enum JobResult
{
    Done,
    Ignore,
    Incomplete,
}

public abstract class FarFile
{
    public virtual FileAttributes Attributes { get; set; }
    public virtual string? Description { get; set; }
    public virtual object? Data { get; set; }
    public virtual string FullName { get; set; } = string.Empty;
    public virtual string Name { get; set; } = string.Empty;
    public virtual string? Owner { get; set; }
    public virtual string[]? Columns { get; set; }
    public virtual DateTime CreationTime { get; set; }
    public virtual DateTime LastAccessTime { get; set; }
    public virtual DateTime LastWriteTime { get; set; }
    public virtual long Length { get; set; }
    public bool IsArchive => (Attributes & FileAttributes.Archive) != 0;
    public bool IsDirectory => (Attributes & FileAttributes.Directory) != 0;
    public bool IsHidden => (Attributes & FileAttributes.Hidden) != 0;
    public bool IsReadOnly => (Attributes & FileAttributes.ReadOnly) != 0;
    public bool IsReparsePoint => (Attributes & FileAttributes.ReparsePoint) != 0;
    public bool IsSystem => (Attributes & FileAttributes.System) != 0;
    public bool IsTemporary => (Attributes & FileAttributes.Temporary) != 0;
}

public sealed class SetFile : FarFile
{
}

public abstract class Explorer
{
    public Explorer(Guid typeId)
    {
        TypeId = typeId;
    }

    public Guid TypeId { get; }
    public virtual ExplorerFunctions Functions { get; protected set; }
    public virtual bool CanExploreLocation
    {
        get => (Functions & ExplorerFunctions.ExploreLocation) != 0;
        set => SetFunction(ExplorerFunctions.ExploreLocation, value);
    }

    public virtual bool CanGetContent
    {
        get => (Functions & ExplorerFunctions.GetContent) != 0;
        set => SetFunction(ExplorerFunctions.GetContent, value);
    }

    public virtual bool CanSetFile
    {
        get => (Functions & ExplorerFunctions.SetFile) != 0;
        set => SetFunction(ExplorerFunctions.SetFile, value);
    }

    public virtual bool CanSetText
    {
        get => (Functions & ExplorerFunctions.SetText) != 0;
        set => SetFunction(ExplorerFunctions.SetText, value);
    }

    public virtual bool CanAcceptFiles
    {
        get => (Functions & ExplorerFunctions.AcceptFiles) != 0;
        set => SetFunction(ExplorerFunctions.AcceptFiles, value);
    }

    public virtual bool CanCreateFile
    {
        get => (Functions & ExplorerFunctions.CreateFile) != 0;
        set => SetFunction(ExplorerFunctions.CreateFile, value);
    }

    public virtual bool CanDeleteFiles
    {
        get => (Functions & ExplorerFunctions.DeleteFiles) != 0;
        set => SetFunction(ExplorerFunctions.DeleteFiles, value);
    }

    public virtual bool CanExportFiles
    {
        get => (Functions & ExplorerFunctions.ExportFiles) != 0;
        set => SetFunction(ExplorerFunctions.ExportFiles, value);
    }

    public virtual bool CanImportFiles
    {
        get => (Functions & ExplorerFunctions.ImportFiles) != 0;
        set => SetFunction(ExplorerFunctions.ImportFiles, value);
    }

    public virtual bool CanOpenFile
    {
        get => (Functions & ExplorerFunctions.OpenFile) != 0;
        set => SetFunction(ExplorerFunctions.OpenFile, value);
    }

    public virtual bool CanCloneFile
    {
        get => (Functions & ExplorerFunctions.CloneFile) != 0;
        set => SetFunction(ExplorerFunctions.CloneFile, value);
    }

    public virtual bool CanRenameFile
    {
        get => (Functions & ExplorerFunctions.RenameFile) != 0;
        set => SetFunction(ExplorerFunctions.RenameFile, value);
    }

    public virtual string Location { get; set; } = string.Empty;
    public virtual IEqualityComparer<FarFile> FileComparer { get; set; } = EqualityComparer<FarFile>.Default;

    public abstract IEnumerable<FarFile> GetFiles(GetFilesEventArgs args);
    public virtual void GetContent(GetContentEventArgs args) => throw new FarNetUnsupportedApiException(nameof(GetContent));
    public virtual void SetFile(SetFileEventArgs args) => throw new FarNetUnsupportedApiException(nameof(SetFile));
    public virtual void SetText(SetTextEventArgs args) => throw new FarNetUnsupportedApiException(nameof(SetText));
    public virtual Explorer? OpenFile(OpenFileEventArgs args) => throw new FarNetUnsupportedApiException(nameof(OpenFile));
    public virtual void CreateFile(CreateFileEventArgs args) => throw new FarNetUnsupportedApiException(nameof(CreateFile));
    public virtual void DeleteFiles(DeleteFilesEventArgs args) => throw new FarNetUnsupportedApiException(nameof(DeleteFiles));
    public virtual Panel CreatePanel() => new(this);
    public virtual void EnterPanel(Panel panel)
    {
    }

    protected void SetFunction(ExplorerFunctions function, bool enabled)
    {
        Functions = enabled ? Functions | function : Functions & ~function;
    }
}

public class Panel : IPanel
{
    public Panel(Explorer explorer)
    {
        Explorer = explorer;
    }

    public Explorer Explorer { get; }
    public string Title { get; set; } = string.Empty;
    public PanelSortMode SortMode { get; set; }
    public PanelViewMode ViewMode { get; set; }
    public string CurrentDirectory { get; set; } = string.Empty;
    public string CurrentLocation { get; set; } = string.Empty;
    public Panel? Child { get; private set; }
    public Panel? Parent { get; private set; }
    public Guid TypeId { get; set; }
    public bool NeedsNewFiles { get; set; } = true;
    public bool IsOpened { get; private set; }
    public IList<FarFile> Files { get; } = [];
    public Hashtable Data { get; } = [];
    public PanelPlan ViewPlan { get; } = new();
    private readonly Dictionary<PanelViewMode, PanelPlan> _plans = [];

    public virtual void Open()
    {
        if (Far.Api is IFarNetPanelHost host)
        {
            IsOpened = true;
            host.OpenPanel(this);
            return;
        }

        throw new FarNetUnsupportedApiException("Panel.Open");
    }

    public void OpenChild(Panel parent)
    {
        ArgumentNullException.ThrowIfNull(parent);

        Parent = parent;
        parent.Child = this;
        Open();
    }

    public PanelPlan GetPlan(PanelViewMode mode)
    {
        if (!_plans.TryGetValue(mode, out var plan))
        {
            plan = new PanelPlan();
            _plans[mode] = plan;
        }

        return plan;
    }

    public void SetPlan(PanelViewMode mode, PanelPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        _plans[mode] = plan;
    }

    public virtual void UICreateFile(CreateFileEventArgs args) => Explorer.CreateFile(args);
    public virtual void UIDeleteFiles(DeleteFilesEventArgs args) => Explorer.DeleteFiles(args);
    public virtual void UIGetContent(GetContentEventArgs args) => Explorer.GetContent(args);
    public virtual void UISetFile(SetFileEventArgs args) => Explorer.SetFile(args);
    public virtual void UISetText(SetTextEventArgs args) => Explorer.SetText(args);
    public virtual Explorer? UIOpenFile(OpenFileEventArgs args) => Explorer.OpenFile(args);
    public virtual void UIOpenFile(FarFile file)
    {
        var explorer = UIOpenFile(new OpenFileEventArgs(file));
        if (explorer is null)
            return;

        var panel = explorer.CreatePanel();
        panel.OpenChild(this);
    }

    public virtual void UIEditFile(FarFile file) => throw new FarNetUnsupportedApiException(nameof(UIEditFile));
}

public abstract class ExplorerEventArgs : EventArgs
{
    protected ExplorerEventArgs()
        : this(ExplorerModes.None)
    {
    }

    protected ExplorerEventArgs(ExplorerModes mode)
    {
        Mode = mode;
    }

    public ExplorerModes Mode { get; }
    public object? Parameter { get; set; }
    public JobResult Result { get; set; }
    public object? Data { get; set; }
    public object? PostData { get; set; }
    public FarFile? PostFile { get; set; }
    public string? PostName { get; set; }
    public bool UI { get; }
}

public class GetFilesEventArgs : ExplorerEventArgs
{
    public GetFilesEventArgs()
    {
    }

    public GetFilesEventArgs(ExplorerModes mode)
        : base(mode)
    {
    }

    public GetFilesEventArgs(ExplorerModes mode, Panel? panel, int offset, int limit, bool newFiles)
        : base(mode)
    {
        Panel = panel;
        Offset = offset;
        Limit = limit;
        NewFiles = newFiles;
    }

    public Panel? Panel { get; }
    public int Offset { get; }
    public int Limit { get; }
    public bool NewFiles { get; }
}

public class CreateFileEventArgs : ExplorerEventArgs
{
    public CreateFileEventArgs()
    {
    }

    public CreateFileEventArgs(ExplorerModes mode)
        : base(mode)
    {
    }
}

public class ExplorerFileEventArgs : ExplorerEventArgs
{
    protected ExplorerFileEventArgs(ExplorerModes mode, FarFile file)
        : base(mode)
    {
        File = file;
    }

    public FarFile File { get; }
}

public sealed class OpenFileEventArgs : ExplorerFileEventArgs
{
    public OpenFileEventArgs(FarFile file)
        : base(ExplorerModes.None, file)
    {
    }
}

public sealed class GetContentEventArgs : ExplorerFileEventArgs
{
    public GetContentEventArgs(ExplorerModes mode, FarFile file, string fileName)
        : base(mode, file)
    {
        FileName = fileName;
    }

    public string FileName { get; }
    public bool CanSet { get; set; }
    public object? UseText { get; set; }
    public string? UseFileName { get; set; }
    public string? UseFileExtension { get; set; }
    public int CodePage { get; set; }
    public EventHandler? EditorOpened { get; set; }
}

public sealed class SetFileEventArgs : ExplorerFileEventArgs
{
    public SetFileEventArgs(ExplorerModes mode, FarFile file, string fileName)
        : base(mode, file)
    {
        FileName = fileName;
    }

    public string FileName { get; }
}

public sealed class SetTextEventArgs : ExplorerFileEventArgs
{
    public SetTextEventArgs(ExplorerModes mode, FarFile file, string text)
        : base(mode, file)
    {
        Text = text;
    }

    public string Text { get; }
}

public class ExplorerFilesEventArgs : ExplorerEventArgs
{
    protected ExplorerFilesEventArgs()
    {
    }

    protected ExplorerFilesEventArgs(ExplorerModes mode, IList<FarFile> files)
        : base(mode)
    {
        Files = files;
    }

    public IList<FarFile> Files { get; init; } = [];
}

public class DeleteFilesEventArgs : ExplorerFilesEventArgs
{
    public DeleteFilesEventArgs()
    {
    }

    public DeleteFilesEventArgs(ExplorerModes mode, IList<FarFile> files, bool force)
        : base(mode, files)
    {
        Force = force;
    }

    public bool Force { get; }
}

public interface IFarNetPanelHost
{
    void OpenPanel(Panel panel);
    Panel? ConsumePendingPanel();
}

public interface IMenu
{
}

public interface IListMenu
{
}

public interface IInputBox
{
}

public interface IEditor
{
}

public interface IViewer
{
}

public interface IDialog
{
}

public interface IPanel
{
}

public interface IUserInterface
{
    void Write(string text);
}

public interface ILine
{
}

public interface IWindow
{
    WindowKind Kind { get; }
    bool IsModal { get; }
}

public interface IHistory
{
}

public interface IAnyEditor
{
}

public interface IAnyViewer
{
}

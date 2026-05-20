using System.Reflection;
using System.Runtime.Loader;
using FarNet;

namespace CSharpFar.Tests;

[Collection(FarNetTestCollection.Name)]
public sealed class FarNetCompatibilitySurfaceTests
{
    [Fact]
    public void PublicFarNetTypes_HaveOnlyApprovedCompatibilityGaps()
    {
        var officialContext = new AssemblyLoadContext("official-farnet-types", isCollectible: true);
        var shimContext = new AssemblyLoadContext("csharpfar-farnet-types", isCollectible: true);
        try
        {
            var official = officialContext.LoadFromAssemblyPath(GetOfficialFarNetAssemblyPath());
            var shim = shimContext.LoadFromAssemblyPath(GetShimFarNetAssemblyPath());

            string[] missingTypes = GetFarNetExportedTypeNames(official)
                .Except(GetFarNetExportedTypeNames(shim), StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();

            Assert.Equal(ApprovedMissingTypes(), missingTypes);
        }
        finally
        {
            officialContext.Unload();
            shimContext.Unload();
        }
    }

    [Fact]
    public void CoreFarNetMembers_HaveOnlyApprovedCompatibilityGaps()
    {
        var officialContext = new AssemblyLoadContext("official-farnet-members", isCollectible: true);
        var shimContext = new AssemblyLoadContext("csharpfar-farnet-members", isCollectible: true);
        try
        {
            var official = officialContext.LoadFromAssemblyPath(GetOfficialFarNetAssemblyPath());
            var shim = shimContext.LoadFromAssemblyPath(GetShimFarNetAssemblyPath());

            string[] comparedTypes =
            [
                "FarNet.IFar",
                "FarNet.Panel",
                "FarNet.Explorer",
                "FarNet.FarFile",
                "FarNet.IEditor",
                "FarNet.IViewer",
                "FarNet.IWindow",
                "FarNet.IHistory",
            ];

            string[] missingMembers = comparedTypes
                .SelectMany(typeName => GetMissingMemberSignatures(official, shim, typeName))
                .Order(StringComparer.Ordinal)
                .ToArray();

            Assert.Equal(ApprovedMissingMembers(), missingMembers);
        }
        finally
        {
            officialContext.Unload();
            shimContext.Unload();
        }
    }

    private static IEnumerable<string> GetFarNetExportedTypeNames(Assembly assembly) =>
        assembly.GetExportedTypes()
            .Where(type => string.Equals(type.Namespace, "FarNet", StringComparison.Ordinal))
            .Select(type => type.Name)
            .Order(StringComparer.Ordinal);

    private static IEnumerable<string> GetMissingMemberSignatures(
        Assembly official,
        Assembly shim,
        string typeName)
    {
        var officialType = official.GetType(typeName) ??
            throw new InvalidOperationException("Official FarNet type was not found: " + typeName);
        var shimType = shim.GetType(typeName) ??
            throw new InvalidOperationException("CSharpFar FarNet type was not found: " + typeName);

        string[] officialMembers = GetDeclaredMemberSignatures(officialType).ToArray();
        var shimMembers = GetDeclaredMemberSignatures(shimType).ToHashSet(StringComparer.Ordinal);

        return officialMembers
            .Where(member => !shimMembers.Contains(member))
            .Select(member => typeName + "|" + member);
    }

    private static IEnumerable<string> GetDeclaredMemberSignatures(Type type) =>
        type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Select(GetMemberSignature)
            .OfType<string>()
            .Order(StringComparer.Ordinal);

    private static string? GetMemberSignature(MemberInfo member) =>
        member switch
        {
            MethodInfo { IsSpecialName: false } method => "M:" + method.Name + "(" +
                string.Join(",", method.GetParameters().Select(parameter => parameter.ParameterType.Name)) + ")",
            PropertyInfo property => "P:" + property.Name,
            EventInfo eventInfo => "E:" + eventInfo.Name,
            _ => null,
        };

    private static string GetOfficialFarNetAssemblyPath()
    {
        string path = Path.Combine(
            GetRepoRoot(),
            "tests",
            "CSharpFar.Tests.Fixtures.OfficialFarNetModule",
            "bin",
            GetBuildConfiguration(),
            "net10.0",
            "FarNet.dll");

        if (!File.Exists(path))
            throw new FileNotFoundException("Official FarNet fixture assembly was not found.", path);

        return path;
    }

    private static string GetShimFarNetAssemblyPath() => typeof(IFar).Assembly.Location;

    private static string GetBuildConfiguration()
    {
        var baseDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        return baseDirectory.Parent?.Name is { Length: > 0 } configuration
            ? configuration
            : "Debug";
    }

    private static string GetRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CSharpFar.slnx")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Cannot find repository root.");
    }

    private static string[] ApprovedMissingTypes() =>
        SplitApprovedList(
            """
            AcceptFilesEventArgs
            CloneFileEventArgs
            CommandLineEventArgs
            CopyFilesEventArgs
            DataItem
            DirectoryChangedEventArgs
            Disposable
            DisposableEventHandler`1
            EditTextArgs
            EditorChangedEventArgs
            EditorChangeKind
            EditorColor
            EditorColorInfo
            EditorSavingEventArgs
            ExpandTabsMode
            ExploreDirectoryEventArgs
            ExploreEventArgs
            ExploreLocationEventArgs
            ExploreParentEventArgs
            ExploreRootEventArgs
            ExplorerEnteredEventArgs
            ExportFilesEventArgs
            FSContext
            FSContextPanel
            FSContextSingle
            FarSetting
            FileDataComparer
            FileFileComparer
            FileNameComparer
            GetHistoryArgs
            HistoryInfo
            HistoryKind
            IEditorBase
            IEditorBookmark
            IFace
            IViewerBase
            ImportFilesEventArgs
            KeyBar
            KeyCode
            Log
            MacroArea
            MacroState
            ModuleEditorEventArgs
            ModuleSettings`1
            ModuleSettingsArgs
            ModuleSettingsBase
            MouseAction
            MouseButtons
            MouseEventArgs
            MouseInfo
            PaletteColor
            PanelDotsMode
            PanelEventArgs
            PanelHighlighting
            PanelKind
            Place
            PlaceKind
            Point
            QuittingEventArgs
            ReadKeyOptions
            RenameFileEventArgs
            Span
            TaskbarProgressBarState
            Tasks
            TextFrame
            User
            Validators
            ViewChangedEventArgs
            ViewFrame
            ViewFrameOptions
            ViewerViewMode
            WrapFile
            XmlCData
            """);

    private static string[] ApprovedMissingMembers() =>
        SplitApprovedList(
            """
            FarNet.Explorer|M:AcceptFiles(AcceptFilesEventArgs)
            FarNet.Explorer|M:CloneFile(CloneFileEventArgs)
            FarNet.Explorer|M:ExploreDirectory(ExploreDirectoryEventArgs)
            FarNet.Explorer|M:ExploreLocation(ExploreLocationEventArgs)
            FarNet.Explorer|M:ExploreParent(ExploreParentEventArgs)
            FarNet.Explorer|M:ExploreRoot(ExploreRootEventArgs)
            FarNet.Explorer|M:ExportFiles(ExportFilesEventArgs)
            FarNet.Explorer|M:ImportFiles(ImportFilesEventArgs)
            FarNet.Explorer|M:RenameFile(RenameFileEventArgs)
            FarNet.FarFile|M:ToString()
            FarNet.FarFile|P:IsCompressed
            FarNet.FarFile|P:IsEncrypted
            FarNet.IFar|E:DirectoryChanged
            FarNet.IFar|M:Editors()
            FarNet.IFar|M:GetFolderPath(SpecialFolder)
            FarNet.IFar|M:GetModuleInterop(String,String,Object)
            FarNet.IFar|M:GetSetting(FarSetting,String)
            FarNet.IFar|M:InvokeCommand(String)
            FarNet.IFar|M:IsMaskMatch(String,String)
            FarNet.IFar|M:IsMaskMatch(String,String,Boolean)
            FarNet.IFar|M:IsMaskValid(String)
            FarNet.IFar|M:KeyInfoToName(KeyInfo)
            FarNet.IFar|M:LoadModule(String)
            FarNet.IFar|M:NameToKeyInfo(String)
            FarNet.IFar|M:Panels(Guid)
            FarNet.IFar|M:Panels(Type)
            FarNet.IFar|M:PostJob(Action)
            FarNet.IFar|M:PostJobAsync(Action)
            FarNet.IFar|M:PostMacro(String)
            FarNet.IFar|M:PostMacro(String,Boolean,Boolean)
            FarNet.IFar|M:PostStep(Action)
            FarNet.IFar|M:Quit()
            FarNet.IFar|M:Viewers()
            FarNet.IFar|P:Dialog
            FarNet.IFar|P:Editor
            FarNet.IFar|P:FS
            FarNet.IFar|P:HasPanels
            FarNet.IFar|P:Line
            FarNet.IFar|P:MacroArea
            FarNet.IFar|P:MacroState
            FarNet.IFar|P:Viewer
            FarNet.IEditor|M:Activate()
            FarNet.IEditor|M:Add(String)
            FarNet.IEditor|M:AddDrawer(IModuleDrawer)
            FarNet.IEditor|M:BeginAsync()
            FarNet.IEditor|M:BeginUndo()
            FarNet.IEditor|M:Clear()
            FarNet.IEditor|M:Close()
            FarNet.IEditor|M:ConvertColumnEditorToScreen(Int32,Int32)
            FarNet.IEditor|M:ConvertColumnScreenToEditor(Int32,Int32)
            FarNet.IEditor|M:ConvertPointEditorToScreen(Point)
            FarNet.IEditor|M:ConvertPointScreenToEditor(Point)
            FarNet.IEditor|M:DeleteChar()
            FarNet.IEditor|M:DeleteLine()
            FarNet.IEditor|M:DeleteText()
            FarNet.IEditor|M:EndAsync()
            FarNet.IEditor|M:EndUndo()
            FarNet.IEditor|M:GetColors(Int32,List`1)
            FarNet.IEditor|M:GetLineText2(Int32)
            FarNet.IEditor|M:GetSelectedText()
            FarNet.IEditor|M:GetSelectedText(String)
            FarNet.IEditor|M:GetText()
            FarNet.IEditor|M:GetText(String)
            FarNet.IEditor|M:GoTo(Int32,Int32)
            FarNet.IEditor|M:GoToColumn(Int32)
            FarNet.IEditor|M:GoToEnd(Boolean)
            FarNet.IEditor|M:GoToLine(Int32)
            FarNet.IEditor|M:HasColorer()
            FarNet.IEditor|M:Insert(Int32,String)
            FarNet.IEditor|M:InsertChar(Char)
            FarNet.IEditor|M:InsertLine()
            FarNet.IEditor|M:InsertLine(Boolean)
            FarNet.IEditor|M:InsertText(String)
            FarNet.IEditor|M:Open()
            FarNet.IEditor|M:Open(OpenMode)
            FarNet.IEditor|M:OpenWriter()
            FarNet.IEditor|M:Redo()
            FarNet.IEditor|M:Redraw()
            FarNet.IEditor|M:RemoveAt(Int32)
            FarNet.IEditor|M:RemoveDrawer(Guid)
            FarNet.IEditor|M:Save()
            FarNet.IEditor|M:Save(Boolean)
            FarNet.IEditor|M:Save(String)
            FarNet.IEditor|M:SelectAllText()
            FarNet.IEditor|M:SelectText(Int32,Int32,Int32,Int32)
            FarNet.IEditor|M:SelectText(Int32,Int32,Int32,Int32,PlaceKind)
            FarNet.IEditor|M:SetLineText2(Int32,ReadOnlySpan`1)
            FarNet.IEditor|M:SetSelectedText(String)
            FarNet.IEditor|M:SetText(String)
            FarNet.IEditor|M:Sync()
            FarNet.IEditor|M:Undo()
            FarNet.IEditor|M:UnselectText()
            FarNet.IEditor|M:WorksSetColors(Guid,Int32,IEnumerable`1)
            FarNet.IEditor|P:Bookmark
            FarNet.IEditor|P:Caret
            FarNet.IEditor|P:ChangeCount
            FarNet.IEditor|P:CodePage
            FarNet.IEditor|P:Count
            FarNet.IEditor|P:Data
            FarNet.IEditor|P:DeleteSource
            FarNet.IEditor|P:DisableHistory
            FarNet.IEditor|P:ExpandTabs
            FarNet.IEditor|P:FileName
            FarNet.IEditor|P:Frame
            FarNet.IEditor|P:Host
            FarNet.IEditor|P:Id
            FarNet.IEditor|P:IsKeyBar
            FarNet.IEditor|P:IsLocked
            FarNet.IEditor|P:IsModified
            FarNet.IEditor|P:IsOpened
            FarNet.IEditor|P:IsTitleBar
            FarNet.IEditor|P:IsVirtualSpace
            FarNet.IEditor|P:Item
            FarNet.IEditor|P:Line
            FarNet.IEditor|P:Lines
            FarNet.IEditor|P:Overtype
            FarNet.IEditor|P:SelectedLines
            FarNet.IEditor|P:SelectionExists
            FarNet.IEditor|P:SelectionKind
            FarNet.IEditor|P:SelectionPlace
            FarNet.IEditor|P:SelectionPoint
            FarNet.IEditor|P:ShowWhiteSpace
            FarNet.IEditor|P:Strings
            FarNet.IEditor|P:Switching
            FarNet.IEditor|P:TabSize
            FarNet.IEditor|P:TimeOfGotFocus
            FarNet.IEditor|P:TimeOfOpen
            FarNet.IEditor|P:TimeOfSave
            FarNet.IEditor|P:Title
            FarNet.IEditor|P:Window
            FarNet.IEditor|P:WindowKind
            FarNet.IEditor|P:WindowSize
            FarNet.IEditor|P:WordDiv
            FarNet.IEditor|P:WriteByteOrderMark
            FarNet.IHistory|M:Command()
            FarNet.IHistory|M:Dialog(String)
            FarNet.IHistory|M:Editor()
            FarNet.IHistory|M:Folder()
            FarNet.IHistory|M:GetHistory(GetHistoryArgs)
            FarNet.IHistory|M:Viewer()
            FarNet.IViewer|M:Activate()
            FarNet.IViewer|M:Close()
            FarNet.IViewer|M:Open()
            FarNet.IViewer|M:Open(OpenMode)
            FarNet.IViewer|M:Redraw()
            FarNet.IViewer|M:SelectText(Int64,Int32)
            FarNet.IViewer|M:SetFrame(Int64,Int32,ViewFrameOptions)
            FarNet.IViewer|P:CodePage
            FarNet.IViewer|P:DeleteSource
            FarNet.IViewer|P:DisableHistory
            FarNet.IViewer|P:FileName
            FarNet.IViewer|P:FileSize
            FarNet.IViewer|P:Frame
            FarNet.IViewer|P:Id
            FarNet.IViewer|P:IsOpened
            FarNet.IViewer|P:Switching
            FarNet.IViewer|P:TimeOfGotFocus
            FarNet.IViewer|P:TimeOfOpen
            FarNet.IViewer|P:Title
            FarNet.IViewer|P:ViewMode
            FarNet.IViewer|P:Window
            FarNet.IViewer|P:WindowKind
            FarNet.IViewer|P:WindowSize
            FarNet.IViewer|P:WordWrapMode
            FarNet.IViewer|P:WrapMode
            FarNet.IWindow|M:CountVisiblePanels()
            FarNet.IWindow|M:GetAt(Int32)
            FarNet.IWindow|M:GetIdAt(Int32)
            FarNet.IWindow|M:GetKindAt(Int32)
            FarNet.IWindow|M:GetNameAt(Int32)
            FarNet.IWindow|M:SetCurrentAt(Int32)
            FarNet.IWindow|P:Count
            FarNet.Panel|E:Closed
            FarNet.Panel|E:Closing
            FarNet.Panel|E:CtrlBreak
            FarNet.Panel|E:Escaping
            FarNet.Panel|E:ExplorerEntered
            FarNet.Panel|E:GotFocus
            FarNet.Panel|E:InvokingCommand
            FarNet.Panel|E:LosingFocus
            FarNet.Panel|E:Redrawing
            FarNet.Panel|E:Timer
            FarNet.Panel|E:UpdateInfo
            FarNet.Panel|E:ViewChanged
            FarNet.Panel|M:Close()
            FarNet.Panel|M:Close(String)
            FarNet.Panel|M:CloseChild()
            FarNet.Panel|M:GetFiles()
            FarNet.Panel|M:GetSelectedFiles()
            FarNet.Panel|M:GoToName(String)
            FarNet.Panel|M:GoToName(String,Boolean)
            FarNet.Panel|M:GoToPath(String)
            FarNet.Panel|M:Navigate(Explorer)
            FarNet.Panel|M:OnThatFileChanged(Panel,EventArgs)
            FarNet.Panel|M:OnThisFileChanged(EventArgs)
            FarNet.Panel|M:PostData(Object)
            FarNet.Panel|M:PostFile(FarFile)
            FarNet.Panel|M:PostName(String)
            FarNet.Panel|M:Push()
            FarNet.Panel|M:SelectAll()
            FarNet.Panel|M:SelectAt(Int32[])
            FarNet.Panel|M:SelectFiles(IEnumerable,IEqualityComparer`1)
            FarNet.Panel|M:SelectNames(IEnumerable)
            FarNet.Panel|M:SelectedIndexes()
            FarNet.Panel|M:SetActive()
            FarNet.Panel|M:SetKeyBars(KeyBar[])
            FarNet.Panel|M:UIAcceptFiles(AcceptFilesEventArgs)
            FarNet.Panel|M:UIClone()
            FarNet.Panel|M:UICloneFile(CloneFileEventArgs)
            FarNet.Panel|M:UIClosed()
            FarNet.Panel|M:UIClosing(PanelEventArgs)
            FarNet.Panel|M:UICopyMove(Boolean)
            FarNet.Panel|M:UICreate()
            FarNet.Panel|M:UICtrlBreak()
            FarNet.Panel|M:UIDelete(Boolean)
            FarNet.Panel|M:UIEscape(Boolean)
            FarNet.Panel|M:UIExploreDirectory(ExploreDirectoryEventArgs)
            FarNet.Panel|M:UIExploreLocation(ExploreLocationEventArgs)
            FarNet.Panel|M:UIExploreParent(ExploreParentEventArgs)
            FarNet.Panel|M:UIExploreRoot(ExploreRootEventArgs)
            FarNet.Panel|M:UIExplorerEntered(ExplorerEnteredEventArgs)
            FarNet.Panel|M:UIExportFiles(ExportFilesEventArgs)
            FarNet.Panel|M:UIGetFiles(GetFilesEventArgs)
            FarNet.Panel|M:UIGotFocus()
            FarNet.Panel|M:UIImportFiles(ImportFilesEventArgs)
            FarNet.Panel|M:UILosingFocus()
            FarNet.Panel|M:UIRedrawing(PanelEventArgs)
            FarNet.Panel|M:UIRename()
            FarNet.Panel|M:UIRenameFile(RenameFileEventArgs)
            FarNet.Panel|M:UITimer()
            FarNet.Panel|M:UIUpdateInfo()
            FarNet.Panel|M:UIViewChanged(ViewChangedEventArgs)
            FarNet.Panel|M:UIViewFile(FarFile)
            FarNet.Panel|M:UnselectAll()
            FarNet.Panel|M:UnselectAt(Int32[])
            FarNet.Panel|M:UnselectNames(IEnumerable)
            FarNet.Panel|M:WorksEscaping(KeyEventArgs)
            FarNet.Panel|M:WorksExportExplorerFile(Explorer,Panel,ExplorerModes,FarFile,String)
            FarNet.Panel|M:WorksInvokingCommand(CommandLineEventArgs)
            FarNet.Panel|P:CompareFatTime
            FarNet.Panel|P:DirectoriesFirst
            FarNet.Panel|P:DotsDescription
            FarNet.Panel|P:DotsMode
            FarNet.Panel|P:Frame
            FarNet.Panel|P:Garbage
            FarNet.Panel|P:Highlight
            FarNet.Panel|P:Highlighting
            FarNet.Panel|P:Id
            FarNet.Panel|P:InfoItems
            FarNet.Panel|P:IsActive
            FarNet.Panel|P:IsLeft
            FarNet.Panel|P:IsNavigation
            FarNet.Panel|P:IsPlugin
            FarNet.Panel|P:IsPushed
            FarNet.Panel|P:IsTimerUpdate
            FarNet.Panel|P:IsVisible
            FarNet.Panel|P:Kind
            FarNet.Panel|P:NoFilter
            FarNet.Panel|P:PageLimit
            FarNet.Panel|P:PageOffset
            FarNet.Panel|P:PreserveCase
            FarNet.Panel|P:RawSelection
            FarNet.Panel|P:RealNames
            FarNet.Panel|P:RealNamesDeleteFiles
            FarNet.Panel|P:RealNamesExportFiles
            FarNet.Panel|P:RealNamesImportFiles
            FarNet.Panel|P:RealNamesMakeDirectory
            FarNet.Panel|P:RightAligned
            FarNet.Panel|P:SelectedFirst
            FarNet.Panel|P:SelectionExists
            FarNet.Panel|P:ShowHidden
            FarNet.Panel|P:ShowNamesOnly
            FarNet.Panel|P:StartDirectory
            FarNet.Panel|P:TargetPanel
            FarNet.Panel|P:TimerInterval
            FarNet.Panel|P:TopIndex
            FarNet.Panel|P:UseSortGroups
            FarNet.Panel|P:Window
            FarNet.Panel|P:WindowKind
            FarNet.Panel|P:WorksPanel
            """);

    private static string[] SplitApprovedList(string text) =>
        text.Split(
                ['\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Order(StringComparer.Ordinal)
            .ToArray();
}

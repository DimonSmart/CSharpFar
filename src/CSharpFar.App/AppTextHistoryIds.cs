using CSharpFar.Ui;

namespace CSharpFar.App;

internal static class AppTextHistoryIds
{
    public static readonly TextHistoryId SearchMask = new("SearchDialog.Mask");
    public static readonly TextHistoryId SearchText = new("SearchDialog.Text");
    public static readonly TextHistoryId SearchParallelism = new("SearchDialog.Parallelism");
    public static readonly TextHistoryId CreateFolderName = new("CreateFolderDialog.FolderName");
    public static readonly TextHistoryId ViewerFindPattern = new("Viewer.Find.Pattern");
    public static readonly TextHistoryId EditorFindPattern = new("Editor.Find.Pattern");
    public static readonly TextHistoryId CompareInclude = new("Compare.Include");
    public static readonly TextHistoryId CompareExclude = new("Compare.Exclude");
    public static readonly TextHistoryId CompareDepth = new("Compare.Depth");
    public static readonly TextHistoryId FileOperationDestination = new("FileOperationDialog.Destination");
    public static readonly TextHistoryId FileOperationFilter = new("FileOperationDialog.Filter");
    public static readonly TextHistoryId OpenCreateFilePath = new("OpenCreateFileDialog.FilePath");
}

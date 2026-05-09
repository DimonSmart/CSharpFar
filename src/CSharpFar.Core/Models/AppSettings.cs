using CSharpFar.Core.Highlighting;

namespace CSharpFar.Core.Models;

public sealed class AppSettings
{
    public UiSettings      Ui      { get; set; } = new();
    public ShellSettings   Shell   { get; set; } = new();
    public PanelsSettings  Panels  { get; set; } = new();
    public FileOperationSettings FileOperations { get; set; } = new();
    public HistorySettings History { get; set; } = new();

    public sealed class UiSettings
    {
        public string Theme         { get; set; } = "classic-blue";
        public bool   ConfirmDelete { get; set; } = true;
        public string Palette       { get; set; } = "Default";
    }

    public sealed class ShellSettings
    {
        public string Kind            { get; set; } = "cmd";
        public string Executable      { get; set; } = "cmd.exe";
        public string ArgumentsFormat { get; set; } = "/c {0}";
    }

    public sealed class PanelsSettings
    {
        public string? LeftStartDirectory  { get; set; }
        public string? RightStartDirectory { get; set; }
        public string  DefaultSortMode     { get; set; } = "name";
        public string  LeftViewMode        { get; set; } = "Full";
        public string  RightViewMode       { get; set; } = "Full";
        public FileHighlightingSettings FileHighlighting { get; set; } = new();
        public PanelOptionsSettings     Options          { get; set; } = new();
    }

    public sealed class PanelOptionsSettings
    {
        public bool ShowHiddenAndSystemFiles       { get; set; } = true;
        public bool SelectFolders                  { get; set; } = true;
        public bool RightClickSelectsFiles         { get; set; } = true;
        public bool SortFoldersByExtension         { get; set; } = true;
        public PanelAutoRefreshSettings AutoRefresh { get; set; } = new();
        public bool DetectVolumeMountPoints        { get; set; } = false;
        public bool ShowStatusLine                 { get; set; } = true;
        public bool ShowFilesTotalInformation      { get; set; } = true;
        public bool ShowFreeSize                   { get; set; } = false;
        public bool ShowScrollbar                  { get; set; } = false;
        public bool ShowSortModeLetter             { get; set; } = true;
        public bool ShowParentDirectoryInRootFolders { get; set; } = false;
    }

    public sealed class PanelAutoRefreshSettings
    {
        public int  DisableIfObjectCountExceeds { get; set; } = 0;
        public bool NetworkDrivesAutoRefresh    { get; set; } = false;
    }

    public sealed class FileHighlightingSettings
    {
        public bool                    Enabled    { get; set; } = true;
        public string                  Preset     { get; set; } = "FarDefault";
        public string                  Mode       { get; set; } = "PresetPlusUserRules";
        public List<FileHighlightRule> Rules      { get; set; } = [];
        public List<MaskGroup>         MaskGroups { get; set; } = [];
    }

    public sealed class FileOperationSettings
    {
        public bool ShowTotalProgress { get; set; } = true;
        public bool PreserveTimestamps { get; set; } = true;
        public bool PreserveAttributes { get; set; } = true;
        public string SecurityMode { get; set; } = "Inherit";
        public string SymlinkMode { get; set; } = "CopyLink";
        public bool UseRecycleBinForDelete { get; set; } = true;
        public string ConflictDecision { get; set; } = "Ask";
    }

    public sealed class HistorySettings
    {
        public int MaxCommandHistoryItems   { get; set; } = 1000;
        public int MaxDirectoryHistoryItems { get; set; } = 500;
        public int MaxFileHistoryItems      { get; set; } = 200;
    }
}

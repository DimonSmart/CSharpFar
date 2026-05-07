namespace CSharpFar.Core.Models;

public sealed class AppSettings
{
    public UiSettings      Ui      { get; set; } = new();
    public ShellSettings   Shell   { get; set; } = new();
    public PanelsSettings  Panels  { get; set; } = new();
    public HistorySettings History { get; set; } = new();

    public sealed class UiSettings
    {
        public string Theme           { get; set; } = "classic-blue";
        public bool   ShowHiddenFiles { get; set; } = true;
        public bool   ShowSystemFiles { get; set; } = true;
        public bool   ConfirmDelete   { get; set; } = true;
        public string Palette         { get; set; } = "Default";
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
    }

    public sealed class HistorySettings
    {
        public int MaxCommandHistoryItems   { get; set; } = 1000;
        public int MaxDirectoryHistoryItems { get; set; } = 500;
        public int MaxFileHistoryItems      { get; set; } = 200;
    }
}

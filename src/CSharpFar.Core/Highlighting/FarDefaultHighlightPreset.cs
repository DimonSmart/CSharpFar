namespace CSharpFar.Core.Highlighting;

/// <summary>
/// FarDefault highlight preset: rules and mask groups approximating Far Manager defaults.
/// FAR_COMPAT: Colors use Windows ConsoleColor indices; behaviour approximates Far defaults
/// based on far/hilight.cpp and far/config.cpp at 2026-05-07.
/// </summary>
public static class FarDefaultHighlightPreset
{
    // ── Built-in mask groups ─────────────────────────────────────────────────
    // Source: Far config.cpp ApplyDefaultMaskGroups

    public static readonly IReadOnlyList<MaskGroup> Groups =
    [
        new MaskGroup
        {
            Name = "arc",
            IsBuiltIn = true,
            MaskExpression =
                "*.zip,*.rar,*.[7bgxl]z,*.[bg]zip,*.tar,*.t[agbxl]z,*.z,*.ar[cj]," +
                "*.r[0-9][0-9],*.a[0-9][0-9],*.bz2,*.cab,*.jar,*.lha,*.lzh,*.ha," +
                "*.ac[bei],*.pa[ck],*.rk,*.cpio,*.rpm,*.zoo,*.hqx,*.sit,*.ice,*.uc2," +
                "*.ain,*.imp,*.777,*.ufa,*.boa,*.bs[2a],*.sea,*.[ah]pk,*.ddi,*.x2," +
                "*.rkv,*.[lw]sz,*.h[ay]p,*.lim,*.sqz,*.chz,*.aa[br],*.zst",
        },
        new MaskGroup
        {
            Name = "temp",
            IsBuiltIn = true,
            MaskExpression = "*.bak,*.tmp",
        },
        new MaskGroup
        {
            Name = "exec",
            IsBuiltIn = true,
            MaskExpression = "*.exe,*.cmd,*.bat,*.com,%PATHEXT%",
        },
    ];

    /// <summary>Groups indexed by name (case-insensitive).</summary>
    public static IReadOnlyDictionary<string, MaskGroup> GroupsByName =>
        Groups.ToDictionary(g => g.Name, g => g, StringComparer.OrdinalIgnoreCase);

    // ── Default highlight rules ──────────────────────────────────────────────
    // Source: Far hilight.cpp default highlighting
    // FAR_COMPAT: directory rule catches test.zip before archive rule (no ForbiddenAttributes
    // on exec/arc/temp) — matches Far's default rule ordering.

    public static readonly IReadOnlyList<FileHighlightRule> Rules =
    [
        new FileHighlightRule
        {
            Id          = "far.hidden",
            DisplayName = "Hidden files",
            Order       = 10,
            UseMask     = false,
            RequiredAttributes = FileAttributes.Hidden,
            Colors = new FileHighlightColors
            {
                Normal = new FileHighlightColor(3,  null), // fg DarkCyan
                Cursor = new FileHighlightColor(8,  null), // fg DarkGray
            },
        },
        new FileHighlightRule
        {
            Id          = "far.system",
            DisplayName = "System files",
            Order       = 20,
            UseMask     = false,
            RequiredAttributes = FileAttributes.System,
            Colors = new FileHighlightColors
            {
                Normal = new FileHighlightColor(3,  null),
                Cursor = new FileHighlightColor(8,  null),
            },
        },
        new FileHighlightRule
        {
            Id          = "far.directory",
            DisplayName = "Directories",
            Order       = 30,
            UseMask     = false,
            RequiredAttributes = FileAttributes.Directory,
            Colors = new FileHighlightColors
            {
                Normal = new FileHighlightColor(15, null), // fg White
                Cursor = new FileHighlightColor(15, null),
            },
        },
        new FileHighlightRule
        {
            Id          = "far.exec",
            DisplayName = "Executable files",
            Order       = 40,
            UseMask     = true,
            MaskExpression = "<exec>",
            Colors = new FileHighlightColors
            {
                Normal = new FileHighlightColor(10, null), // fg LightGreen
                Cursor = new FileHighlightColor(10, null),
            },
        },
        new FileHighlightRule
        {
            Id          = "far.archive",
            DisplayName = "Archive files",
            Order       = 50,
            UseMask     = true,
            MaskExpression = "<arc>",
            Colors = new FileHighlightColors
            {
                Normal = new FileHighlightColor(13, null), // fg LightMagenta
                Cursor = new FileHighlightColor(13, null),
            },
        },
        new FileHighlightRule
        {
            Id          = "far.temp",
            DisplayName = "Temporary files",
            Order       = 60,
            UseMask     = true,
            MaskExpression = "<temp>",
            Colors = new FileHighlightColors
            {
                Normal = new FileHighlightColor(6, null), // fg DarkYellow
                Cursor = new FileHighlightColor(6, null),
            },
        },
    ];
}

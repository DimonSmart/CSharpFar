using CSharpFar.Console.Models;
using TextMateSharp.Grammars;
using TextMateSharp.Themes;

namespace CSharpFar.App.Editor;

public sealed class TextMateThemeMapper
{
    private static readonly Dictionary<ConsoleColor, (int Red, int Green, int Blue)> ConsoleColorRgb = new()
    {
        [ConsoleColor.Black] = (0, 0, 0),
        [ConsoleColor.DarkBlue] = (0, 0, 128),
        [ConsoleColor.DarkGreen] = (0, 128, 0),
        [ConsoleColor.DarkCyan] = (0, 128, 128),
        [ConsoleColor.DarkRed] = (128, 0, 0),
        [ConsoleColor.DarkMagenta] = (128, 0, 128),
        [ConsoleColor.DarkYellow] = (128, 128, 0),
        [ConsoleColor.Gray] = (192, 192, 192),
        [ConsoleColor.DarkGray] = (128, 128, 128),
        [ConsoleColor.Blue] = (0, 0, 255),
        [ConsoleColor.Green] = (0, 255, 0),
        [ConsoleColor.Cyan] = (0, 255, 255),
        [ConsoleColor.Red] = (255, 0, 0),
        [ConsoleColor.Magenta] = (255, 0, 255),
        [ConsoleColor.Yellow] = (255, 255, 0),
        [ConsoleColor.White] = (255, 255, 255),
    };

    private readonly Theme _theme;

    public TextMateThemeMapper(Theme theme)
    {
        _theme = theme;
    }

    public CellStyle MapScopes(IReadOnlyList<string> scopes, CellStyle baseStyle)
    {
        ConsoleColor? foreground = MapFarLikeForeground(scopes);
        if (foreground is not null)
            return new CellStyle(foreground.Value, baseStyle.Background);

        IList<string> scopeList = scopes as IList<string> ?? scopes.ToArray();
        foreach (var rule in _theme.Match(scopeList))
        {
            foreground = MapThemeColor(rule.foreground) ?? foreground;
        }

        return new CellStyle(
            foreground ?? baseStyle.Foreground,
            baseStyle.Background);
    }

    public CellStyle MapEncodedStyle(int foregroundId, int backgroundId, CellStyle baseStyle) =>
        new(
            MapThemeColor(foregroundId) ?? baseStyle.Foreground,
            baseStyle.Background);

    public static ThemeName ResolveThemeName(
        string requestedTheme,
        out string resolvedThemeName,
        out string? fallbackReason)
    {
        string normalized = NormalizeThemeName(requestedTheme);
        foreach (ThemeName themeName in Enum.GetValues<ThemeName>())
        {
            string displayName = DisplayName(themeName);
            if (string.Equals(NormalizeThemeName(themeName.ToString()), normalized, StringComparison.Ordinal) ||
                string.Equals(NormalizeThemeName(displayName), normalized, StringComparison.Ordinal))
            {
                resolvedThemeName = displayName;
                fallbackReason = null;
                return themeName;
            }
        }

        resolvedThemeName = DisplayName(ThemeName.DarkPlus);
        fallbackReason = $"Unknown theme '{requestedTheme}', using {resolvedThemeName}.";
        return ThemeName.DarkPlus;
    }

    public static string DisplayName(ThemeName themeName) =>
        themeName switch
        {
            ThemeName.DarkPlus => "Dark+",
            ThemeName.LightPlus => "Light+",
            ThemeName.DimmedMonokai => "Dimmed Monokai",
            ThemeName.KimbieDark => "Kimbie Dark",
            ThemeName.SolarizedDark => "Solarized Dark",
            ThemeName.SolarizedLight => "Solarized Light",
            ThemeName.TomorrowNightBlue => "Tomorrow Night Blue",
            ThemeName.HighContrastLight => "High Contrast Light",
            ThemeName.HighContrastDark => "High Contrast Dark",
            ThemeName.AtomOneLight => "Atom One Light",
            ThemeName.AtomOneDark => "Atom One Dark",
            ThemeName.VisualStudioLight => "Visual Studio Light",
            ThemeName.VisualStudioDark => "Visual Studio Dark",
            _ => themeName.ToString(),
        };

    private ConsoleColor? MapThemeColor(int colorId)
    {
        if (colorId <= 0)
            return null;

        string? color = _theme.GetColor(colorId);
        return TryParseHexColor(color, out int red, out int green, out int blue)
            ? NearestConsoleColor(red, green, blue)
            : null;
    }

    private static ConsoleColor? MapFarLikeForeground(IReadOnlyList<string> scopes)
    {
        if (HasScope(scopes, "invalid"))
            return ConsoleColor.White;
        if (HasScope(scopes, "comment"))
            return ConsoleColor.DarkCyan;
        if (HasScope(scopes, "punctuation"))
            return ConsoleColor.Yellow;
        if (HasScope(scopes, "string"))
            return ConsoleColor.Yellow;
        if (HasScope(scopes, "constant.numeric"))
            return ConsoleColor.Yellow;
        if (HasScope(scopes, "keyword.operator"))
            return ConsoleColor.Yellow;
        if (HasScope(scopes, "entity.name") ||
            HasScope(scopes, "variable.parameter") ||
            HasScope(scopes, "support.type") ||
            HasScope(scopes, "support.class") ||
            HasScope(scopes, "markup.heading"))
        {
            return ConsoleColor.Cyan;
        }

        return null;
    }

    private static bool HasScope(IReadOnlyList<string> scopes, string prefix)
    {
        foreach (string scope in scopes)
        {
            if (scope.StartsWith(prefix, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static ConsoleColor NearestConsoleColor(int red, int green, int blue)
    {
        ConsoleColor bestColor = ConsoleColor.Gray;
        int bestDistance = int.MaxValue;
        foreach (var (consoleColor, rgb) in ConsoleColorRgb)
        {
            int redDelta = red - rgb.Red;
            int greenDelta = green - rgb.Green;
            int blueDelta = blue - rgb.Blue;
            int distance = redDelta * redDelta + greenDelta * greenDelta + blueDelta * blueDelta;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestColor = consoleColor;
            }
        }

        return bestColor;
    }

    private static bool TryParseHexColor(string? color, out int red, out int green, out int blue)
    {
        red = 0;
        green = 0;
        blue = 0;
        if (string.IsNullOrWhiteSpace(color))
            return false;

        string hex = color[0] == '#' ? color[1..] : color;
        if (hex.Length != 6)
            return false;

        return int.TryParse(hex[..2], System.Globalization.NumberStyles.HexNumber, null, out red) &&
            int.TryParse(hex[2..4], System.Globalization.NumberStyles.HexNumber, null, out green) &&
            int.TryParse(hex[4..6], System.Globalization.NumberStyles.HexNumber, null, out blue);
    }

    private static string NormalizeThemeName(string themeName)
    {
        var chars = themeName
            .Where(char.IsLetterOrDigit)
            .Select(char.ToUpperInvariant)
            .ToArray();
        return new string(chars);
    }
}

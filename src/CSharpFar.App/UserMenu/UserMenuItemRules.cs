using CSharpFar.Core.Models;
using System.Text.RegularExpressions;

namespace CSharpFar.App.UserMenu;

internal static class UserMenuItemRules
{
    public static string EffectiveCommand(UserMenuItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return string.IsNullOrWhiteSpace(item.Command)
            ? item.Title
            : item.Command;
    }

    public static string DisplayText(UserMenuItem item)
    {
        string command = EffectiveCommand(item);
        string label = item.Id is null ? item.Title : item.Title + " [" + item.Id + "]";
        return string.Equals(item.Title, command, StringComparison.Ordinal)
            ? label
            : label + "  →  " + command;
    }

    public static string? NormalizeCommand(string title, string? command)
    {
        ArgumentNullException.ThrowIfNull(title);
        return string.IsNullOrWhiteSpace(command) ||
               string.Equals(title, command, StringComparison.Ordinal)
            ? null
            : command;
    }

    public static UserMenuItem Clone(UserMenuItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return new UserMenuItem
        {
            Title = item.Title,
            Command = item.Command,
            Id = item.Id,
            Platform = item.Platform,
        };
    }

    public static UserMenuItem[] CloneItems(IEnumerable<UserMenuItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return items.Select(Clone).ToArray();
    }

    public static UserMenuItem[] NormalizeItems(IEnumerable<UserMenuItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return items.Select(item => new UserMenuItem
        {
            Title = item.Title,
            Command = NormalizeCommand(item.Title, item.Command),
            Id = string.IsNullOrWhiteSpace(item.Id) ? null : item.Id.Trim(),
            Platform = item.Platform,
        }).ToArray();
    }
    public static bool IsValidId(string? id) =>
        id is not null && Regex.IsMatch(id, "^[a-z][a-z0-9._-]{0,63}$", RegexOptions.CultureInvariant);

    public static string? FindValidationError(IReadOnlyList<UserMenuItem> items)
    {
        var seen = new Dictionary<string, List<PlatformKind?>>(StringComparer.Ordinal);
        foreach (UserMenuItem item in items)
        {
            if (item is null || string.IsNullOrWhiteSpace(item.Title))
                return "Every menu item must have a non-empty title.";
            if (item.Id is { } id)
            {
                if (!IsValidId(id))
                    return "Invalid ID '" + id + "' (expected lowercase 1-64 characters: a-z, 0-9, '.', '_' or '-').";
                if (!seen.TryGetValue(id, out var platforms))
                {
                    platforms = [];
                    seen.Add(id, platforms);
                }
                if (platforms.Any(existing => existing is null || item.Platform is null || existing == item.Platform))
                    return "Duplicate ID '" + id + "' has overlapping platform entries.";
                platforms.Add(item.Platform);
            }
            if (item.Platform is { } platform && !Enum.IsDefined(platform))
                return "Unsupported menu platform: " + platform;
        }
        return null;
    }
}

using CSharpFar.Core.Models;

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
        return string.Equals(item.Title, command, StringComparison.Ordinal)
            ? item.Title
            : $"{item.Title}  →  {command}";
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
            Platform = item.Platform,
        }).ToArray();
    }
}

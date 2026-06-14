using CSharpFar.Core.Models;

namespace CSharpFar.App.DirectoryShortcuts;

internal static class DirectoryShortcutNormalizer
{
    public const int MaxNameLength = 8;

    public static IReadOnlyList<AppSettings.DirectoryShortcutItem> Normalize(
        AppSettings.DirectoryShortcutSettings? settings)
    {
        var itemsByNumber = new Dictionary<int, AppSettings.DirectoryShortcutItem>();
        foreach (var item in settings?.Items ?? [])
        {
            if (item is null || !IsValidNumber(item.Number))
                continue;

            string path = item.Path?.Trim() ?? string.Empty;
            if (path.Length == 0)
            {
                itemsByNumber.Remove(item.Number);
                continue;
            }

            itemsByNumber[item.Number] = new AppSettings.DirectoryShortcutItem
            {
                Number = item.Number,
                Name = NormalizeName(item.Name),
                Path = path,
            };
        }

        return DisplayOrder
            .Where(itemsByNumber.ContainsKey)
            .Select(number => itemsByNumber[number])
            .ToArray();
    }

    public static IReadOnlyList<int> DisplayOrder { get; } = [1, 2, 3, 4, 5, 6, 7, 8, 9, 0];

    public static string NormalizeName(string? name)
    {
        string trimmed = name?.Trim() ?? string.Empty;
        return trimmed.Length <= MaxNameLength ? trimmed : trimmed[..MaxNameLength];
    }

    public static bool IsValidNumber(int number) => number is >= 0 and <= 9;

    public static string GetDefaultNameFromPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        string trimmedPath = path.Trim().TrimEnd('/', '\\');
        if (trimmedPath.Length == 0)
            return string.Empty;

        int separatorIndex = trimmedPath.LastIndexOfAny(['/', '\\']);
        string name = separatorIndex >= 0
            ? trimmedPath[(separatorIndex + 1)..]
            : trimmedPath;

        return NormalizeName(name);
    }
}

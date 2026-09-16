using CSharpFar.Core.Models;

namespace CSharpFar.App.UserMenu;

internal static class UserMenuAvailability
{
    public static bool IsAvailableOn(UserMenuItem item, PlatformKind platform) =>
        item.Platform is null || item.Platform == platform;

    public static UserMenuItem[] FilterForPlatform(
        IEnumerable<UserMenuItem> items,
        PlatformKind platform) =>
        items.Where(item => IsAvailableOn(item, platform)).ToArray();
}

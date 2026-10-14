using System.Security.Cryptography;
using System.Text;
using CSharpFar.Plugin.Abstractions;

namespace CSharpFar.App.Plugins;

/// <summary>Stable effective IDs shared by registration validation and menu projection.</summary>
internal static class PluginMenuActionIds
{
    public static Guid Get(string pluginId, PluginMenuItemDescriptor item)
    {
        if (item.ActionId is { } explicitId)
            return explicitId;

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(pluginId + "/" + item.Id));
        return new Guid(hash.AsSpan(0, 16));
    }
}

using System.Security;
using System.Text.Json;
using System.Text.Json.Serialization;
using CSharpFar.Core.Models;

namespace CSharpFar.App.UserMenu;

internal static class UserMenuFile
{
    private static readonly JsonSerializerOptions JsonOptions = CreateOptions();

    public static UserMenuItem[]? ReadOrNull(string path)
    {
        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            throw new IOException("Cannot read user menu '" + path + "': " + ex.Message, ex);
        }

        try
        {
            var items = JsonSerializer.Deserialize<List<UserMenuItem>>(json, JsonOptions)
                ?? throw new InvalidDataException("Expected a JSON array.");
            string? error = UserMenuItemRules.FindValidationError(items);
            if (error is not null)
                throw new InvalidDataException(error);
            return UserMenuItemRules.CloneItems(items);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidDataException)
        {
            throw new InvalidDataException("Invalid user menu '" + path + "': " + ex.Message, ex);
        }
    }

    public static void WriteSafely(string path, IReadOnlyList<UserMenuItem> items)
    {
        string? error = UserMenuItemRules.FindValidationError(items);
        if (error is not null)
            throw new InvalidDataException("Invalid user menu '" + path + "': " + error);

        string directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        string temporaryPath = Path.Combine(
            directory, "." + Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(items, JsonOptions));
            File.Move(temporaryPath, path, overwrite: true);
        }
        catch
        {
            try { File.Delete(temporaryPath); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            throw;
        }
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}

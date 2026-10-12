using System.Text.Json;
using System.Text.Json.Serialization;

namespace CSharpFar.Core.Models;

/// <summary>Canonical persisted values for the macOS app-bundle terminal preference.</summary>
public static class MacOsTerminalPreference
{
    public const string Auto = "auto";
    public const string Kitty = "kitty";
    public const string Terminal = "terminal";

    public static readonly string[] Values = [Auto, Kitty, Terminal];

    public static string Normalize(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            Kitty => Kitty,
            Terminal => Terminal,
            _ => Auto,
        };

    public static string Label(string value) => Normalize(value) switch
    {
        Kitty => "Kitty",
        Terminal => "Terminal.app",
        _ => "Automatic (recommended)",
    };
}

/// <summary>Ignores unexpected JSON value types for this property only.</summary>
public sealed class MacOsTerminalPreferenceConverter : JsonConverter<string>
{
    public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
            return MacOsTerminalPreference.Normalize(reader.GetString());

        using JsonDocument ignoredValue = JsonDocument.ParseValue(ref reader);
        return MacOsTerminalPreference.Auto;
    }

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) =>
        writer.WriteStringValue(MacOsTerminalPreference.Normalize(value));
}

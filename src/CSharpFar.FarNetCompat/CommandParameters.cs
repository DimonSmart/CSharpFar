using System.ComponentModel;
using System.Data.Common;
using System.Globalization;

namespace FarNet;

[Flags]
public enum ParameterOptions
{
    None = 0,
    ExpandVariables = 1,
    GetFullPath = 2,
    UseCursorPath = 4,
    UseCursorFile = 8,
    UseCursorDirectory = 16,
}

public readonly ref struct CommandParameters
{
    private const string TextSeparator = ";;";

    private readonly DbConnectionStringBuilder _parameters;
    private readonly string _command;
    private readonly string _text;
    private readonly string _text2;

    private CommandParameters(
        string command,
        string text,
        string text2,
        DbConnectionStringBuilder parameters)
    {
        _command = command;
        _text = text;
        _text2 = text2;
        _parameters = parameters;
    }

    public ReadOnlySpan<char> Command => _command.AsSpan();

    public ReadOnlySpan<char> Text => _text.AsSpan();

    public ReadOnlySpan<char> Text2 => _text2.AsSpan();

    public static DbConnectionStringBuilder ParseParameters(string parameters)
    {
        var builder = new DbConnectionStringBuilder();
        string text = parameters.Trim();
        if (text.Length == 0)
            return builder;

        try
        {
            builder.ConnectionString = text;
            return builder;
        }
        catch (ArgumentException ex)
        {
            throw new ModuleException("Invalid command parameters.", ex);
        }
    }

    public static CommandParameters Parse(ReadOnlySpan<char> commandLine) =>
        Parse(commandLine, hasCommand: true);

    public static CommandParameters Parse(ReadOnlySpan<char> commandLine, bool hasCommand)
    {
        string line = commandLine.ToString();
        string text = string.Empty;
        string text2 = string.Empty;

        int textIndex = line.IndexOf(TextSeparator, StringComparison.Ordinal);
        if (textIndex >= 0)
        {
            text = line[(textIndex + TextSeparator.Length)..].Trim();
            line = line[..textIndex];
        }

        line = line.Trim();
        string command = string.Empty;
        string parameters = line;
        if (hasCommand)
        {
            int index = 0;
            while (index < line.Length && !char.IsWhiteSpace(line[index]))
                index++;

            command = line[..index];
            parameters = index >= line.Length ? string.Empty : line[index..].Trim();
        }

        if (parameters.StartsWith("@", StringComparison.Ordinal))
            (parameters, text2) = ReadCommandFile(parameters);

        return new CommandParameters(command, text, text2, ParseParameters(parameters));
    }

    public ModuleException ParameterError(string name, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(message);
        return new ModuleException($"Parameter '{name}': {message}");
    }

    public string? GetString(string name, ParameterOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (!_parameters.TryGetValue(name, out object? value))
            return GetMissingString(name, options);

        _parameters.Remove(name);
        string text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        return ApplyStringOptions(name, text, options);
    }

    public string GetRequiredString(string name, ParameterOptions options) =>
        GetString(name, options) ??
        throw ParameterError(name, "Required parameter is missing.");

    public string? GetPath(string name, ParameterOptions options) =>
        GetString(name, options | ParameterOptions.ExpandVariables | ParameterOptions.GetFullPath);

    public string GetRequiredPath(string name, ParameterOptions options) =>
        GetPath(name, options) ??
        throw ParameterError(name, "Required parameter is missing.");

    public string GetPathOrCurrentDirectory(string name, ParameterOptions options) =>
        GetPath(name, options) ?? Far.Api.CurrentDirectory;

    public bool GetBool(string name)
    {
        string? value = GetString(name, ParameterOptions.None);
        if (value is null)
            return false;
        if (value.Length == 0)
            return true;
        if (bool.TryParse(value, out bool result))
            return result;

        throw ParameterError(name, $"Cannot convert '{value}' to Boolean.");
    }

    public T GetValue<T>(string name, T value)
    {
        string? text = GetString(name, ParameterOptions.None);
        if (text is null)
            return value;

        try
        {
            if (typeof(T).IsEnum)
                return (T)Enum.Parse(typeof(T), text, ignoreCase: true);

            var converter = TypeDescriptor.GetConverter(typeof(T));
            if (converter.CanConvertFrom(typeof(string)))
                return (T)converter.ConvertFromInvariantString(text)!;

            return (T)Convert.ChangeType(text, typeof(T), CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or InvalidCastException or NotSupportedException)
        {
            throw ParameterError(name, $"Cannot convert '{text}' to {typeof(T).Name}.");
        }
    }

    public void ThrowUnknownParameters()
    {
        if (_parameters.Count == 0)
            return;

        string names = string.Join(", ", _parameters.Keys.Cast<string>().Order(StringComparer.OrdinalIgnoreCase));
        throw new ModuleException("Unknown parameter(s): " + names + ".");
    }

    private static (string Parameters, string Text2) ReadCommandFile(string parameters)
    {
        int question = parameters.IndexOf('?', StringComparison.Ordinal);
        string filePart = question < 0 ? parameters[1..] : parameters[1..question];
        string text2 = question < 0 ? string.Empty : parameters[(question + 1)..].Trim();
        if (filePart.Length == 0)
            throw new ModuleException("Command file path is missing.");

        string path = Environment.ExpandEnvironmentVariables(filePart);
        if (!Path.IsPathRooted(path))
            path = Path.GetFullPath(path, Far.Api.CurrentDirectory);

        try
        {
            return (File.ReadAllText(path), text2);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            throw new ModuleException($"Cannot read command file '{path}'.", ex);
        }
    }

    private string? GetMissingString(string name, ParameterOptions options)
    {
        var cursorOptions = ParameterOptions.UseCursorPath |
            ParameterOptions.UseCursorFile |
            ParameterOptions.UseCursorDirectory;
        if ((options & cursorOptions) != 0)
            throw new FarNetUnsupportedApiException("CommandParameters cursor path options");

        return null;
    }

    private string ApplyStringOptions(string name, string value, ParameterOptions options)
    {
        string result = value;
        if ((options & ParameterOptions.ExpandVariables) != 0)
            result = Environment.ExpandEnvironmentVariables(result);

        if ((options & ParameterOptions.GetFullPath) != 0 && result.Length > 0)
        {
            try
            {
                result = Path.IsPathRooted(result)
                    ? Path.GetFullPath(result)
                    : Path.GetFullPath(result, Far.Api.CurrentDirectory);
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
            {
                throw ParameterError(name, $"Invalid path '{result}'.");
            }
        }

        return result;
    }
}

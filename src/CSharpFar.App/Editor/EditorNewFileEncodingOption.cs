using System.Globalization;
using System.Text;
using CSharpFar.Core.Models;
using CSharpFar.Core.Text;

namespace CSharpFar.App.Editor;

internal sealed record EditorNewFileEncodingOption(
    string Label,
    int? CodePage,
    bool EmitByteOrderMark)
{
    public bool IsDefault => CodePage is null;

    public EditorDocumentFormat CreateDocumentFormat(AppSettings.EditorSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (IsDefault)
            return EditorFileService.CreateDefaultNewFileFormat(settings);

        TextEncodingDetector.EnsureCodePagesProviderRegistered();
        Encoding encoding = Encoding.GetEncoding(CodePage!.Value);
        return new EditorDocumentFormat(
            encoding,
            EmitByteOrderMark,
            EditorSettingsResolver.ResolveDefaultLineEnding(settings),
            EncodingDisplayName(CodePage.Value));
    }

    public static IReadOnlyList<EditorNewFileEncodingOption> CreateCatalog()
    {
        TextEncodingDetector.EnsureCodePagesProviderRegistered();

        var options = new List<EditorNewFileEncodingOption>
        {
            new("Default", null, EmitByteOrderMark: false),
            new("UTF-8", Encoding.UTF8.CodePage, EmitByteOrderMark: false),
            new("UTF-8 with BOM", Encoding.UTF8.CodePage, EmitByteOrderMark: true),
            new("UTF-16 LE with BOM", Encoding.Unicode.CodePage, EmitByteOrderMark: true),
            new("UTF-16 BE with BOM", Encoding.BigEndianUnicode.CodePage, EmitByteOrderMark: true),
            new("Windows-1251", 1251, EmitByteOrderMark: false),
        };

        if (CanResolveCodePage(866))
            options.Add(new("OEM 866", 866, EmitByteOrderMark: false));

        int ansiCodePage = CultureInfo.CurrentCulture.TextInfo.ANSICodePage;
        if (ansiCodePage is not 1251 and not 866 &&
            CanResolveCodePage(ansiCodePage))
        {
            options.Add(new($"Windows ANSI ({ansiCodePage})", ansiCodePage, EmitByteOrderMark: false));
        }

        return options;
    }

    private static bool CanResolveCodePage(int codePage)
    {
        try
        {
            _ = Encoding.GetEncoding(codePage);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static string EncodingDisplayName(int codePage) =>
        codePage switch
        {
            65001 => "UTF-8",
            1200 => "UTF-16 LE",
            1201 => "UTF-16 BE",
            1251 => "Windows-1251",
            866 => "OEM 866",
            _ => Encoding.GetEncoding(codePage).EncodingName,
        };
}

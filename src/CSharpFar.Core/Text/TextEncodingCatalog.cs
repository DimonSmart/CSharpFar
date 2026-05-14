using System.Globalization;
using System.Text;

namespace CSharpFar.Core.Text;

public static class TextEncodingCatalog
{
    public static IReadOnlyList<TextEncodingCatalogItem> CreateViewerCatalog(
        EncodingDetectionResult currentDetection)
    {
        ArgumentNullException.ThrowIfNull(currentDetection);
        TextEncodingDetector.EnsureCodePagesProviderRegistered();

        int ansiCodePage = CultureInfo.CurrentCulture.TextInfo.ANSICodePage;

        return
        [
            new TextEncodingCatalogItem(
                TextEncodingSelection.Automatic,
                $"Automatic ({currentDetection.DisplayName})"),
            new TextEncodingCatalogItem(TextEncodingSelection.Explicit(Encoding.UTF8.CodePage), "UTF-8"),
            new TextEncodingCatalogItem(TextEncodingSelection.Explicit(Encoding.Unicode.CodePage), "UTF-16 LE"),
            new TextEncodingCatalogItem(TextEncodingSelection.Explicit(Encoding.BigEndianUnicode.CodePage), "UTF-16 BE"),
            new TextEncodingCatalogItem(TextEncodingSelection.Explicit(ansiCodePage), $"Windows ANSI ({ansiCodePage})"),
            new TextEncodingCatalogItem(TextEncodingSelection.Explicit(1251), "Windows-1251"),
            new TextEncodingCatalogItem(TextEncodingSelection.Explicit(1252), "Windows-1252"),
            new TextEncodingCatalogItem(TextEncodingSelection.Explicit(866), "CP866"),
        ];
    }
}

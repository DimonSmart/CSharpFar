using System.Globalization;
using System.Text;
using CSharpFar.Console.Input;
using CSharpFar.Ui;

namespace CSharpFar.App.Editor;

internal sealed class EditorFormatDialog
{
    private const int DialogWidth = 52;
    private const int DialogHeight = 9;
    private const string EncodingRowId = "editor-format.encoding";
    private const string BomRowId = "editor-format.bom";
    private const string LineEndingRowId = "editor-format.line-ending";

    private readonly DialogService _dialogs;

    public EditorFormatDialog(DialogService dialogs)
    {
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
    }

    public EditorDocumentFormat? Show(EditorDocumentFormat current)
    {
        var encoding = FormControls.CompactChoice(
            EncodingRowId, "Encoding", Encodings, static value => value.Label,
            new EncodingSpec(current.Encoding.CodePage, string.Empty), EncodingSpecCodePageComparer);
        var bom = FormControls.CompactChoice(
            BomRowId, "BOM", BomChoices, static value => value ? "Yes" : "No", current.EmitByteOrderMark);
        var lineEnding = FormControls.CompactChoice(
            LineEndingRowId, "Line ends", LineEndings, static value => value.Value.ToDisplayName(),
            new LineEndingSpec(current.LineEnding), LineEndingSpecValueComparer);
        return _dialogs.Form(
            new FormDialogOptions("Editor format", DialogWidth, DialogHeight, SubmitOnEnter: true)
            {
                Layout = new FormLayoutOptions(CursorPolicy: FormCursorPolicy.Hidden),
            },
            rows: () =>
            [
                encoding,
                bom,
                lineEnding,
                FormControls.Spacer(),
                FormControls.Label("Enter apply  Esc/F10 cancel  Left/Right change"),
            ],
            handle: result =>
            {
                if (result.IsCancelled || result.Key == ConsoleKey.F10)
                    return FormDialogOutcome<EditorDocumentFormat?>.Complete(null);

                if (result.IsSubmitted)
                    return FormDialogOutcome<EditorDocumentFormat?>.Complete(
                        CreateFormat(encoding.Value, bom.Value, lineEnding.Value.Value));

                return FormDialogOutcome<EditorDocumentFormat?>.Continue();
            });
    }

    private static EditorDocumentFormat CreateFormat(EncodingSpec encodingSpec, bool emitBom, EditorLineEnding lineEnding)
    {
        Encoding encoding = Encoding.GetEncoding(encodingSpec.CodePage);
        return new EditorDocumentFormat(encoding, emitBom, lineEnding, encodingSpec.Label);
    }

    private readonly record struct EncodingSpec(int CodePage, string Label);
    private readonly record struct LineEndingSpec(EditorLineEnding Value);

    private static readonly IEqualityComparer<EncodingSpec> EncodingSpecCodePageComparer =
        EqualityComparer<EncodingSpec>.Create(static (left, right) => left.CodePage == right.CodePage);
    private static readonly IEqualityComparer<LineEndingSpec> LineEndingSpecValueComparer =
        EqualityComparer<LineEndingSpec>.Create(static (left, right) => left.Value == right.Value);

    private static readonly EncodingSpec[] Encodings =
    [
        new(Encoding.UTF8.CodePage, "UTF-8"),
        new(Encoding.Unicode.CodePage, "UTF-16 LE"),
        new(Encoding.BigEndianUnicode.CodePage, "UTF-16 BE"),
        new(CultureInfo.CurrentCulture.TextInfo.ANSICodePage, $"Windows ANSI ({CultureInfo.CurrentCulture.TextInfo.ANSICodePage})"),
        new(1251, "Windows-1251"),
        new(1252, "Windows-1252"),
        new(866, "CP866"),
    ];

    private static readonly LineEndingSpec[] LineEndings =
    [
        new(EditorLineEnding.CrLf),
        new(EditorLineEnding.Lf),
        new(EditorLineEnding.Cr),
        new(EditorLineEnding.Mixed),
    ];

    private static readonly bool[] BomChoices = [false, true];
}

file static class EditorLineEndingDisplay
{
    public static string ToDisplayName(this EditorLineEnding value) => value switch
    {
        EditorLineEnding.CrLf => "CRLF",
        EditorLineEnding.Lf => "LF",
        EditorLineEnding.Cr => "CR",
        EditorLineEnding.Mixed => "Mixed",
        _ => value.ToString(),
    };
}

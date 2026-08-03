using System.Globalization;
using System.Text;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Ui;

namespace CSharpFar.App.Editor;

internal sealed class EditorFormatDialog
{
    private const int DialogWidth = 52;
    private const int DialogHeight = 9;
    private const string EncodingRowId = "editor-format.encoding";
    private const string BomRowId = "editor-format.bom";
    private const string LineEndingRowId = "editor-format.line-ending";

    private readonly ModalFormHost _formDialogs;

    public EditorFormatDialog(ModalDialogHost modalDialogs)
    {
        _formDialogs = new ModalFormHost(modalDialogs);
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
        var form = new ScrollableFormDialog(new FormLayoutOptions(CursorPolicy: FormCursorPolicy.Hidden));

        void PrepareRows() =>
            form.SetRows(
                [
                    encoding,
                    bom,
                    lineEnding,
                    new SpacerRow(FarDialogStyles.Border),
                    new LabelRow("Enter apply  Esc/F10 cancel  Left/Right change"),
                ]);

        return _formDialogs.Run(
            form,
            new ModalFormOptions("Editor format", DialogWidth, DialogHeight, SubmitOnEnter: true),
            static layout => ModalFormLayout.BodyOnly(layout.ContentBounds),
            (routed, result) =>
            {
                if (result.Kind == FormInputResultKind.Cancel ||
                    routed.Input is KeyConsoleInputEvent { Key.Key: ConsoleKey.F10 })
                {
                    return ModalDialogLoopResult<EditorDocumentFormat?>.Complete(null);
                }

                if (result.Kind == FormInputResultKind.Submit ||
                    FormDialogInput.ShouldImplicitlySubmit(routed, result, form))
                {
                    return ModalDialogLoopResult<EditorDocumentFormat?>.Complete(
                        CreateFormat(encoding.Value, bom.Value, lineEnding.Value.Value));
                }

                return ModalDialogLoopResult<EditorDocumentFormat?>.ContinueNoChange;
            },
            prepareRender: PrepareRows);
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

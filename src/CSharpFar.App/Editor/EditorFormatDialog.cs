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
        var encoding = new CompactChoiceFormRow<EncodingSpec>(
            label: "Encoding",
            values: Encodings,
            format: static value => value.Label,
            selectedValue: new EncodingSpec(current.Encoding.CodePage, string.Empty),
            comparer: EncodingSpecCodePageComparer)
        {
            Id = EncodingRowId,
            ShowCursor = false,
        };
        var bom = new CompactChoiceFormRow<bool>(
            label: "BOM",
            values: BomChoices,
            format: static value => value ? "Yes" : "No",
            selectedValue: current.EmitByteOrderMark)
        {
            Id = BomRowId,
            ShowCursor = false,
        };
        var lineEnding = new CompactChoiceFormRow<LineEndingSpec>(
            label: "Line ends",
            values: LineEndings,
            format: static value => value.Value.ToDisplayName(),
            selectedValue: new LineEndingSpec(current.LineEnding),
            comparer: LineEndingSpecValueComparer)
        {
            Id = LineEndingRowId,
            ShowCursor = false,
        };
        var form = new ScrollableFormDialog();

        void PrepareRows() =>
            form.SetRows(
                [
                    encoding,
                    bom,
                    lineEnding,
                    new SpacerRow(FarDialogStyles.Border),
                    new LabelRow("Enter apply  Esc/F10 cancel  Left/Right change", FarDialogStyles.Fill),
                ]);

        return _formDialogs.Run(
            form,
            new ModalFormOptions("Editor format", DialogWidth, DialogHeight, SubmitOnEnter: true),
            static layout => new ModalFormLayout(layout.ContentBounds),
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

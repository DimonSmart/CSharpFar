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

    private readonly ModalDialogHost _modalDialogs;
    private readonly ModalDialogRenderer _modalRenderer = new();

    public EditorFormatDialog(ModalDialogHost modalDialogs)
    {
        _modalDialogs = modalDialogs;
    }

    public EditorDocumentFormat? Show(EditorDocumentFormat current)
    {
        var encoding = new CompactChoiceFormRow<EncodingSpec>(
            new ChoiceRow<EncodingSpec>(Encodings, static value => value.Label, EncodingIndex(current.Encoding.CodePage)),
            "Encoding")
        {
            Id = EncodingRowId,
            ShowCursor = false,
        };
        var bom = new CompactChoiceFormRow<bool>(
            new ChoiceRow<bool>(BomChoices, static value => value ? "Yes" : "No", current.EmitByteOrderMark ? 1 : 0),
            "BOM")
        {
            Id = BomRowId,
            ShowCursor = false,
        };
        var lineEnding = new CompactChoiceFormRow<LineEndingSpec>(
            new ChoiceRow<LineEndingSpec>(LineEndings, static value => value.Value.ToDisplayName(), LineEndingIndex(current.LineEnding)),
            "Line ends")
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
                    new SeparatorRow(FarDialogStyles.Border, drawLine: false),
                    new LabelRow("Enter apply  Esc/F10 cancel  Left/Right change", FarDialogStyles.Fill),
                ]);

        return _modalDialogs.RunInteractive<ScrollableFormFrame, FormInputResult, EditorDocumentFormat?>(
            (context, focusScope) => Draw(context, focusScope, form),
            form.BuildInteractionFrame,
            (input, frame, route) =>
            {
                if (input is KeyConsoleInputEvent { Key.Key: ConsoleKey.Enter })
                    return (FormInputResult.Submit(), UiInputResult.HandledResult);

                FormRouteResult result = form.RouteInput(input, frame, route);
                return (result.FormResult, result.UiResult);
            },
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

                return ModalDialogLoopResult<EditorDocumentFormat?>.Continue;
            },
            prepareRender: PrepareRows);
    }

    private ScrollableFormFrame Draw(UiRenderContext context, UiFocusScope focusScope, ScrollableFormDialog form)
    {
        ScrollableFormFrame? frame = null;
        _modalRenderer.Render(
            context.Screen,
            OuterBounds(context.Size),
            "Editor format",
            doubleBorder: true,
            FarDialogStyles.OuterOptions,
            FarDialogStyles.FrameOptions,
            (_, layout) =>
            {
                Rect contentBounds = layout.ContentBounds;
                var bodyBounds = contentBounds.Width >= 2
                    ? new Rect(contentBounds.X + 1, contentBounds.Y, contentBounds.Width - 2, contentBounds.Height)
                    : new Rect(contentBounds.X, contentBounds.Y, 0, 0);
                frame = form.Render(new FormRenderContext(context, bodyBounds, FarDialogStyles.Border), focusScope);
            });

        return frame ?? throw new InvalidOperationException("Editor format dialog did not render a form frame.");
    }

    private static Rect OuterBounds(ConsoleSize size)
    {
        return new ModalDialogRenderer().CenteredOuterBounds(size, DialogWidth, DialogHeight);
    }

    private static EditorDocumentFormat CreateFormat(EncodingSpec encodingSpec, bool emitBom, EditorLineEnding lineEnding)
    {
        Encoding encoding = Encoding.GetEncoding(encodingSpec.CodePage);
        return new EditorDocumentFormat(encoding, emitBom, lineEnding, encodingSpec.Label);
    }

    private static int EncodingIndex(int codePage)
    {
        for (int index = 0; index < Encodings.Length; index++)
        {
            if (Encodings[index].CodePage == codePage)
                return index;
        }

        return 0;
    }

    private static int LineEndingIndex(EditorLineEnding lineEnding)
    {
        for (int index = 0; index < LineEndings.Length; index++)
        {
            if (LineEndings[index].Value == lineEnding)
                return index;
        }

        return 0;
    }

    private readonly record struct EncodingSpec(int CodePage, string Label);
    private readonly record struct LineEndingSpec(EditorLineEnding Value);

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

using System.Globalization;
using System.Text;
using CSharpFar.App.Rendering;
using CSharpFar.Console;
using CSharpFar.Console.Models;
using CSharpFar.Ui;

namespace CSharpFar.App.Editor;

internal sealed class EditorFormatDialog
{
    private const int DialogWidth = 52;
    private const int DialogHeight = 9;

    private readonly ScreenRenderer _screen;
    private readonly ModalDialogHost _modalDialogs;
    private readonly ConsolePalette _palette;
    private readonly ModalDialogRenderer _modalRenderer = new();

    public EditorFormatDialog(ScreenRenderer screen, ModalDialogHost modalDialogs, ConsolePalette palette)
    {
        _screen = screen;
        _modalDialogs = modalDialogs;
        _palette = palette;
    }

    public EditorDocumentFormat? Show(EditorDocumentFormat current)
    {
        int encodingIndex = EncodingIndex(current.Encoding.CodePage);
        int lineEndingIndex = LineEndingIndex(current.LineEnding);
        bool emitBom = current.EmitByteOrderMark;
        int row = 0;
        using var modal = _modalDialogs.Open(context =>
        {
            var format = CreateFormat(encodingIndex, emitBom, lineEndingIndex);
            Draw(context, format, row);
        });

        while (true)
        {
            modal.Render();
            var input = modal.ReadInput();
            if (input is not CSharpFar.Console.Input.KeyConsoleInputEvent { Key: var key })
                continue;

            var format = CreateFormat(encodingIndex, emitBom, lineEndingIndex);
            switch (key.Key)
            {
                case ConsoleKey.UpArrow: row = Math.Max(0, row - 1); break;
                case ConsoleKey.DownArrow: row = Math.Min(2, row + 1); break;
                case ConsoleKey.LeftArrow: Change(row, -1, ref encodingIndex, ref emitBom, ref lineEndingIndex); break;
                case ConsoleKey.RightArrow:
                case ConsoleKey.Spacebar: Change(row, 1, ref encodingIndex, ref emitBom, ref lineEndingIndex); break;
                case ConsoleKey.Enter: return format;
                case ConsoleKey.Escape:
                case ConsoleKey.F10: return null;
            }
        }
    }

    private void Draw(UiRenderContext context, EditorDocumentFormat format, int cursorRow)
    {
        Rect bounds = _modalRenderer.CenteredOuterBounds(context.Size, DialogWidth, DialogHeight, minHeight: DialogHeight);
        _modalRenderer.Render(
            _screen,
            bounds,
            "Editor format",
            doubleBorder: true,
            PaletteStyles.DialogPopupOptions(_palette) with { DrawBorder = false },
            PaletteStyles.DialogPopupOptions(_palette) with { DrawShadow = false },
            (_, layout) =>
            {
                DrawRow(layout.ContentBounds, 0, "Encoding", format.EncodingDisplayName, cursorRow == 0);
                DrawRow(layout.ContentBounds, 1, "BOM", format.BomDisplayName, cursorRow == 1);
                DrawRow(layout.ContentBounds, 2, "Line ends", format.LineEndingDisplayName, cursorRow == 2);
                const string hint = " Enter apply  Esc cancel  Left/Right change ";
                _screen.Write(
                    layout.ContentBounds.X,
                    layout.ContentBounds.Y + 4,
                    Fit(hint, layout.ContentBounds.Width),
                    PaletteStyles.DialogFill(_palette));
            });
        _screen.SetCursorVisible(false);
    }

    private void DrawRow(Rect content, int row, string label, string value, bool selected)
    {
        string text = $"{label,-11} {value}";
        _screen.Write(
            content.X,
            content.Y + row,
            Fit(text, content.Width),
            selected ? PaletteStyles.InputField(_palette) : PaletteStyles.DialogFill(_palette));
    }

    private static void Change(
        int row,
        int delta,
        ref int encodingIndex,
        ref bool emitBom,
        ref int lineEndingIndex)
    {
        if (row == 0)
            encodingIndex = Mod(encodingIndex + delta, Encodings.Length);
        else if (row == 1)
            emitBom = !emitBom;
        else
            lineEndingIndex = Mod(lineEndingIndex + delta, LineEndings.Length);
    }

    private static EditorDocumentFormat CreateFormat(int encodingIndex, bool emitBom, int lineEndingIndex)
    {
        var spec = Encodings[encodingIndex];
        Encoding encoding = Encoding.GetEncoding(spec.CodePage);
        return new EditorDocumentFormat(encoding, emitBom, LineEndings[lineEndingIndex].Value, spec.Label);
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

    private static int Mod(int value, int size) => ((value % size) + size) % size;

    private static string Fit(string text, int width) =>
        text.Length <= width ? text.PadRight(width) : text[..width];

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
}

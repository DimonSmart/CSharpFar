using System.Text;
using CSharpFar.App.Dialogs;
using CSharpFar.App.Rendering;
using CSharpFar.App.Viewer;
using CSharpFar.Console;
using CSharpFar.Console.Models;

namespace CSharpFar.App.Editor;

/// <summary>
/// Full-screen text file editor.
/// F2 saves, F10/Esc exits (prompts if unsaved changes exist).
/// </summary>
internal sealed class FileEditor
{
    private readonly ScreenRenderer _screen;

    public FileEditor(ScreenRenderer screen) => _screen = screen;

    public void Show(string filePath)
    {
        if (!File.Exists(filePath))
        {
            new MessageDialog(_screen).Show("Editor", "File not found.");
            return;
        }

        var info = new FileInfo(filePath);
        if (info.Length > TextFileReader.MaxFileSizeBytes)
        {
            new MessageDialog(_screen).Show(
                "Editor",
                $"File too large (max {TextFileReader.MaxFileSizeBytes / 1024 / 1024} MB).");
            return;
        }

        string[] lines;
        Encoding encoding;
        try   { (lines, encoding) = TextFileReader.ReadLinesAndEncoding(filePath); }
        catch (Exception ex) { new MessageDialog(_screen).Show("Editor", ex.Message); return; }

        var model = new EditorModel(lines);

        var size  = _screen.GetSize();
        var saved = _screen.Capture(new Rect(0, 0, size.Width, size.Height));
        try   { RunLoop(filePath, model, encoding); }
        finally { _screen.Restore(saved); }
    }

    // ── main loop ─────────────────────────────────────────────────────────────

    private void RunLoop(string filePath, EditorModel model, Encoding encoding)
    {
        int scrollTop  = 0;
        int scrollLeft = 0;

        while (true)
        {
            var size     = _screen.GetSize();
            int contentH = size.Height - 2;
            int contentW = size.Width;

            EnsureCursorVisible(model, contentH, contentW, ref scrollTop, ref scrollLeft);
            Draw(filePath, model, scrollTop, scrollLeft, contentH, size);

            var key = _screen.ReadKey();

            bool isPrintable = key.KeyChar >= ' ' &&
                (key.Modifiers & (ConsoleModifiers.Control | ConsoleModifiers.Alt)) == 0;

            if (isPrintable) { model.InsertChar(key.KeyChar); continue; }

            switch (key.Key)
            {
                case ConsoleKey.Backspace: model.DeleteBack();    break;
                case ConsoleKey.Delete:    model.DeleteForward(); break;
                case ConsoleKey.Enter:     model.BreakLine();     break;

                case ConsoleKey.LeftArrow:  model.MoveLeft();    break;
                case ConsoleKey.RightArrow: model.MoveRight();   break;
                case ConsoleKey.UpArrow:    model.MoveUp();      break;
                case ConsoleKey.DownArrow:  model.MoveDown();    break;

                case ConsoleKey.PageUp:   model.MoveUp(contentH);   break;
                case ConsoleKey.PageDown: model.MoveDown(contentH); break;

                case ConsoleKey.Home when (key.Modifiers & ConsoleModifiers.Control) != 0:
                    model.MoveToDocStart();  break;
                case ConsoleKey.Home:
                    model.MoveToLineStart(); break;

                case ConsoleKey.End when (key.Modifiers & ConsoleModifiers.Control) != 0:
                    model.MoveToDocEnd();  break;
                case ConsoleKey.End:
                    model.MoveToLineEnd(); break;

                case ConsoleKey.F2:
                    SaveFile(filePath, model, encoding); break;

                case ConsoleKey.Escape:
                case ConsoleKey.F10:
                    if (TryExit(filePath, model, encoding)) return;
                    break;
            }
        }
    }

    // ── exit / save ───────────────────────────────────────────────────────────

    private bool TryExit(string filePath, EditorModel model, Encoding encoding)
    {
        if (!model.IsDirty) return true;

        var choice = new SaveChangesDialog(_screen).Show(Path.GetFileName(filePath));
        switch (choice)
        {
            case SaveChangesChoice.Save:
                return SaveFile(filePath, model, encoding);
            case SaveChangesChoice.Discard:
                return true;
            default: // Cancel
                return false;
        }
    }

    private bool SaveFile(string filePath, EditorModel model, Encoding encoding)
    {
        try
        {
            File.WriteAllText(filePath, model.GetText(), encoding);
            model.MarkClean();
            return true;
        }
        catch (Exception ex)
        {
            new MessageDialog(_screen).Show("Save Error", ex.Message);
            return false;
        }
    }

    // ── rendering ─────────────────────────────────────────────────────────────

    private void Draw(
        string filePath, EditorModel model,
        int scrollTop, int scrollLeft, int contentH, ConsoleSize size)
    {
        // Header: filename (with dirty marker) + cursor position
        string nameSection = $" {(model.IsDirty ? "* " : "")}{Path.GetFileName(filePath)} ";
        string posSection  = $" {model.CursorRow + 1}:{model.CursorCol + 1} ";
        int nameWidth = Math.Max(0, size.Width - posSection.Length);
        if (nameSection.Length > nameWidth) nameSection = nameSection[..nameWidth];
        _screen.Write(0, 0, nameSection.PadRight(nameWidth) + posSection, Theme.PathHeaderActive);

        // Content
        for (int i = 0; i < contentH; i++)
        {
            int lineIdx = scrollTop + i;
            string text = lineIdx < model.Lines.Count
                ? FormatLine(model.Lines[lineIdx], scrollLeft, size.Width)
                : new string(' ', size.Width);
            _screen.Write(0, i + 1, text, Theme.CommandLine);
        }

        // Footer key bar
        _screen.FillRegion(new Rect(0, size.Height - 1, size.Width, 1), Theme.KeyBarLabel);
        _screen.Write(0, size.Height - 1, "2",     Theme.KeyBarNum);
        _screen.Write(1, size.Height - 1, "Save  ", Theme.KeyBarLabel);
        _screen.Write(7, size.Height - 1, "10",    Theme.KeyBarNum);
        _screen.Write(9, size.Height - 1, "Close", Theme.KeyBarLabel);

        // Cursor
        int screenRow = 1 + (model.CursorRow - scrollTop);
        int screenCol = model.CursorCol - scrollLeft;
        if (screenRow >= 1 && screenRow <= contentH && screenCol >= 0 && screenCol < size.Width)
        {
            _screen.SetCursorPosition(screenCol, screenRow);
            _screen.SetCursorVisible(true);
        }
        else
        {
            _screen.SetCursorVisible(false);
        }
    }

    private static void EnsureCursorVisible(
        EditorModel model, int contentH, int contentW,
        ref int scrollTop, ref int scrollLeft)
    {
        if (model.CursorRow < scrollTop)
            scrollTop = model.CursorRow;
        else if (model.CursorRow >= scrollTop + contentH)
            scrollTop = model.CursorRow - contentH + 1;

        if (model.CursorCol < scrollLeft)
            scrollLeft = model.CursorCol;
        else if (model.CursorCol >= scrollLeft + contentW)
            scrollLeft = model.CursorCol - contentW + 1;
    }

    // Replace tabs with a single space for 1:1 cursor-to-column mapping
    private static string FormatLine(string line, int scrollLeft, int width)
    {
        line = line.Replace("\t", " ");
        if (scrollLeft >= line.Length) return new string(' ', width);
        string visible = line[scrollLeft..];
        return visible.Length <= width ? visible.PadRight(width) : visible[..width];
    }
}

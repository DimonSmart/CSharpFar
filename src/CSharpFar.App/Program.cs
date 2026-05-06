// CSharpFar - Console dual-panel file manager
// Stage 1: console abstraction layer demo

using CSharpFar.Console;
using CSharpFar.Console.Models;

var driver = new SystemConsoleDriver();
var renderer = new ScreenRenderer(driver);

var size = renderer.GetSize();
renderer.SetCursorVisible(false);

var panelStyle = new CellStyle(ConsoleColor.White, ConsoleColor.DarkBlue);
var titleStyle = new CellStyle(ConsoleColor.Yellow, ConsoleColor.DarkBlue);

renderer.FillRegion(new Rect(0, 0, size.Width, size.Height), panelStyle);
renderer.DrawBox(new Rect(0, 0, size.Width / 2, size.Height - 2), panelStyle);
renderer.DrawBox(new Rect(size.Width / 2, 0, size.Width - size.Width / 2, size.Height - 2), panelStyle);

renderer.Write(2, 0, " CSharpFar Stage 1 ", titleStyle);
renderer.Write(2, size.Height - 2, $"Console size: {size.Width}x{size.Height}  Press any key to exit...", CellStyle.Default);

renderer.SetCursorPosition(0, size.Height - 1);
renderer.SetCursorVisible(true);

renderer.ReadKey();

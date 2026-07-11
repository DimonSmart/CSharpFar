using CSharpFar.Console;
using CSharpFar.Tests.Fakes;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

internal static class ModalTestHost
{
    public static ModalDialogHost Create(FakeConsoleDriver driver)
        => UiTestHost.Create(driver).ModalDialogs;

    public static ModalDialogHost Create(ScreenRenderer screen)
        => UiTestHost.Create(screen).ModalDialogs;
}

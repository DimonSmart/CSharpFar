using System.Globalization;

namespace FarNet;

public abstract class IFar
{
    public void Message(string text) =>
        Message(new MessageArgs { Text = text, Options = MessageOptions.Ok });

    public void Message(string text, string? caption) =>
        Message(new MessageArgs { Text = text, Caption = caption, Options = MessageOptions.Ok });

    public int Message(string text, string? caption, MessageOptions options) =>
        Message(new MessageArgs { Text = text, Caption = caption, Options = options });

    public int Message(string text, string? caption, MessageOptions options, string[]? buttons) =>
        Message(new MessageArgs { Text = text, Caption = caption, Options = options, Buttons = buttons });

    public int Message(
        string text,
        string? caption,
        MessageOptions options,
        string[]? buttons,
        string? helpTopic) =>
        Message(new MessageArgs
        {
            Text = text,
            Caption = caption,
            Options = options,
            Buttons = buttons,
            HelpTopic = helpTopic,
        });

    public abstract int Message(MessageArgs args);

    public string? Input(string? prompt) =>
        Input(prompt, null, null, null);

    public string? Input(string? prompt, string? history) =>
        Input(prompt, history, null, null);

    public string? Input(string? prompt, string? history, string? title) =>
        Input(prompt, history, title, null);

    public abstract string? Input(string? prompt, string? history, string? title, string? text);

    public abstract IModuleAction? GetModuleAction(Guid id);
    public abstract Version FarVersion { get; }
    public abstract Version FarNetVersion { get; }
    public abstract void ShowError(string? title, Exception exception);
    public abstract string CurrentDirectory { get; }
    public abstract string GetFullPath(string path);
    public abstract string TempName(string? prefix);
    public string TempName() => TempName(null);
    public abstract IModuleManager GetModuleManager(string name);
    public virtual IModuleManager GetModuleManager(Type type) =>
        GetModuleManager(Path.GetFileNameWithoutExtension(type.Assembly.Location));

    public abstract CultureInfo GetCurrentUICulture(bool update);
    public abstract void ShowHelp(string path, string topic, HelpOptions options);
    public virtual string? PasteFromClipboard() => throw new FarNetUnsupportedApiException(nameof(PasteFromClipboard));
    public virtual void CopyToClipboard(string text) => throw new FarNetUnsupportedApiException(nameof(CopyToClipboard));

    public virtual IMenu CreateMenu() => throw new FarNetUnsupportedApiException(nameof(CreateMenu));
    public virtual IListMenu CreateListMenu() => throw new FarNetUnsupportedApiException(nameof(CreateListMenu));
    public virtual IInputBox CreateInputBox() => throw new FarNetUnsupportedApiException(nameof(CreateInputBox));
    public virtual IEditor CreateEditor() => throw new FarNetUnsupportedApiException(nameof(CreateEditor));
    public virtual IViewer CreateViewer() => throw new FarNetUnsupportedApiException(nameof(CreateViewer));
    public virtual IDialog CreateDialog(int left, int top, int right, int bottom) =>
        throw new FarNetUnsupportedApiException(nameof(CreateDialog));

    public virtual IUserInterface UI => throw new FarNetUnsupportedApiException(nameof(UI));
    public virtual IPanel? Panel => null;
    public virtual IPanel? Panel2 => null;
    public virtual ILine CommandLine => throw new FarNetUnsupportedApiException(nameof(CommandLine));
    public virtual IWindow Window => throw new FarNetUnsupportedApiException(nameof(Window));
    public virtual IHistory History => throw new FarNetUnsupportedApiException(nameof(History));
    public virtual IAnyEditor AnyEditor => throw new FarNetUnsupportedApiException(nameof(AnyEditor));
    public virtual IAnyViewer AnyViewer => throw new FarNetUnsupportedApiException(nameof(AnyViewer));
}

public static class Far
{
    private static IFar _api = new UnsupportedFarApi();

    public static IFar Api
    {
        get => _api;
        set => _api = value ?? throw new ArgumentNullException(nameof(value));
    }

    public static void ResetApi() => _api = new UnsupportedFarApi();
}

internal sealed class UnsupportedFarApi : IFar
{
    public override IModuleAction? GetModuleAction(Guid id) => null;
    public override Version FarVersion => new(0, 0);
    public override Version FarNetVersion => new(10, 0, 30);

    public override int Message(MessageArgs args) =>
        throw new FarNetUnsupportedApiException(nameof(Message));

    public override string? Input(string? prompt, string? history, string? title, string? text) =>
        throw new FarNetUnsupportedApiException(nameof(Input));

    public override void ShowError(string? title, Exception exception) =>
        throw new FarNetUnsupportedApiException(nameof(ShowError));

    public override string CurrentDirectory =>
        throw new FarNetUnsupportedApiException(nameof(CurrentDirectory));

    public override string GetFullPath(string path) =>
        throw new FarNetUnsupportedApiException(nameof(GetFullPath));

    public override string TempName(string? prefix) =>
        throw new FarNetUnsupportedApiException(nameof(TempName));

    public override IModuleManager GetModuleManager(string name) =>
        throw new FarNetUnsupportedApiException(nameof(GetModuleManager));

    public override CultureInfo GetCurrentUICulture(bool update) =>
        CultureInfo.CurrentUICulture;

    public override void ShowHelp(string path, string topic, HelpOptions options) =>
        throw new FarNetUnsupportedApiException(nameof(ShowHelp));
}

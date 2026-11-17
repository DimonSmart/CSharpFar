using CSharpFar.App.UserMenu;
using CSharpFar.Ui;

namespace CSharpFar.App.Dialogs;

internal sealed record CommandParameterDialogResult(IReadOnlyDictionary<string, string> Values);

internal sealed class CommandParameterDialog
{
    private readonly DialogService _dialogs;
    private readonly FormFieldFactory _fields;

    public CommandParameterDialog(DialogService dialogs, FormFieldFactory fields)
    {
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _fields = fields ?? throw new ArgumentNullException(nameof(fields));
    }

    public CommandParameterDialogResult? Show(string title, IReadOnlyList<CommandTemplateParameter> parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        if (parameters.Count == 0)
            throw new ArgumentException("At least one parameter is required.", nameof(parameters));

        var fields = parameters.Select(parameter =>
            (Parameter: parameter, Field: _fields.Text(new TextFieldOptions(
                parameter.DefaultValue ?? string.Empty,
                SubmitOnEnter: true,
                HistoryValueMode: TextHistoryValueMode.PreserveWhitespace)))).ToArray();

        var rows = new List<FormRow>();
        foreach (var entry in fields)
        {
            if (rows.Count > 0) rows.Add(FormControls.Spacer());
            rows.Add(FormControls.Label(entry.Parameter.Name));
            rows.Add(FormControls.Text(entry.Field));
        }

        var actions = FormControls.Buttons(
            DialogButton.Default("run", "Run", 'R'),
            DialogButton.Cancel());

        return _dialogs.Form(
            new FormDialogOptions(title, PreferredWidth: 64,
                PreferredHeight: Math.Min(30, 9 + 3 * fields.Length), SubmitOnEnter: true)
            {
                ResizeMode = DialogResizeMode.Both,
                InitialFocus = fields[0].Field,
            },
            rows: () => rows,
            footer: () => [actions],
            submit: () =>
            {
                foreach (var entry in fields)
                {
                    if (string.IsNullOrWhiteSpace(entry.Field.Text))
                        return FormSubmit.Invalid<CommandParameterDialogResult>(
                            $"'{entry.Parameter.Name}' is required.", entry.Field);
                }

                return FormSubmit.Success(new CommandParameterDialogResult(
                    fields.ToDictionary(entry => entry.Parameter.Name, entry => entry.Field.Text, StringComparer.Ordinal)));
            });
    }
}

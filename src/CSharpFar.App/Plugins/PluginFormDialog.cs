using CSharpFar.Plugin.Abstractions;
using CSharpFar.Ui;

namespace CSharpFar.App.Plugins;

/// <summary>Adapts the public semantic form to the application's standard modal UI.</summary>
internal static class PluginFormDialog
{
    public static string? Show(DialogService dialogs, FormFieldFactory fields, PluginForm form, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(form);
        ArgumentException.ThrowIfNullOrWhiteSpace(form.Title);
        token.ThrowIfCancellationRequested();
        if (form.PreferredWidth is <= 0 || form.PreferredHeight is <= 0)
            throw new ArgumentException("Preferred form dimensions must be positive.");
        var buttons = form.Buttons.ToArray();
        var allButtons = buttons.Concat(form.Rows.OfType<PluginFormText>().SelectMany(field => field.Actions)).ToArray();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in form.Rows)
            if (!ids.Add(row.Id)) throw new ArgumentException("Duplicate form row ID.");
        foreach (var button in allButtons)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(button.Id);
            ArgumentException.ThrowIfNullOrWhiteSpace(button.Text);
            if (!ids.Add(button.Id) || !Enum.IsDefined(button.Role))
                throw new ArgumentException("Invalid or duplicate form button ID.");
        }
        if (buttons.Count(button => button.IsDefault) != 1 ||
            !buttons.Any(button => button.IsDefault && button.Role == PluginFormButtonRole.Submit && button.Enabled) ||
            !buttons.Any(button => button.Role == PluginFormButtonRole.Cancel && button.Enabled))
            throw new ArgumentException("A form requires one enabled default submit and an enabled cancel button.");
        if (form.Rows.OfType<PluginFormText>().SelectMany(field => field.Actions)
            .Any(button => button.Role != PluginFormButtonRole.Auxiliary || button.IsDefault))
            throw new ArgumentException("Inline buttons must be auxiliary actions.");

        var bindings = form.Rows.Select(row => Bind(fields, row)).ToArray();
        var footerButtons = FormControls.Buttons(buttons.Select(ToButton).ToArray());
        string? error = null;

        IFormFocusTarget? Focus(string? id) => bindings.FirstOrDefault(binding =>
            binding.Model.Id == id && binding.Model is PluginFormField { Visible: true, Enabled: true })?.Focus;

        return dialogs.Form<string?>(
            new FormDialogOptions(form.Title, form.PreferredWidth, form.PreferredHeight)
            {
                InitialFocus = Focus(form.InitialFocusFieldId)
            },
            rows: () => bindings.Where(binding => binding.Model.Visible).Select(binding => binding.Row()).ToArray(),
            footer: () => [FormControls.Label(form.Status), .. FormFooter.ErrorAndButtons(() => error, footerButtons)],
            handle: e =>
            {
                if (e.IsCancelled)
                    return FormDialogOutcome<string?>.Complete(null);

                string? changed = null;
                if (e.IsValueChanged || e.IsSubmitted || e.IsAuxiliary)
                    foreach (var binding in bindings)
                        if (binding.Model.Visible && binding.Pull()) changed ??= binding.Model.Id;

                PluginFormEvent? evt = null;
                if (e.IsSubmitted)
                {
                    string id = e.Command ?? buttons.Single(button => button.IsDefault).Id;
                    if (buttons.Any(button => button.Id == id && button.Enabled && button.Role == PluginFormButtonRole.Submit))
                        evt = new(PluginFormEventKind.Submit, id);
                }
                else if (e.IsAuxiliary && e.Command is { } action &&
                    allButtons.Any(button => button.Id == action && button.Enabled && button.Role == PluginFormButtonRole.Auxiliary))
                    evt = new(PluginFormEventKind.Action, action);
                else if (changed is not null)
                    evt = new(PluginFormEventKind.ValueChanged, changed);

                if (evt is null) return FormDialogOutcome<string?>.Continue();

                token.ThrowIfCancellationRequested();
                var feedback = form.OnEvent?.Invoke(evt);
                token.ThrowIfCancellationRequested();
                foreach (var binding in bindings) binding.Push();
                error = feedback?.Error;
                if (evt.Kind == PluginFormEventKind.Submit && feedback is null)
                    return FormDialogOutcome<string?>.Complete(evt.Id);
                return Focus(feedback?.FocusFieldId) is { } focus
                    ? FormDialogOutcome<string?>.ContinueWithFocus(focus)
                    : FormDialogOutcome<string?>.Continue();
            },
            cancellationToken: token);
    }

    private static DialogButton ToButton(PluginFormButton button) => new(
        button.Id, button.Text, button.HotKey, button.IsDefault, button.Enabled,
        button.Role switch
        {
            PluginFormButtonRole.Submit => DialogButtonRole.Submit,
            PluginFormButtonRole.Auxiliary => DialogButtonRole.Auxiliary,
            PluginFormButtonRole.Cancel => DialogButtonRole.Cancel,
            _ => throw new ArgumentException("Unknown form button role.")
        });

    private static Binding Bind(FormFieldFactory fields, PluginFormRow model)
    {
        switch (model)
        {
            case PluginFormText text:
            {
                var field = fields.Text(new TextFieldOptions(text.Value, MaskInput: text.Masked));
                field.Enabled = text.Enabled;
                FormRow row = text.Actions.Count == 0
                    ? FormControls.Text(text.Label, field)
                    : FormControls.Text(text.Label, field, text.Actions.Select(ToButton).ToArray());
                return new(model, () => row, field,
                    () =>
                    {
                        if (text.Value == field.Text) return false;
                        text.Value = field.Text;
                        return true;
                    },
                    () =>
                    {
                        if (field.Text != text.Value) field.Text = text.Value;
                        field.Enabled = text.Enabled;
                    });
            }
            case PluginFormCheckBox check:
            {
                var row = FormControls.CheckBox(check.Id, check.Label, check.Value, check.Enabled);
                return new(model, () => row, row,
                    () =>
                    {
                        if (check.Value == row.Value) return false;
                        check.Value = row.Value;
                        return true;
                    },
                    () => { row.Value = check.Value; row.Enabled = check.Enabled; });
            }
            case PluginFormSelect select:
            {
                if (select.Options.Count == 0 || select.Options.Any(string.IsNullOrWhiteSpace) ||
                    select.Options.Distinct(StringComparer.Ordinal).Count() != select.Options.Count ||
                    !select.Options.Contains(select.Value, StringComparer.Ordinal))
                    throw new ArgumentException("A form selection must have unique options and a valid selected value.");
                var row = FormControls.Dropdown(select.Id, select.Label, select.Options, value => value,
                    select.Value, StringComparer.Ordinal, select.Enabled);
                return new(model, () => row, row,
                    () =>
                    {
                        if (select.Value == row.Value) return false;
                        select.Value = row.Value;
                        return true;
                    },
                    () =>
                    {
                        if (!select.Options.Contains(select.Value, StringComparer.Ordinal))
                            throw new ArgumentException("Invalid form selection.");
                        if (row.Value != select.Value) row.Value = select.Value;
                        row.Enabled = select.Enabled;
                    });
            }
            case PluginFormLabel label:
                return new(model, () => FormControls.Label(label.Text), null, () => false, () => { });
            case PluginFormSeparator:
                return new(model, FormControls.Separator, null, () => false, () => { });
            default:
                throw new ArgumentException("Unsupported plugin form row.");
        }
    }

    private sealed record Binding(PluginFormRow Model, Func<FormRow> Row, IFormFocusTarget? Focus,
        Func<bool> Pull, Action Push);
}

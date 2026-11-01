using System.ComponentModel;

namespace Futureplace.AgenteFS80H.Desktop;

/// <summary>Pedido de confirmação forte: a pessoa lê as consequências e digita uma palavra (LIMPAR ou TROCAR).</summary>
internal sealed record TypedConfirmation(string Title, string Message, string Word, string ConfirmText)
{
    public const string ClearWord = "LIMPAR";
    public const string ReplaceWord = "TROCAR";

    /// <summary>Aceita a palavra com ou sem maiúsculas e espaços em volta; nada além dela.</summary>
    public static bool Matches(string? typed, string word) =>
        !string.IsNullOrEmpty(word) && string.Equals(typed?.Trim(), word, StringComparison.OrdinalIgnoreCase);

    public static TypedConfirmation ClearPairing(string deviceName) => new(
        "Limpar pareamento e digitais",
        "Limpar o pareamento apaga as digitais guardadas neste computador (a cópia protegida que ele usa para comparar rápido) e desliga " + DeviceLabel(deviceName) + " na Central. " +
        "Este computador para de identificar as pessoas até ser pareado de novo. A Central guarda a cópia principal das digitais: o que ela já tem volta sozinho quando este leitor for pareado outra vez.\n\n" +
        $"Para confirmar, digite {ClearWord} abaixo.",
        ClearWord, "Limpar tudo");

    public static TypedConfirmation ReplacePairing(string deviceName) => new(
        "Trocar o pareamento",
        "Este computador já está ligado à Central" + (string.IsNullOrWhiteSpace(deviceName) ? "" : $" como «{deviceName}»") + ". " +
        "Trocar o pareamento apaga a cópia das digitais guardada neste computador e desliga o leitor antigo na Central. " +
        "A Central guarda a cópia principal: as digitais que ela já tem voltam sozinhas para o leitor pareado; só quem ainda não tem digital na Central precisa cadastrar de novo.\n\n" +
        $"Para continuar, digite {ReplaceWord} abaixo.",
        ReplaceWord, "Trocar pareamento");

    private static string DeviceLabel(string deviceName) =>
        string.IsNullOrWhiteSpace(deviceName) ? "este leitor" : $"o leitor «{deviceName}»";
}

internal sealed class TypedConfirmationDialog : Form
{
    private readonly string word;
    private readonly TextBox input = new() { Name = "confirmationInput", AccessibleName = "Palavra de confirmação", MaxLength = 32 };
    private readonly CentralButton confirm;
    private readonly CentralButton cancel = new("Cancelar") { Name = "confirmationCancel" };

    public TypedConfirmationDialog(TypedConfirmation request)
    {
        word = request.Word;
        confirm = new CentralButton(request.ConfirmText, ButtonTone.Danger) { Name = "confirmationAccept", Enabled = false };
        SuspendLayout();
        Name = "typedConfirmation";
        Text = request.Title;
        Icon = CentralTheme.AppIcon();
        Font = CentralTheme.Body(10.5F);
        BackColor = CentralTheme.Surface;
        ForeColor = CentralTheme.Text;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96, 96);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(24);

        var stack = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, Dock = DockStyle.Fill };
        stack.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var title = new Label
        {
            Text = request.Title, AutoSize = true, UseCompatibleTextRendering = true, Font = CentralTheme.Heading(15),
            ForeColor = CentralTheme.Danger, MaximumSize = new Size(480, 0), Margin = new Padding(0, 0, 0, 12)
        };
        var message = new Label
        {
            Name = "confirmationMessage", Text = request.Message, AutoSize = true, UseCompatibleTextRendering = true,
            Font = CentralTheme.Body(11), MaximumSize = new Size(480, 0), Margin = new Padding(0, 0, 0, 12)
        };
        input.Font = CentralTheme.Body(13);
        input.CharacterCasing = CharacterCasing.Upper;
        input.Width = 220;
        input.Margin = new Padding(0, 0, 0, 16);
        input.PlaceholderText = request.Word;
        input.TextChanged += (_, _) => confirm.Enabled = TypedConfirmation.Matches(input.Text, request.Word);
        confirm.Click += (_, _) => Accept();
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        buttons.Controls.Add(confirm);
        buttons.Controls.Add(cancel);
        stack.Controls.Add(title, 0, 0);
        stack.Controls.Add(message, 0, 1);
        stack.Controls.Add(input, 0, 2);
        stack.Controls.Add(buttons, 0, 3);
        Controls.Add(stack);
        AcceptButton = confirm;
        CancelButton = cancel;
        ResumeLayout(true);
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal string TypedText { get => input.Text; set => input.Text = value; }

    internal bool CanConfirm => confirm.Enabled;

    /// <summary>Confirma como o botão; com a palavra errada não faz nada.</summary>
    internal bool Accept()
    {
        if (!TypedConfirmation.Matches(input.Text, word)) return false;
        DialogResult = DialogResult.OK;
        if (Visible) Close();
        return true;
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        input.Focus();
    }

    public static bool Ask(IWin32Window owner, TypedConfirmation request)
    {
        using var dialog = new TypedConfirmationDialog(request);
        return dialog.ShowDialog(owner) == DialogResult.OK && TypedConfirmation.Matches(dialog.TypedText, request.Word);
    }

    protected override void Dispose(bool disposing)
    {
        var icon = disposing ? Icon : null;
        base.Dispose(disposing);
        if (disposing) icon?.Dispose();
    }
}

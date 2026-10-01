using System;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Traduce
{
    internal sealed class SettingsForm : Form
    {
        private readonly Settings draft;
        private readonly ComboBox provider = new ComboBox(), model = new ComboBox(), shortcut = new ComboBox();
        private readonly ComboBox language = new ComboBox();
        private readonly TextBox key = new TextBox(), endpoint = new TextBox(), path = new TextBox();
        private readonly Label hint = new Label(), status = new Label(), keyLabel = new Label();
        private readonly FlowLayoutPanel cliActions = new FlowLayoutPanel(), keyRow = new FlowLayoutPanel(), modelRow = new FlowLayoutPanel();
        private readonly TableLayoutPanel layout = new TableLayoutPanel();
        private readonly Panel apiSection = new Panel(), advancedSection = new Panel();
        private readonly Label advancedLabel = new Label();
        private readonly Button login = new Button(), install = new Button(), check = new Button(), loadModels = new Button(), save = new Button(), forget = new Button();
        private readonly LinkLabel help = new LinkLabel();
        private string selected;
        private bool busy;
        private CancellationTokenSource operation;
        public Settings Configuration { get { return draft; } }
        public int Shortcut { get { return shortcut.SelectedIndex; } }

        public SettingsForm(Settings settings)
        {
            draft = settings.Clone();
            L.Language = L.Normalize(settings.Language);
            Icon = Brand.Icon();
            Text = L.T("Conexión · Traduce"); ClientSize = new Size(630, 575);
            AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 10); StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
            AutoScroll = true; BackColor = Color.FromArgb(246, 248, 251);
            layout.Dock = DockStyle.Fill; layout.Padding = new Padding(20); layout.ColumnCount = 1;
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); layout.AutoScroll = true;
            var frame = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
            frame.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            frame.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            frame.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
            frame.Controls.Add(layout, 0, 0); Controls.Add(frame);
            Add(new Label { Text = L.T("Conecta tu IA"), Font = new Font("Segoe UI", 19, FontStyle.Bold), AutoSize = true });
            Add(new Label { Text = L.T("Elige cómo traducir. Alt+T seguirá funcionando en cualquier programa."), AutoSize = true, Margin = new Padding(3, 5, 3, 12) });
            provider.DropDownStyle = ComboBoxStyle.DropDownList; provider.Dock = DockStyle.Fill;
            provider.Items.AddRange(Providers.All); provider.Name = "Provider"; Add(provider);
            hint.Height = 58; hint.Dock = DockStyle.Fill; hint.Margin = new Padding(3, 8, 3, 3); Add(hint);
            cliActions.AutoSize = true; cliActions.Dock = DockStyle.Fill;
            Configure(login, L.T("Iniciar sesión oficial"), 170); Configure(install, L.T("Instalar cliente"), 135); Configure(check, L.T("Comprobar"), 110);
            cliActions.Controls.AddRange(new Control[] { login, install, check }); Add(cliActions);
            apiSection.Height = 70; apiSection.Dock = DockStyle.Fill;
            keyLabel.Dock = DockStyle.Top; keyLabel.Height = 24;
            keyRow.Dock = DockStyle.Bottom; keyRow.Height = 38;
            key.Width = 425; key.UseSystemPasswordChar = true; key.Name = "ApiKey";
            Configure(forget, L.T("Borrar clave"), 115); keyRow.Controls.AddRange(new Control[] { key, forget });
            apiSection.Controls.Add(keyRow); apiSection.Controls.Add(keyLabel); Add(apiSection);
            help.AutoSize = true; help.Margin = new Padding(3, 4, 3, 12); Add(help);
            Add(new Label { Text = L.T("Modelo · puedes escribir otro de tu cuenta"), AutoSize = true });
            modelRow.AutoSize = true; modelRow.Dock = DockStyle.Fill;
            model.Width = 380; model.Name = "Model"; Configure(loadModels, L.T("Cargar modelos"), 150);
            modelRow.Controls.AddRange(new Control[] { model, loadModels }); Add(modelRow);
            advancedSection.Height = 56; advancedSection.Dock = DockStyle.Fill;
            advancedLabel.Dock = DockStyle.Top; advancedLabel.Height = 24;
            endpoint.Dock = DockStyle.Bottom; path.Dock = DockStyle.Bottom;
            endpoint.Name = "Endpoint"; path.Name = "CliPath";
            advancedSection.Controls.Add(advancedLabel); advancedSection.Controls.Add(endpoint); advancedSection.Controls.Add(path); Add(advancedSection);
            var shortcutRow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(3, 10, 3, 3) };
            shortcutRow.Controls.Add(new Label { Text = L.T("Atajo"), AutoSize = true, Padding = new Padding(0, 5, 15, 0) });
            shortcut.DropDownStyle = ComboBoxStyle.DropDownList; shortcut.Width = 180; shortcut.Items.AddRange(MainForm.Shortcuts);
            shortcut.SelectedIndex = settings.Shortcut >= 0 && settings.Shortcut < MainForm.Shortcuts.Length ? settings.Shortcut : 0;
            shortcutRow.Controls.Add(shortcut); Add(shortcutRow);
            var languageRow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(3, 8, 3, 3) };
            languageRow.Controls.Add(new Label { Text = L.T("Idioma de la interfaz"), AutoSize = true, Padding = new Padding(0, 5, 15, 0) });
            language.DropDownStyle = ComboBoxStyle.DropDownList; language.Name = "InterfaceLanguage"; language.Width = 180;
            language.Items.AddRange(new object[] { "Español", "English" }); language.SelectedIndex = settings.Language == "en" ? 1 : 0;
            languageRow.Controls.Add(language); Add(languageRow);
            status.Height = 52; status.Dock = DockStyle.Fill; status.ForeColor = Color.FromArgb(50, 70, 95); status.Margin = new Padding(3, 9, 3, 3); Add(status);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(12, 6, 12, 8) };
            var close = new Button { Text = L.T("Cerrar"), Width = 110, Height = 34, DialogResult = DialogResult.Cancel };
            Configure(save, L.T("Guardar"), 130); save.Name = "SaveConnection";
            buttons.Controls.Add(close); buttons.Controls.Add(save);
            frame.Controls.Add(buttons, 0, 1);
            CancelButton = close; AcceptButton = save;
            provider.SelectedIndexChanged += delegate { if (selected != null) SaveDraft(); LoadProvider(); };
            forget.Click += delegate { draft.Profile(selected).ProtectedKey = null; key.Clear(); UpdateKeyLabel(); };
            help.LinkClicked += delegate { Process.Start(new ProcessStartInfo(Providers.Get(selected).HelpUrl) { UseShellExecute = true }); };
            login.Click += async delegate { await RunOperation(async token => { await CliConnections.Login(Current(), token); return await CliConnections.Status(Current(), token); }, L.T("Completa el acceso en el navegador oficial. Puedes cerrar esta ventana para cancelar.")); };
            install.Click += async delegate { await RunOperation(async token => { await CliConnections.Install(Current(), token); return await CliConnections.Status(Current(), token); }, L.T("Instalando el cliente oficial con WinGet…")); };
            check.Click += async delegate { await RunOperation(token => CliConnections.Status(Current(), token), L.T("Comprobando el cliente y la sesión…")); };
            loadModels.Click += async delegate { await RunOperation(async token => {
                var names = await new ApiTranslator().Models(Current(), token);
                string previous = model.Text; model.Items.Clear(); model.Items.AddRange(names); model.Text = previous;
                return names.Length == 0 ? L.T("La conexión respondió, pero no anunció modelos. Escribe el identificador manualmente.") : names.Length + L.T(" modelos disponibles. Elige uno con visión si vas a traducir imágenes.");
            }, L.T("Consultando los modelos de tu cuenta…")); };
            save.Click += async delegate { await RunOperation(async token => {
                var settingsToSave = Current(); Providers.Validate(settingsToSave);
                if (Providers.Get(selected).IsCli) await CliConnections.Resolve(settingsToSave, token);
                draft.ConfigurationComplete = true; DialogResult = DialogResult.OK; return L.T("Conexión guardada.");
            }, L.T("Guardando…")); };
            FormClosing += delegate { if (operation != null) operation.Cancel(); };
            provider.SelectedItem = Providers.All.FirstOrDefault(p => p.Id == draft.Provider) ?? Providers.All[0];
        }
        private static void Configure(Button button, string text, int width) { button.Text = text; button.Width = width; button.Height = 32; }
        private void Add(Control control)
        {
            int row = layout.RowCount++; layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.Controls.Add(control, 0, row);
        }
        private void UpdateKeyLabel() { keyLabel.Text = string.IsNullOrEmpty(draft.Profile(selected).ProtectedKey) ? L.T("Clave API · se guarda protegida para tu usuario de Windows") : L.T("Clave guardada · deja el campo vacío para conservarla"); }
        private void SaveDraft()
        {
            var profile = draft.Profile(selected);
            string newEndpoint = endpoint.Text.Trim();
            if (selected == "custom" && !string.Equals((profile.BaseUrl ?? "").TrimEnd('/'), newEndpoint.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
            {
                // Credentials belong to the configured endpoint, never a newly typed server.
                profile.ProtectedKey = null;
            }
            profile.Model = model.Text.Trim(); profile.CliPath = path.Text.Trim(); profile.BaseUrl = newEndpoint;
            if (key.Text.Length > 0) { profile.SetKey(key.Text); key.Clear(); }
            UpdateKeyLabel();
        }
        private Settings Current() { SaveDraft(); draft.Provider = selected; draft.Shortcut = Shortcut; draft.Language = language.SelectedIndex == 1 ? "en" : "es"; return draft; }
        private void LoadProvider()
        {
            var choice = (ProviderDefinition)provider.SelectedItem; selected = choice.Id; draft.Provider = selected;
            var profile = draft.Profile(selected);
            hint.Text = L.T(choice.Hint); cliActions.Visible = choice.IsCli; apiSection.Visible = !choice.IsCli && selected != "ollama";
            help.Text = choice.IsCli ? L.T("Ayuda e instalación oficial") : selected == "ollama" ? L.T("Instalar Ollama") : L.T("Obtener una clave / abrir mi cuenta");
            loadModels.Visible = !choice.IsCli;
            model.Items.Clear(); if (choice.DefaultModel.Length > 0) model.Items.Add(choice.DefaultModel);
            model.Text = string.IsNullOrWhiteSpace(profile.Model) ? choice.DefaultModel : profile.Model;
            key.Clear(); UpdateKeyLabel(); path.Text = profile.CliPath ?? ""; endpoint.Text = profile.BaseUrl ?? "";
            advancedSection.Visible = choice.IsCli || selected == "custom";
            advancedLabel.Text = choice.IsCli ? L.T("Ruta del cliente .exe · opcional, detección automática") : L.T("URL base de tu API compatible");
            path.Visible = choice.IsCli; endpoint.Visible = !choice.IsCli;
            status.Text = choice.IsCli ? L.T("Si ya tienes sesión en el cliente oficial, basta con Guardar. Comprobar no consume una traducción.") : L.T("Puedes cargar modelos para comprobar la clave. Solo se envía contenido cuando pides traducir.");
        }
        private async Task RunOperation(Func<CancellationToken, Task<string>> action, string message)
        {
            if (busy) return;
            busy = true; operation = new CancellationTokenSource(); var current = operation;
            SetEnabled(false); status.Text = message;
            try { string result = await action(current.Token); if (!IsDisposed) status.Text = result; }
            catch (OperationCanceledException) { if (!IsDisposed) status.Text = L.T("Operación cancelada."); }
            catch (Exception e) { if (!IsDisposed) status.Text = e.Message; }
            finally { current.Dispose(); if (operation == current) operation = null; busy = false; if (!IsDisposed) SetEnabled(true); }
        }
        private void SetEnabled(bool enabled)
        {
            foreach (Control control in new Control[] { provider, model, key, endpoint, path, shortcut, language, login, install, check, loadModels, save, forget }) control.Enabled = enabled;
        }
    }
}

using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8")]

namespace Traduce
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            bool created;
            using (var mutex = new Mutex(true, "Local\\Traduce.Desktop", out created))
            using (var ready = new EventWaitHandle(false, EventResetMode.ManualReset, "Local\\Traduce.Ready"))
            {
                if (!created)
                {
                    if (Array.IndexOf(args, "--background") >= 0) return;
                    // WinForms can recreate the HWND while constructing controls.
                    // Only send requests once the main message loop is initialized.
                    ready.WaitOne(5000);
                    IntPtr window = Native.FindWindow(null, "Traduce");
                    if (window != IntPtr.Zero) Native.PostMessage(window, Array.IndexOf(args, "--quit") >= 0 ? Native.ExitMessage : Native.ShowMessage, IntPtr.Zero, IntPtr.Zero);
                    return;
                }
                if (Array.IndexOf(args, "--quit") >= 0) return;
                ready.Reset();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e) {
                    MessageBox.Show("No se ha podido completar la acción. " + e.Exception.Message, "Traduce", MessageBoxButtons.OK, MessageBoxIcon.Information);
                };
                using (var main = new MainForm())
                {
                    bool firstIdle = true;
                    EventHandler signalReady = delegate {
                        ready.Set();
                        if (firstIdle)
                        {
                            firstIdle = false;
                            if (main.NeedsSetup || Array.IndexOf(args, "--setup") >= 0) main.BeginInvoke((Action)main.EditSettings);
                        }
                    };
                    Application.Idle += signalReady;
                    try
                    {
                        if (Array.IndexOf(args, "--background") >= 0)
                        {
                            // Create the hidden window to register Alt+T, then run
                            // the message loop without showing a startup window.
                            IntPtr hiddenHandle = main.Handle;
                            main.FormClosed += delegate { Application.ExitThread(); };
                            Application.Run();
                        }
                        else Application.Run(main);
                    }
                    finally { Application.Idle -= signalReady; }
                }
            }
        }
    }

    internal sealed class TranslationInput
    {
        public string Text;
        public ImageInput Image;
        public string OcrReason;
        public long OcrMilliseconds;
    }

    internal sealed class MainForm : Form
    {
        private readonly Color ink = Color.FromArgb(231, 237, 245);
        private readonly Color muted = Color.FromArgb(162, 175, 193);
        private readonly Color panel = Color.FromArgb(27, 36, 52);
        private readonly TextBox source = new TextBox();
        private readonly TextBox target = new TextBox();
        private readonly Button translate = new Button();
        private readonly Button cancel = new Button();
        private readonly Button copy = new Button();
        private readonly Button paste = new Button();
        private readonly Button settingsButton = new Button();
        private readonly Button edit = new Button();
        private readonly Button imageButton = new Button();
        private readonly Translator translator = new Translator();
        private readonly TableLayoutPanel root = new TableLayoutPanel();
        private readonly FlowLayoutPanel actions = new FlowLayoutPanel();
        private readonly ToolTip help = new ToolTip();
        private readonly NotifyIcon tray;
        private Settings settings;
        internal bool NeedsSetup { get; private set; }
        private readonly CaptureLog captureLog;
        private CancellationTokenSource cancellation;
        private bool quitting, expanded, selecting, logFailureReported, positioning;
        private Rectangle? popupAnchor;
        private int requestVersion, activeRequests;
        private IntPtr lastSource;
        private bool registered;
        internal bool HotkeyActive { get { return registered; } }
        internal static readonly string[] Shortcuts = { "Alt + T", "Ctrl + Alt + T", "Ctrl + Shift + F9" };

        public MainForm() : this(null) { }

        internal MainForm(Settings overrides) : this(overrides, new CaptureLog(CaptureLog.DefaultFolder)) { }

        internal MainForm(Settings overrides, CaptureLog log)
        {
            captureLog = log;
            settings = overrides ?? Settings.Load();
            NeedsSetup = overrides == null && !settings.ConfigurationComplete;
            if (settings.Shortcut < 0 || settings.Shortcut >= Shortcuts.Length) settings.Shortcut = 0;
            Text = "Traduce";
            ClientSize = new Size(420, 280);
            MinimumSize = new Size(416, 270);
            MaximizeBox = false;
            TopMost = true;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(16, 23, 36);
            ForeColor = ink;
            Font = new Font("Segoe UI", 10);
            AutoScaleDimensions = new SizeF(96, 96);
            AutoScaleMode = AutoScaleMode.Dpi;
            Icon = MakeIcon();
            root.Dock = DockStyle.Fill; root.ColumnCount = 1; root.RowCount = 4; root.Padding = new Padding(6);
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(root);
            source.Name = "Original"; target.Name = "Translation";
            ConfigureText(source, false); source.Visible = false; root.Controls.Add(source, 0, 0);
            actions.Dock = DockStyle.Fill; actions.Padding = new Padding(0, 4, 0, 0); actions.Visible = false;
            ConfigureButton(translate, "Traducir", true); translate.Width = 96;
            actions.Controls.Add(translate); root.Controls.Add(actions, 0, 1);
            ConfigureText(target, true); root.Controls.Add(target, 0, 2);
            var bottom = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(0, 5, 0, 0), WrapContents = true, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
            ConfigureButton(copy, "Copiar", true); copy.Width = 68; copy.Enabled = false;
            ConfigureButton(paste, "Pegar", false); paste.Width = 60; paste.Name = "Paste";
            ConfigureButton(imageButton, "Imagen", false); imageButton.Width = 70;
            ConfigureButton(edit, "Texto", false); edit.Width = 60; edit.Name = "EditOriginal";
            ConfigureButton(settingsButton, "Ajustes", false); settingsButton.Width = 68;
            ConfigureButton(cancel, "Cancelar", false); cancel.Width = 74; cancel.Visible = false;
            bottom.Controls.AddRange(new Control[] { copy, paste, imageButton, edit, settingsButton, cancel }); root.Controls.Add(bottom, 0, 3);
            help.SetToolTip(paste, "Pegar y traducir texto o una imagen del portapapeles.");
            help.SetToolTip(imageButton, "Abrir una imagen. Alt+T permite recortar una zona de la pantalla.");

            var menu = new ContextMenuStrip();
            menu.Items.Add("Abrir Traduce", null, delegate { Reveal(); });
            menu.Items.Add("Seleccionar región", null, delegate { CaptureSelection(); });
            menu.Items.Add("Pegar y traducir", null, async delegate { Reveal(); await PasteAndTranslate(); });
            menu.Items.Add("Abrir imagen", null, async delegate { Reveal(); await OpenImage(); });
            menu.Items.Add("Ajustes", null, delegate { Reveal(); EditSettings(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Salir", null, delegate { Quit(); });
            tray = new NotifyIcon { Icon = Icon, Text = "Traduce · " + Shortcuts[settings.Shortcut], ContextMenuStrip = menu, Visible = true };
            tray.DoubleClick += delegate { Reveal(); };
            tray.BalloonTipClicked += delegate { Reveal(); };
            translate.Click += async delegate { await TranslateText(); };
            paste.Click += async delegate { await PasteAndTranslate(); };
            imageButton.Click += async delegate { await OpenImage(); };
            cancel.Click += delegate { if (cancellation != null) cancellation.Cancel(); };
            copy.Click += delegate { try { Clipboard.SetText(target.Text); SetStatus("Traducción copiada.", false); } catch (System.Runtime.InteropServices.ExternalException) { SetStatus("El portapapeles está ocupado. Inténtalo de nuevo.", true); } };
            edit.Click += delegate { SetExpanded(!expanded); if (expanded) source.Focus(); };
            settingsButton.Click += delegate { EditSettings(); };
            source.TextChanged += delegate { if (cancellation == null) { target.Clear(); copy.Enabled = false; } };
            KeyPreview = true;
            KeyDown += async delegate(object sender, KeyEventArgs e) {
                if (e.Control && e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await TranslateText(); }
                if (e.Control && e.KeyCode == Keys.V && !source.Focused) { e.SuppressKeyPress = true; await PasteAndTranslate(); }
                if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; HideToTray(); }
            };
            Shown += delegate { if (!registered) SetStatus("El atajo está ocupado. Elige otro en Ajustes; puedes usar Pegar.", true); };
        }

        private static Icon MakeIcon()
        {
            using (var bitmap = new Bitmap(32, 32))
            using (var graphics = Graphics.FromImage(bitmap))
            using (var brush = new SolidBrush(Color.FromArgb(104, 226, 195)))
            using (var font = new Font("Segoe UI", 19, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                graphics.Clear(Color.FromArgb(16, 23, 36)); graphics.FillEllipse(brush, 1, 1, 30, 30);
                graphics.DrawString("T", font, Brushes.Black, 7, 3);
                IntPtr handle = bitmap.GetHicon();
                try { using (var icon = Icon.FromHandle(handle)) return (Icon)icon.Clone(); }
                finally { DestroyIcon(handle); }
            }
        }
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);

        private void SetExpanded(bool value)
        {
            if (expanded == value) return;
            expanded = value;
            float scale;
            using (var metrics = CreateGraphics()) scale = metrics.DpiY / 96f;
            root.SuspendLayout();
            root.RowStyles[0].Height = value ? 90 * scale : 0;
            root.RowStyles[1].Height = value ? 36 * scale : 0;
            source.Visible = actions.Visible = value;
            edit.Text = value ? "Ocultar" : "Texto";
            ClientSize = new Size(ClientSize.Width, ClientSize.Height + (int)((value ? 126 : -126) * scale));
            root.ResumeLayout();
            if (Visible && popupAnchor.HasValue) PositionResult();
        }
        private void ConfigureText(TextBox box, bool readOnly)
        {
            box.Multiline = true; box.ReadOnly = readOnly; box.ScrollBars = ScrollBars.Vertical;
            box.Dock = DockStyle.Fill; box.BorderStyle = BorderStyle.FixedSingle; box.BackColor = panel;
            box.ForeColor = ink; box.Font = new Font("Segoe UI", 11); box.MaxLength = 0;
            box.AcceptsReturn = true; box.HideSelection = false;
        }
        private void ConfigureButton(Button button, string text, bool primary)
        {
            button.Text = text; button.Height = 28; button.Width = 74; button.FlatStyle = FlatStyle.Flat; button.Font = new Font("Segoe UI", 9);
            button.FlatAppearance.BorderColor = Color.FromArgb(53, 66, 85);
            button.BackColor = primary ? Color.FromArgb(104, 226, 195) : panel;
            button.ForeColor = primary ? Color.FromArgb(16, 23, 36) : ink;
            button.Cursor = Cursors.Hand; button.Margin = new Padding(0, 0, 6, 0);
        }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); registered = Register(settings.Shortcut); }
        protected override void OnHandleDestroyed(EventArgs e) { Native.UnregisterHotKey(Handle, Native.HotkeyId); base.OnHandleDestroyed(e); }
        private bool Register(int index)
        {
            uint modifier = index == 2 ? 6u : index == 1 ? 3u : 1u;
            uint key = index == 2 ? (uint)Keys.F9 : (uint)Keys.T;
            return Native.RegisterHotKey(Handle, Native.HotkeyId, modifier | 0x4000u, key);
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0312 && m.WParam.ToInt32() == Native.HotkeyId) { CaptureSelection(); return; }
            if (m.Msg == Native.ShowMessage) { Reveal(); return; }
            if (m.Msg == Native.ExitMessage) { Quit(); return; }
            base.WndProc(ref m);
        }
        private async void CaptureSelection()
        {
            if (selecting || quitting) return;
            if (NeedsSetup) { EditSettings(); if (NeedsSetup) return; }
            IntPtr selectedWindow = Native.GetForegroundWindow();
            if (selectedWindow == Handle)
            {
                if (Native.IsWindow(lastSource)) { Native.SetForegroundWindow(lastSource); selectedWindow = lastSource; }
            }
            else lastSource = selectedWindow;
            var trace = new CaptureTrace { ActiveApplication = SourceApplication.FromWindow(selectedWindow) };
            SetExpanded(false);
            Hide(); selecting = true;
            await Submit(async token => {
                RegionCapture crop;
                try { crop = await RegionPicker.CaptureRegion(token); }
                finally
                {
                    selecting = false;
                    if (Native.IsWindow(selectedWindow) && selectedWindow != Handle) Native.SetForegroundWindow(selectedWindow);
                }
                using (crop)
                {
                    token.ThrowIfCancellationRequested();
                    trace.Region = crop.Bounds;
                    trace.RegionApplication = SourceApplication.AtCenter(crop.Bounds);
                    var screen = Screen.FromRectangle(crop.Bounds);
                    trace.Monitor = screen.DeviceName; trace.WorkingArea = screen.WorkingArea;
                    target.Text = "Leyendo…"; RevealNear(crop.Bounds); trace.Popup = Bounds;
                    var image = ImageInput.FromImage(crop.Image);
                    trace.OcrReason = "pending";
                    var ocrWatch = Stopwatch.StartNew();
                    try { return await RegionInput.FromImage(image, token); }
                    finally { trace.OcrMilliseconds = ocrWatch.ElapsedMilliseconds; }
                }
            }, true, trace);
        }

        private Task TranslateText()
        {
            string text = source.Text;
            return Submit(token => Task.FromResult(new TranslationInput { Text = text }), false);
        }

        internal async Task Submit(Func<CancellationToken, Task<TranslationInput>> acquire, bool passive, CaptureTrace trace = null)
        {
            if (quitting) return;
            int version = ++requestVersion;
            if (cancellation != null) cancellation.Cancel();
            var current = new CancellationTokenSource();
            cancellation = current; activeRequests++;
            SetBusy(true);
            help.SetToolTip(target, null);
            target.ForeColor = muted; target.Text = passive ? "Capturando…" : "Preparando…";
            var elapsed = Stopwatch.StartNew();
            var translationTime = new Stopwatch();
            string outcome = "error", errorType = null;
            try
            {
                TranslationInput input = await acquire(current.Token);
                current.Token.ThrowIfCancellationRequested();
                if (trace != null)
                {
                    trace.Provider = settings.Provider;
                    trace.Model = Providers.Model(settings);
                    trace.Mode = input.Image == null ? "text" : "image";
                    trace.Characters = input.Text == null ? 0 : input.Text.Length;
                    trace.OcrReason = input.OcrReason ?? "not_requested";
                    trace.OcrMilliseconds = input.OcrMilliseconds;
                    WriteCaptureLog(trace, "capture", "ready", elapsed.ElapsedMilliseconds);
                }
                source.Text = input.Text ?? "";
                target.Text = "Traduciendo…";
                translationTime.Start();
                string result = input.Image == null
                    ? await translator.Translate(input.Text, settings, current.Token)
                    : await translator.TranslateImage(input.Image, settings, current.Token);
                current.Token.ThrowIfCancellationRequested();
                outcome = "success";
                if (version == requestVersion && !quitting)
                {
                    target.ForeColor = ink; target.Text = result; copy.Enabled = true;
                    if (!Visible) tray.ShowBalloonTip(1800, "Traduce", "Tu traducción está lista.", ToolTipIcon.Info);
                }
            }
            catch (OperationCanceledException)
            {
                outcome = "cancelled";
                if (trace != null && trace.OcrReason == "pending") trace.OcrReason = "cancelled";
                if (version == requestVersion && !quitting) { target.Text = "Traducción cancelada."; target.ForeColor = muted; }
            }
            catch (Exception e)
            {
                errorType = e.GetType().Name;
                if (version == requestVersion && !quitting) { SetStatus(e.Message, true); if (passive) Reveal(false); }
            }
            finally
            {
                if (trace != null)
                {
                    trace.TranslationMilliseconds = translationTime.ElapsedMilliseconds;
                    WriteCaptureLog(trace, "finished", outcome, elapsed.ElapsedMilliseconds, errorType);
                }
                if (cancellation == current) { cancellation = null; if (!quitting) SetBusy(false); }
                current.Dispose(); activeRequests--;
                if (quitting && activeRequests == 0) Close();
            }
        }

        private void WriteCaptureLog(CaptureTrace trace, string phase, string outcome, long elapsed, string errorType = null)
        {
            if (captureLog.Append(trace, phase, outcome, elapsed, errorType) || logFailureReported) return;
            logFailureReported = true;
            tray.ShowBalloonTip(3000, "Traduce", "No se ha podido escribir el registro local de capturas. Comprueba el espacio y los permisos de la carpeta de Traduce.", ToolTipIcon.Warning);
        }

        internal void RevealNear(Rectangle selection)
        {
            IntPtr foreground = Native.GetForegroundWindow();
            popupAnchor = selection; StartPosition = FormStartPosition.Manual;
            if (WindowState != FormWindowState.Normal) WindowState = FormWindowState.Normal;
            PositionResult(); Reveal(false); PositionResult();
            // WinForms may activate a form while applying its first DPI change.
            if (Native.GetForegroundWindow() == Handle && Native.IsWindow(foreground) && foreground != Handle)
                Native.SetForegroundWindow(foreground);
            Native.SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0, 0x0013); // TOPMOST without taking focus.
        }

        private void PositionResult()
        {
            if (!popupAnchor.HasValue || positioning) return;
            positioning = true;
            try
            {
                // Moving to another display can resize the form synchronously for
                // its DPI. Recalculate using the resulting outer window size.
                for (int pass = 0; pass < 2; pass++)
                {
                    Rectangle work = Screen.FromRectangle(popupAnchor.Value).WorkingArea;
                    MinimumSize = new Size(1, Math.Min(270 * DeviceDpi / 96, work.Height));
                    Bounds = PopupPlacement.Calculate(popupAnchor.Value, Size, work, Math.Max(8, 10 * DeviceDpi / 96));
                }
            }
            finally { positioning = false; }
        }

        private void SetBusy(bool busy)
        {
            if (busy) copy.Enabled = false;
            source.ReadOnly = busy; translate.Enabled = !busy; paste.Enabled = !busy;
            settingsButton.Visible = !busy; edit.Enabled = !busy; imageButton.Enabled = !busy; cancel.Visible = busy;
        }

        private Task PasteAndTranslate()
        {
            return Submit(token => {
                if (Native.IsClipboardFormatAvailable(2) || Native.IsClipboardFormatAvailable(8) || Native.IsClipboardFormatAvailable(17))
                {
                    using (var image = Clipboard.GetImage()) return Task.FromResult(new TranslationInput { Image = ImageInput.FromImage(image) });
                }
                if (!Native.IsClipboardFormatAvailable(13)) throw new ArgumentException("El portapapeles no contiene texto ni una imagen. Haz un recorte con Win+Shift+S o usa Imagen.");
                return Task.FromResult(new TranslationInput { Text = ClipboardAccess.ReadText() });
            }, false);
        }

        private async Task OpenImage()
        {
            using (var picker = new OpenFileDialog { Title = "Traducir una imagen", Filter = "Imágenes|*.png;*.jpg;*.jpeg;*.bmp", CheckFileExists = true })
            {
                if (picker.ShowDialog(this) != DialogResult.OK) return;
                await Submit(token => Task.FromResult(new TranslationInput { Image = ImageInput.FromFile(picker.FileName) }), false);
            }
        }

        private void SetStatus(string text, bool error)
        {
            help.SetToolTip(target, text);
            if (error) { target.Text = text; target.ForeColor = Color.FromArgb(255, 171, 150); copy.Enabled = false; }
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        internal void Reveal(bool activate = true)
        {
            if (WindowState != FormWindowState.Normal) WindowState = FormWindowState.Normal;
            Show();
            if (activate) { Activate(); Native.SetForegroundWindow(Handle); }
            else Native.SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0, 0x0013); // TOPMOST, no activation/move/resize
        }
        private void HideToTray() { Hide(); }
        internal void EditSettings()
        {
            if (cancellation != null) return;
            using (var dialog = new SettingsForm(settings))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                Native.UnregisterHotKey(Handle, Native.HotkeyId);
                if (!Register(dialog.Shortcut))
                {
                    registered = Register(settings.Shortcut); SetStatus("Ese atajo está ocupado. Elige otra combinación.", true); return;
                }
                registered = true;
                settings = dialog.Configuration; NeedsSetup = false;
                settings.Save(); tray.Text = "Traduce · " + Shortcuts[settings.Shortcut];
                SetStatus("Ajustes guardados.", false);
            }
        }
        internal void Quit()
        {
            quitting = true;
            if (activeRequests > 0) { if (cancellation != null) cancellation.Cancel(); Hide(); } else Close();
        }
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!quitting && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; HideToTray(); return; }
            quitting = true;
            if (cancellation != null) cancellation.Cancel();
            tray.Visible = false; tray.Dispose(); help.Dispose(); base.OnFormClosing(e);
        }

        internal void Preview(string original, string translation) { source.Text = original; target.Text = translation; copy.Enabled = true; SetStatus("Traducción lista.", false); }
    }

}

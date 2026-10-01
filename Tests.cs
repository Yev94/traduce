using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace Traduce
{
    internal static class Tests
    {
        private static int passed;
        private static readonly string LogRoot = Path.Combine(Path.GetTempPath(), "TraduceTests-" + Guid.NewGuid().ToString("N"));
        private static string Self { get { return System.Reflection.Assembly.GetExecutingAssembly().Location; } }
        private static void Check(bool condition, string name)
        {
            if (!condition) throw new Exception("FAIL: " + name);
            passed++; Console.WriteLine("PASS: " + name);
        }
        [STAThread]
        private static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Thread.CurrentThread.CurrentUICulture = new System.Globalization.CultureInfo("es-ES");
            L.Language = "es";
            if (args.Contains("--contracts"))
            {
                try
                {
                    using (var picture = new Bitmap(40, 20))
                        ProviderTests.Run(Check, ImageInput.FromImage(picture)).GetAwaiter().GetResult();
                    Console.WriteLine("PASS: " + passed + " provider checks"); return 0;
                }
                catch (Exception e) { Console.Error.WriteLine(e); return 1; }
            }
            if (args.Contains("--probe"))
            {
                string input = new StreamReader(Console.OpenStandardInput(), Encoding.UTF8).ReadToEnd();
                Console.Write(new JavaScriptSerializer().Serialize(new[] { args[1], input })); return 0;
            }
            if (args.Contains("--sleep")) { Thread.Sleep(10000); return 0; }
            if (args.Contains("exec"))
            {
                if (args.Contains("--help")) { Console.Write("--ignore-user-config --ephemeral"); return 0; }
                string input = new StreamReader(Console.OpenStandardInput(), Encoding.UTF8).ReadToEnd();
                string output = args[Array.IndexOf(args, "--output-last-message") + 1];
                if (input == "force error") { Console.Error.Write("401 authentication private diagnostic"); return 1; }
                if (input == "force empty") return 0;
                if (input == "cache probe") { File.WriteAllText(output, Guid.NewGuid().ToString()); return 0; }
                if (input == "slow first selection") Thread.Sleep(900);
                if (args.Contains("--image"))
                {
                    string file = args[Array.IndexOf(args, "--image") + 1];
                    using (var image = Image.FromFile(file)) if (image.Width < 1) return 2;
                    File.WriteAllText(output, "Traducción desde imagen"); return 0;
                }
                File.WriteAllText(output, "Traducción: " + input.Replace("\r\n", "\n"), new UTF8Encoding(false)); return 0;
            }
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            int exit = 0;
            using (var pump = new Form { ShowInTaskbar = false, Opacity = 0, Size = new Size(1, 1) })
            {
                pump.Shown += async delegate {
                    try
                    {
                        if (args.Contains("--screenshots")) await DocumentationScreenshots();
                        else await Run(args.Contains("--live"), args.Contains("--desktop"));
                        Console.WriteLine("PASS: " + passed + " checks");
                    }
                    catch (Exception e) { Console.Error.WriteLine(e); exit = 1; }
                    finally { if (Directory.Exists(LogRoot)) Directory.Delete(LogRoot, true); pump.Close(); }
                };
                Application.Run(pump);
            }
            return exit;
        }
        private static async Task Run(bool live, bool desktop)
        {
            Check(AreDpiAwarenessContextsEqual(GetThreadDpiAwarenessContext(), new IntPtr(-4)), "Packaged configuration enables per-monitor DPI awareness");
            string unicode = "Résumé 日本語 — mañana 🦊\r\n¿Dónde está el café?";
            string argument = "spaces \\\"quotes\" & $(literal) \\";
            var result = await Runner.Run(Self, new[] { "--probe", argument }, unicode, Path.GetTempPath(), 5000, CancellationToken.None);
            var echoed = new JavaScriptSerializer().Deserialize<string[]>(result.Output);
            Check(result.ExitCode == 0 && echoed[0] == argument && echoed[1] == unicode, "Unicode stdin and literal Windows argument quoting");
            try { Translator.Validate(" \r\n"); throw new Exception("Accepted empty input"); } catch (ArgumentException) { passed++; }
            try { Translator.Validate(new string('x', Translator.MaxCharacters + 1)); throw new Exception("Accepted oversized input"); } catch (ArgumentException) { passed++; }
            Translator.Validate(new string('x', Translator.MaxCharacters));
            var defaults = Translator.Arguments(Path.GetTempPath(), "");
            Check(defaults[defaults.IndexOf("--model") + 1] == Settings.DefaultModel && defaults.Contains("model_reasoning_effort=\"low\""), "Empty settings explicitly select lightweight Luna at low effort");
            using (var cancel = new CancellationTokenSource(150))
            {
                var watch = Stopwatch.StartNew();
                try { await Runner.Run(Self, new[] { "--sleep" }, null, Path.GetTempPath(), 5000, cancel.Token); throw new Exception("Cancellation ignored"); }
                catch (OperationCanceledException) { Check(watch.ElapsedMilliseconds < 3000, "Cancellation terminates child promptly"); }
            }
            try { await Runner.Run(Self, new[] { "--sleep" }, null, Path.GetTempPath(), 100, CancellationToken.None); throw new Exception("Timeout ignored"); }
            catch (TimeoutException) { passed++; }
            var blockedInput = Stopwatch.StartNew();
            try { await Runner.Run(Self, new[] { "--sleep" }, new string('x', 60000), Path.GetTempPath(), 120, CancellationToken.None); throw new Exception("Blocked stdin ignored timeout"); }
            catch (TimeoutException) { Check(blockedInput.ElapsedMilliseconds < 3000, "Timeout interrupts child even when stdin is blocked"); }
            var settings = new Settings { CodexPath = Self };
            string[] before = Directory.GetDirectories(Path.GetTempPath(), "Traduce-*");
            string translated = await new Translator().Translate(unicode, settings, CancellationToken.None);
            Check(translated == "Traducción: " + unicode, "CLI resolution, stdin and final output file end to end");
            var cachedTranslator = new Translator();
            string first = await cachedTranslator.Translate("cache probe", settings, CancellationToken.None);
            Check(await cachedTranslator.Translate("cache probe", settings, CancellationToken.None) == first, "Repeated selection reuses output without a new model call");
            Check(await cachedTranslator.Translate("cache probe", new Settings { CodexPath = Self, Model = "another-model" }, CancellationToken.None) != first, "Changing model invalidates translation cache");
            try { await cachedTranslator.Translate("cache probe", settings, new CancellationToken(true)); throw new Exception("Cached result ignored cancellation"); }
            catch (OperationCanceledException) { passed++; }
            try { await new Translator().Translate("force error", settings, CancellationToken.None); throw new Exception("Error ignored"); }
            catch (InvalidOperationException e) { Check(e.Message.Contains("sesión") && !e.Message.Contains("private"), "Authentication error is useful and diagnostic stays private"); }
            try { await new Translator().Translate("force empty", settings, CancellationToken.None); throw new Exception("Empty output accepted"); }
            catch (InvalidOperationException e) { Check(e.Message.Contains("ninguna"), "Empty final output rejected"); }
            Check(!Directory.GetDirectories(Path.GetTempPath(), "Traduce-*").Except(before).Any(), "Temporary translations removed after success and failure");

            ImageInput imageFixture;
            using (var picture = TextImage("La reunión es mañana a las nueve.")) imageFixture = ImageInput.FromImage(picture);
            await ProviderTests.Run(Check, imageFixture);
            Check(await new Translator().TranslateImage(imageFixture, settings, CancellationToken.None) == "Traducción desde imagen", "Image is supplied as a real CLI image attachment");
            Check(!Directory.GetDirectories(Path.GetTempPath(), "Traduce-*").Except(before).Any(), "Temporary image is removed after translation");
            var ocrWatch = Stopwatch.StartNew();
            var recognized = await RegionInput.FromImage(imageFixture, CancellationToken.None);
            Check(recognized.Image == null && recognized.Text.Contains("reunión") && recognized.Text.Contains("mañana"), "Windows OCR sends recognized text without an image (" + ocrWatch.ElapsedMilliseconds + " ms)");
            var blank = await RegionInput.Choose(imageFixture, token => Task.FromResult("  "), CancellationToken.None);
            Check(blank.Image == imageFixture && blank.Text == null, "Empty OCR sends the original crop as an image");
            var unavailable = await RegionInput.Choose(imageFixture, token => { throw new InvalidOperationException("OCR unavailable"); }, CancellationToken.None);
            Check(unavailable.Image == imageFixture, "Unavailable OCR falls back to image");
            Check(recognized.OcrReason == "text_recognized" && blank.OcrReason == "no_text" && unavailable.OcrReason == "ocr_error", "OCR result distinguishes recognition, no text and failure");
            var timeoutInput = await RegionInput.Choose(imageFixture, token => { throw new OperationCanceledException(); }, CancellationToken.None);
            var oversizedInput = await RegionInput.Choose(imageFixture, token => { throw new OcrImageTooLargeException(); }, CancellationToken.None);
            var noEngineInput = await RegionInput.Choose(imageFixture, token => { throw new OcrUnavailableException(); }, CancellationToken.None);
            Check(timeoutInput.OcrReason == "timeout" && oversizedInput.OcrReason == "image_too_large" && noEngineInput.OcrReason == "unavailable", "Image fallback records the actual OCR reason");
            CheckPlacement();
            using (var white = new Bitmap(200, 80))
            {
                using (var graphics = Graphics.FromImage(white)) graphics.Clear(Color.White);
                var emptyImage = ImageInput.FromImage(white);
                var noText = await RegionInput.FromImage(emptyImage, CancellationToken.None);
                Check(noText.Image == emptyImage && noText.Text == null, "Real Windows OCR on a blank crop falls back to image");
            }
            try { await RegionInput.Choose(imageFixture, token => Task.FromResult("text"), new CancellationToken(true)); throw new Exception("OCR ignored cancellation"); }
            catch (OperationCanceledException) { passed++; }
            Check(RegionPicker.SelectionBounds(new Point(90, 80), new Point(-10, 20), new Size(100, 100)) == new Rectangle(0, 20, 90, 60), "Reverse drag clamps crop to desktop bounds");
            using (var snapshot = new Bitmap(160, 100))
            {
                snapshot.SetPixel(20, 30, Color.Red);
                using (var picker = new RegionPicker(snapshot, new Rectangle(20, 20, 160, 100)))
                {
                    var picked = picker.Pick(CancellationToken.None);
                    Drag(picker, new Point(20, 30), new Point(90, 80));
                    using (var crop = await picked)
                        Check(crop.Image.Size == new Size(70, 50) && crop.Image.GetPixel(0, 0).ToArgb() == Color.Red.ToArgb() && crop.Bounds == new Rectangle(40, 50, 70, 50), "Overlay returns selected pixels and absolute screen coordinates");
                }
                using (var picker = new RegionPicker(snapshot, new Rectangle(20, 20, 160, 100)))
                {
                    var picked = picker.Pick(CancellationToken.None);
                    Native.PostMessage(picker.Handle, 0x100, (IntPtr)Keys.Escape, IntPtr.Zero);
                    try { await picked; throw new Exception("Escape accepted a capture"); } catch (OperationCanceledException) { passed++; }
                }
                using (var picker = new RegionPicker(snapshot, new Rectangle(20, 20, 160, 100)))
                using (var canceled = new CancellationTokenSource())
                {
                    var picked = picker.Pick(canceled.Token); canceled.Cancel();
                    try { await picked; throw new Exception("Canceled overlay remained open"); } catch (OperationCanceledException) { passed++; }
                }
            }
            string flowFolder = Path.Combine(LogRoot, "flow");
            var flowLog = new CaptureLog(flowFolder);
            using (var form = new MainForm(settings, flowLog))
            {
                IntPtr hiddenHandle = form.Handle;
                Check(form.IsHandleCreated && form.HotkeyActive, "Hidden startup registers Alt+T before the first Show");
                form.Show();
                var box = (TextBox)form.Controls.Find("Translation", true)[0];
                await form.Submit(token => Task.FromResult(new TranslationInput { Text = "first selection" }), false);
                await form.Submit(token => Task.FromResult(new TranslationInput { Text = "second selection" }), false);
                Check(box.Text == "Traducción: second selection", "A second selection is translated by the same window");
                var cancelledTrace = new CaptureTrace { ActiveApplication = SourceApplication.FromWindow(form.Handle) };
                var firstRequest = form.Submit(token => Task.FromResult(new TranslationInput { Text = "slow first selection", OcrReason = "text_recognized" }), false, cancelledTrace);
                await Task.Delay(120);
                var replacement = form.Submit(token => Task.FromResult(new TranslationInput { Text = "latest selection" }), false);
                await Task.WhenAll(firstRequest, replacement);
                Check(box.Text == "Traducción: latest selection", "New selection cancels pending work and stale output cannot overwrite it");
                var imageTrace = new CaptureTrace { ActiveApplication = SourceApplication.FromWindow(form.Handle) };
                await form.Submit(token => Task.FromResult(blank), false, imageTrace);
                var failureTrace = new CaptureTrace();
                await form.Submit(token => Task.FromResult(new TranslationInput { Text = "force error", OcrReason = "text_recognized" }), false, failureTrace);
                var records = ReadLog(flowFolder);
                Check(records.Any(r => (string)r["capture_id"] == imageTrace.Id && (string)r["event"] == "capture" && (string)r["mode"] == "image" && (string)r["ocr_reason"] == "no_text"), "Image decision is written before translation");
                Check(records.Any(r => (string)r["capture_id"] == cancelledTrace.Id && (string)r["event"] == "finished" && (string)r["outcome"] == "cancelled"), "Replacement is recorded against the cancelled request ID");
                Check(records.Any(r => (string)r["capture_id"] == imageTrace.Id && (string)r["event"] == "finished" && (string)r["outcome"] == "success") && records.Any(r => (string)r["capture_id"] == failureTrace.Id && (string)r["outcome"] == "error"), "Log distinguishes successful translation from failure");
                string raw = string.Join("", Directory.GetFiles(flowFolder).Select(File.ReadAllText));
                Check(!raw.Contains("slow first selection") && !raw.Contains("force error") && !raw.Contains("private diagnostic") && !raw.Contains("Traducción desde imagen"), "Log contains metadata without source text, translation or raw errors");
                form.Quit();
            }
            string occupied = Path.Combine(LogRoot, "occupied"); File.WriteAllText(occupied, "fixture");
            Check(!new CaptureLog(occupied).Append(new CaptureTrace(), "capture", "ready", 0), "Unwritable log path reports failure without aborting translation");

            var savedClipboard = ClipboardAccess.Snapshot();
            try
            {
                var original = new DataObject(); original.SetText("previous clipboard"); original.SetData(DataFormats.Rtf, @"{\rtf1\ansi previous clipboard}");
                Clipboard.SetDataObject(original, true);
                Check(ClipboardAccess.ReadText() == "previous clipboard", "Native clipboard reads Unicode text");
                using (var picture = TextImage("La reunión es mañana a las nueve.")) Clipboard.SetImage(picture);
                using (var form = new MainForm(settings))
                {
                    form.Show(); ((Button)form.Controls.Find("Paste", true)[0]).PerformClick();
                    var box = (TextBox)form.Controls.Find("Translation", true)[0];
                    for (int i = 0; i < 100 && box.Text != "Traducción desde imagen"; i++) await Task.Delay(30);
                    Check(box.Text == "Traducción desde imagen", "Paste accepts a screenshot from the Windows clipboard");
                    form.Quit();
                }
                Clipboard.SetDataObject(original, true);

                if (desktop)
                using (var translator = new MainForm(settings, new CaptureLog(Path.Combine(LogRoot, "desktop"))))
                using (var editor = new Form { Text = "Traduce: editor de prueba", Size = new Size(760, 250), StartPosition = FormStartPosition.CenterScreen })
                {
                    IntPtr hiddenHandle = translator.Handle;
                    var field = new Label { Dock = DockStyle.Fill, Padding = new Padding(12, 8, 0, 0), Font = new Font("Segoe UI", 24), Text = "First region capture", BackColor = Color.White, ForeColor = Color.Black };
                    editor.Controls.Add(field); editor.Show(); editor.Activate();
                    FocusTestWindow(editor.Handle);
                    await Task.Delay(100);
                    Check(Native.GetForegroundWindow() == editor.Handle, "Test editor owns foreground before region capture");
                    var keys = new[] { Key(Keys.Menu, false), Key(Keys.T, false), Key(Keys.T, true), Key(Keys.Menu, true) };
                    var box = (TextBox)translator.Controls.Find("Translation", true)[0];
                    var screens = Screen.AllScreens;
                    Console.WriteLine("Desktop test: " + screens.Length + " connected monitor(s)");
                    for (int capture = 0; capture < Math.Max(2, screens.Length); capture++)
                    {
                        Rectangle work = screens[capture % screens.Length].WorkingArea;
                        editor.StartPosition = FormStartPosition.Manual;
                        editor.Bounds = work;
                        field.Dock = capture == 0 ? DockStyle.Top : DockStyle.Bottom;
                        field.Height = 200;
                        FocusTestWindow(editor.Handle);
                        await Task.Delay(70);
                        string expected = capture == 0 ? "First region capture" : "Second region capture";
                        field.Text = expected; editor.Refresh();
                        Check(Native.SendInput((uint)keys.Length, keys, System.Runtime.InteropServices.Marshal.SizeOf(typeof(Native.Input))) == keys.Length, "Global shortcut sent to Windows");
                        RegionPicker picker = null;
                        for (int i = 0; i < 100 && picker == null; i++)
                        {
                            await Task.Delay(30); picker = Application.OpenForms.OfType<RegionPicker>().FirstOrDefault();
                        }
                        Check(picker != null && !translator.Visible, "Alt+T opens overlay with translator hidden");
                        Check(picker.Bounds == SystemInformation.VirtualScreen, "Overlay spans the current virtual desktop");
                        Rectangle area = field.RectangleToScreen(new Rectangle(4, 4, field.Width - 8, field.Height - 8));
                        Drag(picker, picker.PointToClient(area.Location), picker.PointToClient(new Point(area.Right, area.Bottom)));
                        for (int i = 0; i < 600 && !box.Text.StartsWith("Traducción"); i++) await Task.Delay(30);
                        var captureDecision = ReadLog(Path.Combine(LogRoot, "desktop")).Last(r => (string)r["event"] == "capture");
                        Console.WriteLine("Desktop OCR: mode=" + captureDecision["mode"] + ", reason=" + captureDecision["ocr_reason"] + ", characters=" + captureDecision["text_characters"] + ", crop=" + area.Size);
                        // Windows OCR occasionally drops a letter in "region".
                        // Assert the distinct marker and text route, not OCR spelling.
                        Check(box.Text.StartsWith("Traducción:") && box.Text.Contains(capture == 0 ? "First" : "Second") && box.Text.Contains("capture"), "Real region " + (capture + 1) + " uses the new OCR text and displays translation");
                        Check(Native.GetForegroundWindow() == editor.Handle, "Overlay restores focus to source after capture");
                        Check(ClipboardAccess.ReadText() == "previous clipboard", "Region capture leaves clipboard untouched");
                        Check(screens[capture % screens.Length].WorkingArea.Contains(translator.Bounds), "Popup stays inside the source monitor's work area");
                        Check(translator.Width == Math.Min(area.Width, work.Width), "Popup matches the selected width on this monitor");
                        Check(!translator.Bounds.IntersectsWith(area), "Popup fits above or below the selection without covering it");
                        Check(GetAncestor(WindowFromPoint(new Point(translator.Left + translator.Width / 2, translator.Top + translator.Height / 2)), 2) == translator.Handle, "Result remains visible above the full-screen source without taking focus");
                        using (var otherWindow = new Form { Text = "Traduce: cambio de ventana", StartPosition = FormStartPosition.Manual, Bounds = translator.Bounds })
                        {
                            otherWindow.Show(); FocusTestWindow(otherWindow.Handle);
                            await Task.Delay(70);
                            Check(Native.GetForegroundWindow() == otherWindow.Handle, "Another window can receive focus while the translation stays open");
                            Check(GetAncestor(WindowFromPoint(new Point(translator.Left + translator.Width / 2, translator.Top + translator.Height / 2)), 2) == translator.Handle, "Translation stays above a newly activated overlapping window");
                        }
                        FocusTestWindow(editor.Handle);
                        var entry = ReadLog(Path.Combine(LogRoot, "desktop")).Last(r => (string)r["event"] == "capture");
                        var app = (System.Collections.Generic.Dictionary<string, object>)entry["active_app"];
                        var underRegion = (System.Collections.Generic.Dictionary<string, object>)entry["region_app"];
                        Check((string)entry["mode"] == "text" && (int)app["process_id"] == Process.GetCurrentProcess().Id && (int)underRegion["process_id"] == Process.GetCurrentProcess().Id, "Real capture logs text and source application before the overlay");
                        Check((string)entry["monitor"] == screens[capture % screens.Length].DeviceName, "Capture log identifies the selected monitor");
                    }
                    translator.Quit();
                }
            }
            finally
            {
                if (savedClipboard.GetFormats(false).Length == 0) Clipboard.Clear(); else Clipboard.SetDataObject(savedClipboard, true);
            }

            string demoOriginal = "Small steps make a difference.\r\n\r\nPress Alt+T and drag over a region to translate it.";
            string demoTranslation = "Los pequeños pasos marcan la diferencia.\r\n\r\nPulsa Alt+T y arrastra sobre una región para traducirla.";
            if (live)
            {
                var watch = Stopwatch.StartNew();
                demoTranslation = await new Translator().Translate(demoOriginal, new Settings(), CancellationToken.None);
                Console.WriteLine("Live text result: " + demoTranslation);
                Check(demoTranslation.Contains("pasos") && (demoTranslation.Contains("región") || demoTranslation.Contains("zona")), "Real authenticated Codex translation (" + watch.Elapsed.TotalSeconds.ToString("F1") + " s)");
                string reverse = await new Translator().Translate(recognized.Text, new Settings(), CancellationToken.None);
                Check(reverse.ToLowerInvariant().Contains("tomorrow") && reverse.ToLowerInvariant().Contains("meeting"), "Real local OCR text translates from Spanish into English");
                string vision = await new Translator().TranslateImage(imageFixture, new Settings(), CancellationToken.None);
                Check(vision.ToLowerInvariant().Contains("tomorrow") && vision.ToLowerInvariant().Contains("meeting"), "Real screenshot text is read and translated into English");
            }
            using (var form = new MainForm())
            {
                form.Show(); form.Preview(demoOriginal, demoTranslation); Application.DoEvents();
                Check(form.HotkeyActive && !Native.RegisterHotKey(form.Handle, 9876, 0x4001, (uint)Keys.T), "Alt+T is registered by the compact window");
                using (var snapshot = new Bitmap(form.Width, form.Height))
                {
                    form.DrawToBitmap(snapshot, new Rectangle(0, 0, form.Width, form.Height));
                    snapshot.Save(Path.Combine(Path.GetDirectoryName(Self), "preview.png"), System.Drawing.Imaging.ImageFormat.Png);
                }
                ((Button)form.Controls.Find("EditOriginal", true)[0]).PerformClick(); Application.DoEvents();
                using (var snapshot = new Bitmap(form.Width, form.Height))
                {
                    form.DrawToBitmap(snapshot, new Rectangle(0, 0, form.Width, form.Height));
                    snapshot.Save(Path.Combine(Path.GetDirectoryName(Self), "expanded-preview.png"), System.Drawing.Imaging.ImageFormat.Png);
                }
                ((Button)form.Controls.Find("EditOriginal", true)[0]).PerformClick();
                Rectangle work = Screen.FromControl(form).WorkingArea;
                foreach (int width in new[] { 220, 780 })
                {
                    form.RevealNear(new Rectangle(work.Left + 20, work.Top + 40, width, 70)); Application.DoEvents();
                    Check(form.Width == Math.Min(width, work.Width), "Result follows a " + width + " px selection instead of the old minimum width");
                    var bottom = form.Controls.Find("Paste", true)[0].Parent;
                    Check(bottom.Controls.Cast<Control>().Where(c => c.Visible).All(c => bottom.ClientRectangle.Contains(c.Bounds)), "Bottom buttons remain visible at " + width + " px");
                    if (width == 220)
                    using (var snapshot = new Bitmap(form.Width, form.Height))
                    {
                        form.DrawToBitmap(snapshot, new Rectangle(Point.Empty, form.Size));
                        snapshot.Save(Path.Combine(Path.GetDirectoryName(Self), "narrow-preview.png"), System.Drawing.Imaging.ImageFormat.Png);
                    }
                }
                form.Quit();
            }
            Check(true, "Native window renders and closes cleanly");
            var languageSettings = new Settings { CodexPath = Self, Language = "es" };
            using (var connection = new SettingsForm(languageSettings))
            {
                connection.Show();
                ((ComboBox)connection.Controls.Find("InterfaceLanguage", true)[0]).SelectedIndex = 1;
                ((Button)connection.Controls.Find("SaveConnection", true)[0]).PerformClick();
                for (int i = 0; i < 100 && connection.DialogResult != DialogResult.OK; i++) await Task.Delay(30);
                Check(connection.DialogResult == DialogResult.OK && connection.Configuration.Language == "en" && languageSettings.Language == "es", "Language selector saves English without mutating the original settings draft");
                languageSettings = Settings.Decode(new JavaScriptSerializer().Serialize(connection.Configuration));
                Check(languageSettings.Language == "en" && languageSettings.CodexPath == Self, "Interface language persists alongside the existing account configuration");
                connection.Close();
            }
            using (var translatedWindow = new MainForm(languageSettings))
            {
                translatedWindow.Show(); translatedWindow.Preview("sample", "Texto que no debe cambiar");
                Check(translatedWindow.Controls.Find("Paste", true)[0].Text == "Paste" && L.T("Ajustes") == "Settings", "English interface renders translated controls");
                var settingsButton = translatedWindow.Controls.Find("Settings", true)[0];
                Check(settingsButton.Width >= settingsButton.GetPreferredSize(Size.Empty).Width, "English Settings label fits without truncation");
                Check(ApiTranslator.HttpError(401).Contains("key") && Providers.Get("claude-code").ToString().Contains("official account"), "Errors and provider labels follow the interface language");
                languageSettings.Language = "es"; translatedWindow.ApplyLanguage();
                Check(translatedWindow.Controls.Find("Paste", true)[0].Text == "Pegar" && translatedWindow.Controls.Find("Translation", true)[0].Text == "Texto que no debe cambiar", "Language can switch back immediately without changing translated content");
                translatedWindow.Quit();
            }
            using (var dialog = new SettingsForm(new Settings()))
            {
                dialog.Show(); Application.DoEvents();
                var connectionSave = dialog.Controls.Find("SaveConnection", true)[0];
                Check(dialog.RectangleToScreen(dialog.ClientRectangle).Contains(connectionSave.RectangleToScreen(connectionSave.ClientRectangle)) && connectionSave.Visible, "Connection save button stays visible outside the scrolling content");
                using (var snapshot = new Bitmap(dialog.Width, dialog.Height))
                {
                    dialog.DrawToBitmap(snapshot, new Rectangle(0, 0, dialog.Width, dialog.Height));
                    snapshot.Save(Path.Combine(Path.GetDirectoryName(Self), "settings-preview.png"), System.Drawing.Imaging.ImageFormat.Png);
                }
                var providers = (ComboBox)dialog.Controls.Find("Provider", true)[0];
                providers.SelectedItem = Providers.Get("custom"); Application.DoEvents();
                ((TextBox)dialog.Controls.Find("Endpoint", true)[0]).Text = "https://first.example/v1";
                ((TextBox)dialog.Controls.Find("ApiKey", true)[0]).Text = "fixture-credential";
                providers.SelectedItem = Providers.Get("openai");
                Check(dialog.Configuration.Profile("custom").ReadKey() == "fixture-credential" && dialog.Configuration.Profile("openai").ReadKey() == "", "Switching provider preserves its own key without sharing it");
                providers.SelectedItem = Providers.Get("custom");
                ((TextBox)dialog.Controls.Find("Endpoint", true)[0]).Text = "https://other.example/v1";
                providers.SelectedItem = Providers.Get("openai");
                Check(dialog.Configuration.Profile("custom").ReadKey() == "", "Changing custom endpoint clears the previous destination's key");
                Application.DoEvents();
                using (var snapshot = new Bitmap(dialog.Width, dialog.Height))
                {
                    dialog.DrawToBitmap(snapshot, new Rectangle(0, 0, dialog.Width, dialog.Height));
                    snapshot.Save(Path.Combine(Path.GetDirectoryName(Self), "api-settings-preview.png"), System.Drawing.Imaging.ImageFormat.Png);
                }
                dialog.Close();
            }
            // Exercise the real entry point: a second launch must wait until
            // WinForms has finished recreating handles during startup.
            foreach (string startupArguments in new[] { "", "--background" })
            using (var app = Process.Start(new ProcessStartInfo(Path.Combine(Path.GetDirectoryName(Self), "Traduce.exe")) {
                Arguments = startupArguments, UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden
            }))
            {
                try
                {
                    bool hasMutex = false;
                    for (int i = 0; i < 200 && !hasMutex; i++)
                    {
                        try { using (var mutex = Mutex.OpenExisting("Local\\Traduce.Desktop")) hasMutex = true; }
                        catch (WaitHandleCannotBeOpenedException) { }
                        if (!hasMutex) await Task.Delay(5);
                    }
                    Check(hasMutex, "Real application creates its single-instance mutex");
                    if (startupArguments == "--background")
                    {
                        using (var ready = new EventWaitHandle(false, EventResetMode.ManualReset, "Local\\Traduce.Ready"))
                            Check(ready.WaitOne(5000), "Background application completes hidden startup");
                        IntPtr backgroundWindow = Native.FindWindow(null, "Traduce");
                        Check(backgroundWindow != IntPtr.Zero && !IsWindowVisible(backgroundWindow), "Windows startup mode creates no visible translation window");
                        bool shortcutFree = Native.RegisterHotKey(IntPtr.Zero, 9186, 0x4001, (uint)Keys.T);
                        if (shortcutFree) Native.UnregisterHotKey(IntPtr.Zero, 9186);
                        Check(!shortcutFree, "Background application reserves Alt+T at startup");
                        await Runner.Run(Path.Combine(Path.GetDirectoryName(Self), "Traduce.exe"), new[] { "--background" }, null, Path.GetDirectoryName(Self), 7000, CancellationToken.None);
                        Check(!IsWindowVisible(backgroundWindow), "Repeated startup launch leaves the running app hidden");
                    }
                    await Runner.Run(Path.Combine(Path.GetDirectoryName(Self), "Traduce.exe"), new[] { "--quit" }, null, Path.GetDirectoryName(Self), 7000, CancellationToken.None);
                    for (int i = 0; i < 100 && !app.HasExited; i++) await Task.Delay(20);
                    Check(app.HasExited && app.ExitCode == 0, "Immediate second launch can close the real app during startup");
                }
                finally { if (!app.HasExited) { app.Kill(); app.WaitForExit(3000); } }
            }
        }

        private static System.Collections.Generic.Dictionary<string, object>[] ReadLog(string folder)
        {
            var serializer = new JavaScriptSerializer();
            return Directory.GetFiles(folder, "*.jsonl").OrderBy(path => path).SelectMany(File.ReadAllLines)
                .Select(line => serializer.Deserialize<System.Collections.Generic.Dictionary<string, object>>(line)).ToArray();
        }

        private static void CheckPlacement()
        {
            Size size = new Size(440, 330);
            Rectangle primary = new Rectangle(0, 0, 1920, 1040);
            Rectangle top = new Rectangle(700, 40, 250, 80), bottom = new Rectangle(700, 950, 250, 70);
            Rectangle below = PopupPlacement.Calculate(top, size, primary, 10), above = PopupPlacement.Calculate(bottom, size, primary, 10);
            Check(below.Top == top.Bottom + 10 && above.Bottom == bottom.Top - 10 && primary.Contains(below) && primary.Contains(above), "Placement chooses below near the top and above near the bottom");
            Check(below.Width == top.Width && below.Left == top.Left && above.Width == bottom.Width, "Popup width and horizontal edges follow the selection");
            var displays = new[] { new Rectangle(-1920, 0, 1920, 1040), new Rectangle(1920, -400, 1440, 860), new Rectangle(0, -1200, 1920, 1160), new Rectangle(1920, 0, 1080, 1880) };
            foreach (Rectangle work in displays)
            {
                foreach (Size popup in new[] { size, new Size(880, 660), new Size(3000, 2200) })
                {
                    foreach (Rectangle area in new[] { new Rectangle(work.Left, work.Top + 20, 120, 60), new Rectangle(work.Right - 60, work.Bottom - 80, 60, 80), work })
                    {
                        Rectangle placed = PopupPlacement.Calculate(area, popup, work, 12);
                        if (!work.Contains(placed) || placed.Width != Math.Min(area.Width, work.Width)) throw new Exception("Popup left its monitor or lost the selected width: " + placed);
                    }
                }
            }
            Check(true, "Placement handles negative origins, stacked/portrait monitors, larger windows and full-screen selections");
            Rectangle selectedScreen = new Rectangle(1920, 0, 1920, 1040), spanning = new Rectangle(1800, 10, 900, 100);
            Check(selectedScreen.Contains(PopupPlacement.Calculate(spanning, size, selectedScreen, 10)), "Cross-monitor capture uses the chosen monitor's intersection");
            Rectangle nearTaskbar = PopupPlacement.Calculate(new Rectangle(400, 950, 300, 70), size, new Rectangle(70, 35, 1780, 970), 10);
            Check(new Rectangle(70, 35, 1780, 970).Contains(nearTaskbar), "Placement respects taskbars on any edge");
        }

        private static void Drag(RegionPicker picker, Point from, Point to)
        {
            Native.PostMessage(picker.Handle, 0x201, new IntPtr(1), new IntPtr((from.Y << 16) | (from.X & 0xffff)));
            Native.PostMessage(picker.Handle, 0x200, new IntPtr(1), new IntPtr((to.Y << 16) | (to.X & 0xffff)));
            Native.PostMessage(picker.Handle, 0x202, IntPtr.Zero, new IntPtr((to.Y << 16) | (to.X & 0xffff)));
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, IntPtr process);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(Point point);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr GetThreadDpiAwarenessContext();
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool AreDpiAwarenessContextsEqual(IntPtr first, IntPtr second);
        [System.Runtime.InteropServices.DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool AttachThreadInput(uint first, uint second, bool attach);
        private static void FocusTestWindow(IntPtr window)
        {
            uint foreground = GetWindowThreadProcessId(Native.GetForegroundWindow(), IntPtr.Zero), current = GetCurrentThreadId();
            bool attached = foreground != current && AttachThreadInput(current, foreground, true);
            try { Native.SetForegroundWindow(window); }
            finally { if (attached) AttachThreadInput(current, foreground, false); }
        }

        private static Native.Input Key(Keys key, bool up)
        {
            return new Native.Input { Type = 1, Data = new Native.InputUnion { Keyboard = new Native.KeyboardInput { Key = (ushort)key, Flags = up ? 2u : 0u } } };
        }

        private static Bitmap TextImage(string text)
        {
            var bitmap = new Bitmap(760, 110);
            using (var graphics = Graphics.FromImage(bitmap))
            using (var font = new Font("Segoe UI", 25))
            {
                graphics.Clear(Color.White); graphics.DrawString(text, font, Brushes.Black, 14, 24);
            }
            return bitmap;
        }

        private static async Task DocumentationScreenshots()
        {
            string folder = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Self), "..", "docs"));
            Directory.CreateDirectory(folder);
            foreach (string language in new[] { "es", "en" })
            {
                var settings = new Settings { Language = language, ConfigurationComplete = true };
                using (var form = new MainForm(settings))
                {
                    form.ClientSize = new Size(520, 255);
                    string spanish = "Los pequeños pasos marcan la diferencia.\r\n\r\nSelecciona lo que quieras entender, esté donde esté.\r\n\r\nUna idea, dos idiomas. Así de sencillo.";
                    string english = "Small steps make a difference.\r\n\r\nSelect whatever you want to understand, wherever it is.\r\n\r\nOne idea, two languages. It's that simple.";
                    form.Preview(language == "es" ? english : spanish, language == "es" ? spanish : english);
                    await SaveWindowCapture(form, Path.Combine(folder, "window-" + language + ".png"));
                    form.Quit();
                }
                using (var form = new SettingsForm(settings))
                {
                    await SaveWindowCapture(form, Path.Combine(folder, "settings-" + language + ".png"));
                    var selection = (ComboBox)form.Controls.Find("InterfaceLanguage", true)[0];
                    Check(selection.SelectedIndex == (language == "es" ? 0 : 1), "Documentation captures the real " + language + " interface");
                    form.Close();
                }
            }
            L.Language = "es";
        }

        private static async Task SaveWindowCapture(Form form, string path)
        {
            // A neutral window behind the app keeps its translucent frame free of desktop content.
            using (var backdrop = new Form { FormBorderStyle = FormBorderStyle.None, ShowInTaskbar = false, BackColor = Color.FromArgb(234, 238, 243), Bounds = Screen.PrimaryScreen.WorkingArea, StartPosition = FormStartPosition.Manual })
            {
                backdrop.Show(); form.Show(); form.Activate(); FocusTestWindow(form.Handle);
                await Task.Delay(400); form.Refresh();
                var translation = form.Controls.Find("Translation", true).FirstOrDefault() as TextBox;
                if (translation != null)
                {
                    translation.Select(0, 0);
                    form.Controls.Find("Paste", true)[0].Focus();
                    form.Refresh(); await Task.Delay(80);
                }
                WindowRect frame;
                Rectangle area = form.Bounds;
                if (DwmGetWindowAttribute(form.Handle, 9, out frame, 16) == 0)
                    area = Rectangle.FromLTRB(frame.Left, frame.Top, frame.Right, frame.Bottom);
                using (var bitmap = new Bitmap(area.Width, area.Height))
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    graphics.CopyFromScreen(area.Location, Point.Empty, area.Size);
                    bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                }
            }
        }
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct WindowRect { public int Left, Top, Right, Bottom; }
        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out WindowRect value, int size);
    }
}

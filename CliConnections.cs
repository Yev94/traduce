using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Traduce
{
    internal static class CliConnections
    {
        internal static IEnumerable<string> Candidates(string id)
        {
            string name = id == "codex" ? "codex.exe" : "claude.exe";
            var dirs = new List<string>((Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator));
            dirs.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin"));
            dirs.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WinGet", "Links"));
            foreach (string raw in dirs.Where(d => !string.IsNullOrWhiteSpace(d)))
            {
                string file = null;
                try { file = Path.Combine(raw.Trim('"'), name); } catch (ArgumentException) { }
                if (file != null && File.Exists(file)) yield return file;
                if (id == "claude-code")
                {
                    string native = null;
                    try { native = Path.Combine(raw.Trim('"'), "node_modules", "@anthropic-ai", "claude-code", "bin", "claude.exe"); } catch (ArgumentException) { }
                    if (native != null && File.Exists(native)) yield return native;
                }
            }
            string packages = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WinGet", "Packages");
            if (Directory.Exists(packages))
                foreach (string dir in Directory.GetDirectories(packages, (id == "codex" ? "OpenAI.Codex" : "Anthropic.ClaudeCode") + "*"))
                    foreach (string file in Directory.GetFiles(dir, id == "codex" ? "codex*.exe" : "claude.exe", SearchOption.AllDirectories))
                        if (Path.GetFileName(file) == name || Path.GetFileName(file) == "codex-x86_64-pc-windows-msvc.exe") yield return file;
        }
        public static async Task<string> Resolve(Settings settings, CancellationToken token)
        {
            string id = Providers.Get(settings.Provider).Id;
            string preferred = settings.Profile(id).CliPath;
            if (id == "codex") return await Translator.Resolve(preferred, token);
            IEnumerable<string> paths = string.IsNullOrWhiteSpace(preferred) ? Candidates(id) : new[] { preferred };
            foreach (string path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!File.Exists(path) || !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
                var result = await Runner.Run(path, new[] { "--help" }, null, Path.GetTempPath(), 12000, token);
                if (result.ExitCode == 0 && result.Output.Contains("--safe-mode") && result.Output.Contains("--no-session-persistence")) return path;
            }
            throw new InvalidOperationException("No se encontró Claude Code nativo actualizado. Pulsa Instalar cliente o elige su claude.exe en Opciones avanzadas.");
        }
        public static async Task<string> Status(Settings settings, CancellationToken token)
        {
            string exe = await Resolve(settings, token);
            var args = settings.Provider == "codex" ? new[] { "login", "status" } : new[] { "auth", "status" };
            var result = await Runner.Run(exe, args, null, Path.GetTempPath(), 15000, token);
            if (result.ExitCode != 0) return "Cliente instalado. Falta iniciar sesión.";
            if (settings.Provider == "codex") return (result.Output + result.Error).IndexOf("ChatGPT", StringComparison.OrdinalIgnoreCase) >= 0 ? "Conectado con ChatGPT." : "Conectado con credenciales API de Codex (facturación API).";
            var status = JsonData.Serializer().DeserializeObject(result.Output);
            string method = JsonData.Text(status, "authMethod");
            return method.IndexOf("api", StringComparison.OrdinalIgnoreCase) >= 0 ? "Claude Code conectado por API (facturación API)." : "Claude Code conectado con tu cuenta oficial.";
        }
        public static async Task Login(Settings settings, CancellationToken token)
        {
            string exe = await Resolve(settings, token);
            var args = settings.Provider == "codex" ? new[] { "login" } : new[] { "auth", "login" };
            var result = await Runner.Run(exe, args, null, Path.GetTempPath(), 300000, token);
            if (result.ExitCode != 0) throw new InvalidOperationException("El acceso oficial no se completó. Abre el cliente oficial para iniciar sesión y pulsa Comprobar después.");
        }
        public static async Task Install(Settings settings, CancellationToken token)
        {
            string winget = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", "winget.exe");
            if (!File.Exists(winget)) throw new InvalidOperationException("Instala App Installer desde Microsoft Store o usa el enlace de ayuda para instalar el cliente oficial.");
            string package = settings.Provider == "codex" ? "OpenAI.Codex" : "Anthropic.ClaudeCode";
            var result = await Runner.Run(winget, new[] { "install", "--id", package, "--exact", "--source", "winget", "--scope", "user", "--accept-source-agreements", "--accept-package-agreements", "--disable-interactivity", "--silent" }, null, Path.GetTempPath(), 600000, token);
            // WinGet may return a nonzero code if an up-to-date package exists.
            if (result.ExitCode != 0 && !Candidates(settings.Provider).Any()) throw new InvalidOperationException("WinGet no pudo instalar el cliente. Usa el enlace de ayuda para instalarlo y después pulsa Comprobar.");
            await Resolve(settings, token);
        }
        internal static string[] ClaudeArguments(string model)
        {
            return new[] { "--print", "--safe-mode", "--no-session-persistence", "--input-format", "stream-json", "--output-format", "stream-json", "--verbose", "--tools", "", "--permission-mode", "dontAsk", "--disable-slash-commands", "--strict-mcp-config", "--mcp-config", "{\"mcpServers\":{}}", "--model", model, "--system-prompt", Translator.Instructions };
        }
        internal static string ClaudeInput(string text, ImageInput image)
        {
            var content = new List<object> { new { type = "text", text = text } };
            if (image != null) content.Add(new { type = "image", source = new { type = "base64", media_type = "image/png", data = Convert.ToBase64String(image.Png) } });
            return JsonData.Serializer().Serialize(new { type = "user", message = new { role = "user", content = content } }) + "\n";
        }
        internal static string ClaudeResult(string output)
        {
            foreach (string line in output.Split('\n'))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var item = JsonData.Serializer().DeserializeObject(line);
                if (JsonData.Text(item, "type") != "result") continue;
                if (object.Equals(JsonData.Get(item, "is_error"), true)) throw new InvalidOperationException("Claude Code no completó la traducción. Comprueba tu cuenta, el modelo y sus límites.");
                string text = JsonData.Text(item, "result");
                if (!string.IsNullOrWhiteSpace(text)) return text.Trim();
            }
            throw new InvalidOperationException("Claude Code no devolvió una traducción completa.");
        }
        public static async Task<string> TranslateClaude(string text, ImageInput image, Settings settings, CancellationToken token)
        {
            string exe = await Resolve(settings, token);
            string folder = Path.Combine(Path.GetTempPath(), "Traduce-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                var result = await Runner.Run(exe, ClaudeArguments(Providers.Model(settings)), ClaudeInput(text, image), folder, 180000, token);
                if (result.ExitCode != 0) throw new InvalidOperationException("Claude Code no pudo traducir. Comprueba tu sesión, el modelo y sus límites desde Conexión.");
                try { return ClaudeResult(result.Output); }
                catch (ArgumentException) { throw new InvalidOperationException("Actualiza Claude Code: devolvió una respuesta no compatible."); }
            }
            finally { try { Directory.Delete(folder, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
        }
    }
}

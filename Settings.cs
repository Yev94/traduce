using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace Traduce
{
    internal sealed class ProviderProfile
    {
        public string Model { get; set; }
        public string BaseUrl { get; set; }
        public string CliPath { get; set; }
        public string ProtectedKey { get; set; }
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Traduce.API.v1");
        public void SetKey(string key)
        {
            key = (key ?? "").Trim();
            if (key.IndexOfAny(new[] { '\r', '\n' }) >= 0) throw new ArgumentException("La clave debe ocupar una sola línea.");
            ProtectedKey = key.Length == 0 ? null : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(key), Entropy, DataProtectionScope.CurrentUser));
        }
        public string ReadKey()
        {
            if (string.IsNullOrEmpty(ProtectedKey)) return "";
            try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(ProtectedKey), Entropy, DataProtectionScope.CurrentUser)); }
            catch (Exception e)
            {
                if (!(e is CryptographicException) && !(e is FormatException)) throw;
                throw new InvalidOperationException("Esta clave pertenece a otro usuario de Windows o está dañada. Pégala de nuevo en Conexión.");
            }
        }
    }

    internal sealed class Settings
    {
        public const string DefaultModel = "gpt-6-luna";
        // Retained for migration from the original Codex-only version.
        public string CodexPath { get; set; }
        public string Model { get; set; }
        public int Shortcut { get; set; }
        public string Provider { get; set; }
        public bool ConfigurationComplete { get; set; }
        public Dictionary<string, ProviderProfile> Profiles { get; set; }
        public Settings() { Provider = "codex"; Profiles = new Dictionary<string, ProviderProfile>(); }
        public static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Traduce");
        public ProviderProfile Profile(string id)
        {
            if (Profiles == null) Profiles = new Dictionary<string, ProviderProfile>();
            ProviderProfile profile;
            if (!Profiles.TryGetValue(id, out profile) || profile == null)
            {
                profile = new ProviderProfile();
                if (id == "codex") { profile.Model = Model; profile.CliPath = CodexPath; }
                Profiles[id] = profile;
            }
            return profile;
        }
        public Settings Clone() { return new JavaScriptSerializer().Deserialize<Settings>(new JavaScriptSerializer().Serialize(this)); }
        internal static Settings Decode(string json)
        {
            var serializer = new JavaScriptSerializer();
            var settings = serializer.Deserialize<Settings>(json) ?? new Settings();
            if (!serializer.Deserialize<Dictionary<string, object>>(json).ContainsKey("Provider")) settings.ConfigurationComplete = true;
            if (string.IsNullOrWhiteSpace(settings.Provider)) settings.Provider = "codex";
            settings.Profile("codex");
            return settings;
        }
        public static Settings Load()
        {
            try { return Decode(File.ReadAllText(Path.Combine(Folder, "settings.json"))); }
            catch (IOException) { return new Settings(); }
            catch (ArgumentException) { return new Settings(); }
            catch (UnauthorizedAccessException) { return new Settings(); }
            catch (InvalidOperationException) { return new Settings(); }
        }
        public void Save()
        {
            Directory.CreateDirectory(Folder);
            string path = Path.Combine(Folder, "settings.json"), temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, new JavaScriptSerializer().Serialize(this), new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}

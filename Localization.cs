using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace Traduce
{
    internal static class L
    {
        private static readonly Dictionary<string, string> English = LoadEnglish();
        public static string Language = "es";
        public static string DefaultLanguage { get { return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "es" ? "es" : "en"; } }
        public static string Normalize(string language) { return language == "en" ? "en" : "es"; }
        private static Dictionary<string, string> LoadEnglish()
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Traduce.English"))
            using (var reader = new StreamReader(stream))
                return new JavaScriptSerializer().Deserialize<Dictionary<string, string>>(reader.ReadToEnd());
        }
        public static string T(string spanish)
        {
            string translated;
            return Language == "en" && English.TryGetValue(spanish, out translated) ? translated : spanish;
        }
        // Register source strings once so changing language never translates user content.
        public static void Apply(Control parent)
        {
            if (!(parent is TextBoxBase) && !(parent is ComboBox) && parent.Text.Length > 0)
            {
                if (parent.Tag == null) parent.Tag = Source(parent.Text);
                var source = parent.Tag as string;
                if (source != null && English.ContainsKey(source)) parent.Text = T(source);
            }
            var strip = parent as ToolStrip;
            if (strip != null) foreach (ToolStripItem item in strip.Items)
            {
                if (item.Tag == null) item.Tag = Source(item.Text);
                var source = item.Tag as string;
                if (source != null) item.Text = T(source);
            }
            foreach (Control child in parent.Controls) Apply(child);
        }
        private static string Source(string text)
        {
            if (English.ContainsKey(text)) return text;
            foreach (var pair in English) if (pair.Value == text) return pair.Key;
            return null;
        }
    }
}

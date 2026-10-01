using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;

namespace Traduce
{
    internal sealed class SourceApplication
    {
        public string process = "unknown";
        public int process_id;
        public string window_class = "";

        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder name, int count);
        [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(Point point);

        public static SourceApplication FromWindow(IntPtr window)
        {
            var info = new SourceApplication();
            if (window == IntPtr.Zero) return info;
            uint id; GetWindowThreadProcessId(window, out id); info.process_id = (int)id;
            var name = new StringBuilder(256); GetClassName(window, name, name.Capacity); info.window_class = name.ToString();
            try { using (var process = Process.GetProcessById(info.process_id)) info.process = process.ProcessName + ".exe"; }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
            catch (Win32Exception) { }
            return info;
        }

        public static SourceApplication AtCenter(Rectangle area)
        {
            return FromWindow(WindowFromPoint(new Point(area.Left + area.Width / 2, area.Top + area.Height / 2)));
        }
    }

    internal sealed class CaptureTrace
    {
        public readonly string Id = Guid.NewGuid().ToString("N");
        public readonly string Started = DateTimeOffset.Now.ToString("o");
        public SourceApplication ActiveApplication;
        public SourceApplication RegionApplication;
        public Rectangle Region;
        public string Monitor;
        public Rectangle WorkingArea;
        public Rectangle Popup;
        public string Mode = "none";
        public string Provider;
        public string Model;
        public string OcrReason = "not_started";
        public int Characters;
        public long OcrMilliseconds;
        public long TranslationMilliseconds;
    }

    internal sealed class CaptureLog
    {
        private readonly string folder;
        private readonly object gate = new object();
        public CaptureLog(string directory) { folder = directory; }
        public static string DefaultFolder { get { return Path.Combine(Settings.Folder, "logs"); } }

        private static int[] Rect(Rectangle rectangle) { return new[] { rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height }; }

        public bool Append(CaptureTrace trace, string phase, string outcome, long elapsedMilliseconds, string errorType = null)
        {
            var entry = new Dictionary<string, object> {
                { "schema", 1 }, { "capture_pipeline_version", 1 }, { "timestamp", DateTimeOffset.Now.ToString("o") },
                { "capture_id", trace.Id }, { "started_at", trace.Started }, { "event", phase },
                { "active_app", trace.ActiveApplication }, { "region_app", trace.RegionApplication },
                { "region", Rect(trace.Region) }, { "monitor", trace.Monitor },
                { "working_area", Rect(trace.WorkingArea) }, { "popup", Rect(trace.Popup) },
                { "mode", trace.Mode }, { "ocr_reason", trace.OcrReason },
                { "provider", trace.Provider }, { "model", trace.Model },
                { "text_characters", trace.Characters }, { "ocr_ms", trace.OcrMilliseconds },
                { "translation_ms", trace.TranslationMilliseconds },
                { "outcome", outcome }, { "elapsed_ms", elapsedMilliseconds }, { "error_type", errorType }
            };
            try
            {
                string line = new JavaScriptSerializer().Serialize(entry) + Environment.NewLine;
                lock (gate)
                {
                    Directory.CreateDirectory(folder);
                    File.AppendAllText(Path.Combine(folder, "captures-" + DateTime.Now.ToString("yyyy-MM-dd") + ".jsonl"), line, new UTF8Encoding(false));
                }
                return true;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }
    }
}

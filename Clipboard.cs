using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace Traduce
{
    internal static class Native
    {
        public const int HotkeyId = 0x5452;
        public const int ShowMessage = 0x8002;
        public const int ExitMessage = 0x8003;
        [DllImport("user32.dll", SetLastError = true)] public static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hwnd, int id);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")] public static extern bool OpenClipboard(IntPtr window);
        [DllImport("user32.dll")] public static extern bool CloseClipboard();
        [DllImport("user32.dll")] public static extern IntPtr GetClipboardData(uint format);
        [DllImport("user32.dll")] public static extern bool IsClipboardFormatAvailable(uint format);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern uint RegisterClipboardFormat(string format);
        [DllImport("kernel32.dll")] public static extern IntPtr GlobalLock(IntPtr memory);
        [DllImport("kernel32.dll")] public static extern bool GlobalUnlock(IntPtr memory);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string name, string title);
        [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hwnd, int msg, IntPtr wparam, IntPtr lparam);
        [DllImport("user32.dll", SetLastError = true)] public static extern uint SendInput(uint count, Input[] inputs, int size);
        [StructLayout(LayoutKind.Sequential)] public struct Input { public uint Type; public InputUnion Data; }
        [StructLayout(LayoutKind.Explicit)] public struct InputUnion
        {
            [FieldOffset(0)] public KeyboardInput Keyboard;
            [FieldOffset(0)] public MouseInput Mouse;
        }
        [StructLayout(LayoutKind.Sequential)] public struct KeyboardInput
        {
            public ushort Key, Scan;
            public uint Flags, Time;
            public UIntPtr Extra;
        }
        [StructLayout(LayoutKind.Sequential)] public struct MouseInput
        {
            public int X, Y;
            public uint Data, Flags, Time;
            public UIntPtr Extra;
        }
    }

    internal static class ClipboardAccess
    {
        internal static string ReadText()
        {
            // Read under one native clipboard lock. OLE GetText can transiently return
            // an empty string when another clipboard client is reading concurrently.
            for (int attempt = 0; attempt < 6; attempt++)
            {
                if (!Native.OpenClipboard(IntPtr.Zero)) { Thread.Sleep(15); continue; }
                try
                {
                    IntPtr memory = Native.GetClipboardData(13); // CF_UNICODETEXT
                    if (memory == IntPtr.Zero) return "";
                    IntPtr text = Native.GlobalLock(memory);
                    if (text == IntPtr.Zero) throw new ExternalException(L.T("No se ha podido leer el portapapeles."));
                    try { return Marshal.PtrToStringUni(text) ?? ""; }
                    finally { Native.GlobalUnlock(memory); }
                }
                finally { Native.CloseClipboard(); }
            }
            throw new ExternalException(L.T("El portapapeles está ocupado. Inténtalo de nuevo."));
        }
        internal static DataObject Snapshot()
        {
            var snapshot = new DataObject();
            var original = Clipboard.GetDataObject();
            if (original == null) return snapshot;
            foreach (string format in original.GetFormats(false))
            {
                object value = format == DataFormats.UnicodeText ? ReadText() : original.GetData(format, false);
                var stream = value as MemoryStream;
                if (stream != null) value = new MemoryStream(stream.ToArray());
                var bitmap = value as Bitmap;
                if (bitmap != null) value = bitmap.Clone();
                if (value != null) snapshot.SetData(format, false, value);
            }
            return snapshot;
        }

    }
}

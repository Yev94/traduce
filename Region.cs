using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Traduce
{
    internal sealed class RegionCapture : IDisposable
    {
        public Bitmap Image;
        public Rectangle Bounds;
        public void Dispose() { Image.Dispose(); }
    }

    internal sealed class RegionPicker : Form
    {
        private readonly Bitmap desktop;
        private readonly TaskCompletionSource<RegionCapture> result = new TaskCompletionSource<RegionCapture>(TaskCreationOptions.RunContinuationsAsynchronously);
        private Point anchor, pointer;
        private bool dragging;

        internal RegionPicker(Bitmap snapshot, Rectangle bounds)
        {
            desktop = snapshot;
            Text = L.T("Traduce: seleccionar región");
            FormBorderStyle = FormBorderStyle.None; StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None; Bounds = bounds;
            ShowInTaskbar = false; TopMost = true; DoubleBuffered = true;
            Cursor = Cursors.Cross; KeyPreview = true;
            KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) Close(); };
            FormClosed += delegate { result.TrySetCanceled(); };
        }

        internal static Rectangle SelectionBounds(Point first, Point last, Size size)
        {
            return Rectangle.Intersect(new Rectangle(Point.Empty, size), Rectangle.FromLTRB(
                Math.Min(first.X, last.X), Math.Min(first.Y, last.Y),
                Math.Max(first.X, last.X), Math.Max(first.Y, last.Y)));
        }

        protected override void WndProc(ref Message message)
        {
            // This overlay covers the physical virtual desktop; a monitor DPI
            // change must not rescale the screenshot or its selection coordinates.
            if (message.Msg == 0x02E0) return;
            base.WndProc(ref message);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.DrawImageUnscaled(desktop, 0, 0);
            using (var shade = new SolidBrush(Color.FromArgb(115, 0, 0, 0))) e.Graphics.FillRectangle(shade, ClientRectangle);
            Rectangle area = SelectionBounds(anchor, pointer, desktop.Size);
            if (dragging && area.Width > 0 && area.Height > 0)
            {
                e.Graphics.DrawImage(desktop, area, area, GraphicsUnit.Pixel);
                using (var border = new Pen(Color.FromArgb(104, 226, 195), 2)) e.Graphics.DrawRectangle(border, area);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Right) { Close(); return; }
            if (e.Button != MouseButtons.Left) return;
            anchor = pointer = e.Location; dragging = true; Capture = true; Invalidate();
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (dragging) { pointer = e.Location; Invalidate(); }
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!dragging || e.Button != MouseButtons.Left) return;
            pointer = e.Location; dragging = false; Capture = false;
            Rectangle area = SelectionBounds(anchor, pointer, desktop.Size);
            if (area.Width < 4 || area.Height < 4) { Invalidate(); return; }
            Bitmap crop = desktop.Clone(area, PixelFormat.Format32bppArgb);
            Rectangle screenArea = area; screenArea.Offset(Location);
            Hide(); result.TrySetResult(new RegionCapture { Image = crop, Bounds = screenArea }); Close();
        }

        internal async Task<RegionCapture> Pick(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Show(); Activate(); Native.SetForegroundWindow(Handle);
            using (token.Register(delegate {
                if (!IsDisposed && IsHandleCreated)
                    try { BeginInvoke((Action)delegate { if (!IsDisposed) Close(); }); }
                    catch (InvalidOperationException) { }
            })) return await result.Task;
        }

        public static async Task<RegionCapture> CaptureRegion(CancellationToken token)
        {
            // Let the hidden translation window repaint out of the desktop first.
            await Task.Delay(60, token);
            Rectangle bounds = SystemInformation.VirtualScreen;
            using (var snapshot = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb))
            {
                using (var graphics = Graphics.FromImage(snapshot))
                    graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size, CopyPixelOperation.SourceCopy);
                using (var picker = new RegionPicker(snapshot, bounds)) return await picker.Pick(token);
            }
        }
    }
}

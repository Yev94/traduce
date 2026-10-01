using System;
using System.Drawing;

namespace Traduce
{
    internal static class PopupPlacement
    {
        internal static Rectangle Calculate(Rectangle selection, Size window, Rectangle work, int gap)
        {
            int width = Math.Min(Math.Max(1, selection.Width), work.Width);
            int height = Math.Min(Math.Max(1, window.Height), work.Height);
            Rectangle anchor = Rectangle.Intersect(selection, work);
            if (anchor.IsEmpty) anchor = new Rectangle(
                Clamp(selection.X, work.Left, work.Right - 1), Clamp(selection.Y, work.Top, work.Bottom - 1), 1, 1);
            int above = anchor.Top - work.Top - gap;
            int below = work.Bottom - anchor.Bottom - gap;
            bool placeBelow = below >= above;
            int x = Clamp(anchor.Left + anchor.Width / 2 - width / 2, work.Left, work.Right - width);
            int y = placeBelow ? anchor.Bottom + gap : anchor.Top - gap - height;
            // A near-full-screen selection may leave neither side large enough.
            // Keep the complete window visible, with only the unavoidable overlap.
            y = Clamp(y, work.Top, work.Bottom - height);
            return new Rectangle(x, y, width, height);
        }

        private static int Clamp(int value, int minimum, int maximum) { return Math.Max(minimum, Math.Min(maximum, value)); }
    }
}

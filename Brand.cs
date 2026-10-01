using System.Drawing;
using System.Reflection;

namespace Traduce
{
    internal static class Brand
    {
        internal static Icon Icon()
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Traduce.Icon"))
            using (var icon = new Icon(stream, 32, 32)) return (Icon)icon.Clone();
        }
    }
}

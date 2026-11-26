using System.Drawing;
using System.IO;
using System.Reflection;

namespace NineFlow.Wpf;

/// <summary>内嵌应用图标（托盘与窗口共用）。</summary>
internal static class Assets
{
    public static Icon? AppIcon { get; } = LoadIcon("NineFlow.app.ico");

    private static Icon? LoadIcon(string name)
    {
        try
        {
            using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
            return stream == null ? null : new Icon(stream);
        }
        catch
        {
            return null;
        }
    }
}

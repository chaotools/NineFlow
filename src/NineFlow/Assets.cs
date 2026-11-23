using System.Drawing;
using System.Reflection;

namespace NineFlow;

/// <summary>内嵌资源：应用图标。</summary>
internal static class Assets
{
    public static Icon? AppIcon { get; } = LoadIcon("NineFlow.app.ico");

    private static Stream? Open(string name)
    {
        try
        {
            return Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
        }
        catch
        {
            return null;
        }
    }

    private static Icon? LoadIcon(string name)
    {
        try
        {
            using Stream? stream = Open(name);
            return stream == null ? null : new Icon(stream);
        }
        catch
        {
            return null;
        }
    }
}

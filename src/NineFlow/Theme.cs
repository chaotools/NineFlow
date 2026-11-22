using System.Drawing;
using System.Drawing.Drawing2D;

namespace NineFlow;

/// <summary>苹果风配色、字体与绘制辅助。</summary>
internal static class Theme
{
    // 页面与卡片
    public static readonly Color Page = ColorTranslator.FromHtml("#F5F5F7");
    public static readonly Color Card = Color.White;
    public static readonly Color CardBorder = ColorTranslator.FromHtml("#E8E8ED");
    public static readonly Color Hairline = ColorTranslator.FromHtml("#EAEAEF");

    // 文字
    public static readonly Color TextPrimary = ColorTranslator.FromHtml("#1D1D1F");
    public static readonly Color TextSecondary = ColorTranslator.FromHtml("#6E6E73");
    public static readonly Color TextTertiary = ColorTranslator.FromHtml("#86868B");
    public static readonly Color TextDisabled = ColorTranslator.FromHtml("#B9B9C0");

    // 强调色（macOS system blue）
    public static readonly Color Accent = ColorTranslator.FromHtml("#007AFF");
    public static readonly Color AccentHover = ColorTranslator.FromHtml("#1A88FF");
    public static readonly Color AccentPressed = ColorTranslator.FromHtml("#0062CC");
    public static readonly Color AccentSoft = ColorTranslator.FromHtml("#E9F1FF");

    // 语义色
    public static readonly Color Green = ColorTranslator.FromHtml("#34C759");
    public static readonly Color GreenSoft = ColorTranslator.FromHtml("#E8F8EC");
    public static readonly Color Red = ColorTranslator.FromHtml("#FF3B30");
    public static readonly Color RedSoft = ColorTranslator.FromHtml("#FFECEB");
    public static readonly Color RedPressed = ColorTranslator.FromHtml("#D92D23");
    public static readonly Color Orange = ColorTranslator.FromHtml("#FF9500");
    public static readonly Color OrangeSoft = ColorTranslator.FromHtml("#FFF4E5");

    // 控件
    public static readonly Color FieldFill = Color.White;
    public static readonly Color FieldFillDisabled = ColorTranslator.FromHtml("#F5F5F7");
    public static readonly Color FieldBorder = ColorTranslator.FromHtml("#D2D2D7");
    public static readonly Color FieldBorderFocused = ColorTranslator.FromHtml("#007AFF");
    public static readonly Color SecondaryFill = ColorTranslator.FromHtml("#E9E9EE");
    public static readonly Color SecondaryFillHover = ColorTranslator.FromHtml("#DFDFE6");
    public static readonly Color SecondaryFillPressed = ColorTranslator.FromHtml("#D3D3DB");
    public static readonly Color DisabledFill = ColorTranslator.FromHtml("#F2F2F4");
    public static readonly Color SwitchOff = ColorTranslator.FromHtml("#D9D9DF");

    public static readonly string FontFamily = ResolveFontFamily();

    private static readonly Dictionary<(float Size, FontStyle Style), Font> FontCache = new();

    public static Font Font(float size, FontStyle style = FontStyle.Regular)
    {
        (float, FontStyle) key = (size, style);
        if (FontCache.TryGetValue(key, out Font? cached)) return cached;

        var font = new Font(FontFamily, size, style, GraphicsUnit.Point);
        FontCache[key] = font;
        return font;
    }

    private static string ResolveFontFamily()
    {
        foreach (string candidate in new[] { "Microsoft YaHei UI", "Microsoft YaHei", "Segoe UI", "Tahoma" })
        {
            try
            {
                using var probe = new FontFamily(candidate);
                return candidate;
            }
            catch
            {
                // 换下一个候选字体
            }
        }

        return SystemFonts.DefaultFont.FontFamily.Name;
    }

    public static GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();

        if (bounds.Width <= 0 || bounds.Height <= 0) return path;

        int diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
        if (diameter <= 2)
        {
            path.AddRectangle(bounds);
            return path;
        }

        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// <summary>卡片阴影：几层低透明度圆角矩形叠加，模拟 macOS 的柔和投影。</summary>
    public static void DrawShadow(Graphics graphics, Rectangle bounds, int radius, int spread = 5)
    {
        SmoothingMode previous = graphics.SmoothingMode;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        for (int layer = spread; layer >= 1; layer--)
        {
            var shadowRect = new Rectangle(
                bounds.X - layer,
                bounds.Y - layer + 2,
                bounds.Width + layer * 2,
                bounds.Height + layer * 2);

            using GraphicsPath path = RoundedRect(shadowRect, radius + layer);
            using var brush = new SolidBrush(Color.FromArgb(7, 0, 0, 0));
            graphics.FillPath(brush, path);
        }

        graphics.SmoothingMode = previous;
    }

    /// <summary>带阴影的白色圆角卡片。</summary>
    public static void DrawCard(Graphics graphics, Rectangle bounds, int radius = 14)
    {
        DrawShadow(graphics, bounds, radius);

        using (GraphicsPath path = RoundedRect(bounds, radius))
        {
            using (var fill = new SolidBrush(Card)) graphics.FillPath(fill, path);
            using var border = new Pen(CardBorder);
            graphics.DrawPath(border, path);
        }
    }
}

using NineFlow.Core;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace NineFlow;

/// <summary>苹果风白色圆角卡片（无边框投影，靠浅色描边区分层次）。</summary>
internal sealed class CardPanel : Panel
{
    public CardPanel()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.UserPaint
            | ControlStyles.ResizeRedraw,
            true);

        BackColor = Theme.Page;
        CornerRadius = 14;
        DoubleBuffered = true;
    }

    public int CornerRadius { get; set; }

    protected override void OnPaint(PaintEventArgs e)
    {
        // 先合成到不透明位图再整块贴出，避免圆角抗锯齿轮廓和未绘制像素混合出现黑边
        Theme.RenderOpaque(e.Graphics, Size, Theme.Page, g =>
        {
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using GraphicsPath path = Theme.RoundedRect(rect, CornerRadius);
            using (var fill = new SolidBrush(Theme.Card))
            {
                g.FillPath(fill, path);
            }

            using var border = new Pen(Theme.CardBorder);
            g.DrawPath(border, path);
        });
    }
}

internal enum AppButtonKind
{
    Primary,
    Secondary,
    Danger,
}

/// <summary>苹果风按钮：无边框、纯色填充、圆角。</summary>
internal sealed class AppButton : Button
{
    private bool _hover;
    private bool _pressed;

    public AppButton()
    {
        SetStyle(
            ControlStyles.UserPaint
            | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw,
            true);

        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false;
        BackColor = Theme.Page;
        Font = Theme.Font(9.5f, FontStyle.Bold);
        Cursor = Cursors.Hand;
        TabStop = false;
        Height = 40;
        DoubleBuffered = true;
    }

    public AppButtonKind Kind { get; set; } = AppButtonKind.Secondary;

    public int CornerRadius { get; set; } = 10;

    protected override void OnPaintBackground(PaintEventArgs pevent)
    {
        using var brush = new SolidBrush(Parent?.BackColor ?? Theme.Page);
        pevent.Graphics.FillRectangle(brush, ClientRectangle);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Color surround = Parent?.BackColor ?? Theme.Page;
        (Color back, Color fore) = ResolveColors();
        var rect = new Rectangle(0, 0, Width, Height);

        // 圆角背景先合成到不透明位图再整块贴出：抗锯齿只与周围色混合，不会出现黑边
        Theme.RenderOpaque(e.Graphics, Size, surround, g =>
        {
            using GraphicsPath path = Theme.RoundedRect(rect, CornerRadius);
            using var fill = new SolidBrush(back);
            g.FillPath(fill, path);
        });

        // 文字直接画在窗口表面（不经过位图），保持 ClearType 清晰度
        TextRenderer.DrawText(
            e.Graphics,
            Text,
            Font,
            rect,
            fore,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    private (Color Back, Color Fore) ResolveColors()
    {
        if (!Enabled) return (Theme.DisabledFill, Theme.TextDisabled);

        return Kind switch
        {
            AppButtonKind.Primary => _pressed
                ? (Theme.AccentPressed, Color.White)
                : _hover
                    ? (Theme.AccentHover, Color.White)
                    : (Theme.Accent, Color.White),
            AppButtonKind.Danger => _pressed
                ? (Theme.RedPressed, Color.White)
                : _hover
                    ? (Theme.Red, Color.White)
                    : (Theme.RedSoft, Theme.Red),
            _ => _pressed
                ? (Theme.SecondaryFillPressed, Theme.TextPrimary)
                : _hover
                    ? (Theme.SecondaryFillHover, Theme.TextPrimary)
                    : (Theme.SecondaryFill, Theme.TextPrimary),
        };
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hover = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = false;
        _pressed = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs mevent)
    {
        _pressed = true;
        Invalidate();
        base.OnMouseDown(mevent);
    }

    protected override void OnMouseUp(MouseEventArgs mevent)
    {
        _pressed = false;
        Invalidate();
        base.OnMouseUp(mevent);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        Invalidate();
        base.OnEnabledChanged(e);
    }
}

/// <summary>苹果风开关（用 CheckBox 派生，保留系统的无障碍/自动化支持）。</summary>
internal sealed class AppSwitch : CheckBox
{
    private bool _hover;

    public AppSwitch()
    {
        SetStyle(
            ControlStyles.UserPaint
            | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw,
            true);

        AutoSize = false;
        Size = new Size(44, 26);
        Text = string.Empty;
        Cursor = Cursors.Hand;
        FlatStyle = FlatStyle.Flat;
        BackColor = Theme.Card;
        DoubleBuffered = true;
    }

    public Color SurfaceColor { get; set; } = Theme.Card;

    protected override void OnPaintBackground(PaintEventArgs pevent)
    {
        using var brush = new SolidBrush(SurfaceColor);
        pevent.Graphics.FillRectangle(brush, ClientRectangle);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Theme.RenderOpaque(e.Graphics, Size, SurfaceColor, g =>
        {
        int trackWidth = 44;
        int trackHeight = 26;
        var track = new Rectangle((Width - trackWidth) / 2, (Height - trackHeight) / 2, trackWidth - 1, trackHeight - 1);

        bool on = Checked;
        Color trackColor;
        if (!Enabled) trackColor = Theme.DisabledFill;
        else if (on) trackColor = _hover ? Theme.AccentHover : Theme.Accent;
        else trackColor = _hover ? ColorTranslator.FromHtml("#C9C9D1") : Theme.SwitchOff;

        using (GraphicsPath path = Theme.RoundedRect(track, trackHeight / 2))
        {
            using var fill = new SolidBrush(trackColor);
            g.FillPath(fill, path);
        }

        const int knob = 22;
        int knobX = on ? track.Right - knob - 2 : track.X + 2;
        int knobY = track.Y + (track.Height - knob) / 2;
        var knobRect = new Rectangle(knobX, knobY, knob, knob);

        using (var shadow = new SolidBrush(Color.FromArgb(38, 0, 0, 0)))
        {
            g.FillEllipse(shadow, new Rectangle(knobX, knobY + 1, knob, knob));
        }

        using (var knobBrush = new SolidBrush(Enabled ? Color.White : ColorTranslator.FromHtml("#F7F7F9")))
        {
            g.FillEllipse(knobBrush, knobRect);
        }
        });
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hover = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnCheckedChanged(EventArgs e)
    {
        Invalidate();
        base.OnCheckedChanged(e);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        Invalidate();
        base.OnEnabledChanged(e);
    }
}

/// <summary>苹果风圆角输入框（外层自绘边框，内嵌无边框 TextBox）。</summary>
internal class AppTextField : Panel
{
    private readonly TextBox _editor;
    private bool _focused;

    public AppTextField()
    {
        SetStyle(
            ControlStyles.UserPaint
            | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw,
            true);

        BackColor = Theme.Card;

        _editor = new TextBox
        {
            BorderStyle = BorderStyle.None,
            Font = Theme.Font(9.5f),
            BackColor = Theme.FieldFill,
            ForeColor = Theme.TextPrimary,
        };

        _editor.TextChanged += (_, _) => OnTextChanged(EventArgs.Empty);
        _editor.GotFocus += (_, _) =>
        {
            _focused = true;
            Invalidate();
        };
        _editor.LostFocus += (_, _) =>
        {
            _focused = false;
            Invalidate();
        };

        Controls.Add(_editor);

        // 注意：必须在 _editor 创建之后再设置高度，否则 OnSizeChanged 会访问空引用
        Height = 32;
    }

    public Color SurfaceColor
    {
        get => BackColor;
        set
        {
            if (BackColor == value) return;
            BackColor = value;
            Invalidate();
        }
    }

    public TextBox Editor => _editor;

    public HorizontalAlignment TextAlign
    {
        get => _editor.TextAlign;
        set => _editor.TextAlign = value;
    }

    public string Value
    {
        get => _editor.Text;
        set => _editor.Text = value ?? string.Empty;
    }

    public string Placeholder
    {
        get => _editor.PlaceholderText;
        set => _editor.PlaceholderText = value;
    }

    public int Radius { get; set; } = 8;

    public int RightInset { get; set; } = 10;

    private Color _valueColor = Theme.TextPrimary;

    /// <summary>文字颜色（未启用状态下会自动变浅）。</summary>
    public Color ValueColor
    {
        get => _valueColor;
        set
        {
            _valueColor = value;
            _editor.ForeColor = Enabled ? value : Theme.TextDisabled;
        }
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        PositionEditor();
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        PositionEditor();
    }

    private void PositionEditor()
    {
        const int left = 12;
        int right = Width - RightInset;
        int editorHeight = _editor.PreferredHeight;
        _editor.Bounds = new Rectangle(left, (Height - editorHeight) / 2, Math.Max(10, right - left), editorHeight);
    }

    protected override void OnPaintBackground(PaintEventArgs pevent)
    {
        using var brush = new SolidBrush(SurfaceColor);
        pevent.Graphics.FillRectangle(brush, ClientRectangle);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Theme.RenderOpaque(e.Graphics, Size, SurfaceColor, g =>
        {
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using GraphicsPath path = Theme.RoundedRect(rect, Radius);

            Color fill = Enabled ? Theme.FieldFill : Theme.FieldFillDisabled;
            using (var brush = new SolidBrush(fill))
            {
                g.FillPath(brush, path);
            }

            Color borderColor = !Enabled
                ? Theme.CardBorder
                : _focused
                    ? Theme.FieldBorderFocused
                    : Theme.FieldBorder;

            using var border = new Pen(borderColor, _focused ? 1.6f : 1f);
            g.DrawPath(border, path);

            if (_focused && Enabled)
            {
                var ringRect = new Rectangle(-2, -2, Width + 3, Height + 3);
                using GraphicsPath ringPath = Theme.RoundedRect(ringRect, Radius + 2);
                using var ring = new Pen(Color.FromArgb(50, Theme.Accent), 2f);
                g.DrawPath(ring, ringPath);
            }
        });
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (Enabled) _editor.Focus();
        base.OnMouseDown(e);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        _editor.Enabled = Enabled;
        _editor.BackColor = Enabled ? Theme.FieldFill : Theme.FieldFillDisabled;
        _editor.ForeColor = Enabled ? _valueColor : Theme.TextDisabled;
        Invalidate();
        base.OnEnabledChanged(e);
    }
}

/// <summary>按键输入框：右侧带下拉箭头，提供常用按键候选。</summary>
internal sealed class KeyField : AppTextField
{
    private static readonly string[] Suggestions =
    {
        "Down", "Up", "Left", "Right", "Enter", "Space", "Tab", "Esc",
        "Home", "End", "PgUp", "PgDn", "Backspace", "Delete",
        "^a", "^c", "^v", "!q", "+s", "Ctrl+A", "F4", "F5", "LButton", "WheelUp",
    };

    private bool _chevronHot;

    public KeyField()
    {
        RightInset = 30;
        Placeholder = UiText.T("row.placeholder");
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        int cx = Width - 17;
        int cy = Height / 2;
        Color color = !Enabled ? Theme.TextDisabled : _chevronHot ? Theme.Accent : Theme.TextTertiary;

        using var pen = new Pen(color, 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(pen, cx - 4, cy - 2, cx, cy + 2);
        g.DrawLine(pen, cx, cy + 2, cx + 4, cy - 2);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        bool hot = e.X >= Width - 30;
        if (hot != _chevronHot)
        {
            _chevronHot = hot;
            Cursor = hot ? Cursors.Hand : Cursors.IBeam;
            Invalidate();
        }

        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        if (_chevronHot)
        {
            _chevronHot = false;
            Cursor = Cursors.IBeam;
            Invalidate();
        }

        base.OnMouseLeave(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && e.X >= Width - 30)
        {
            ShowSuggestions();
            return;
        }

        base.OnMouseUp(e);
    }

    private void ShowSuggestions()
    {
        var menu = new ContextMenuStrip
        {
            Font = Theme.Font(9.5f),
            ShowImageMargin = false,
            BackColor = Color.White,
        };

        foreach (string suggestion in Suggestions)
        {
            var item = new ToolStripMenuItem(suggestion);
            item.Click += (_, _) =>
            {
                Value = suggestion;
                Editor.Focus();
                Editor.SelectionStart = Editor.TextLength;
            };
            menu.Items.Add(item);
        }

        menu.Closed += (_, _) => menu.Dispose();
        menu.Show(this, new Point(0, Height + 2));
    }
}

/// <summary>顶部标题区：Logo + 名称 + 副标题 + 状态胶囊。</summary>
internal sealed class HeaderPanel : Panel
{
    private Image? _logo;
    private bool _running;

    public HeaderPanel()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.UserPaint
            | ControlStyles.ResizeRedraw,
            true);

        BackColor = Theme.Page;
        Height = 92;
    }

    public Image? Logo
    {
        get => _logo;
        set
        {
            _logo = value;
            Invalidate();
        }
    }

    public bool Running
    {
        get => _running;
        set
        {
            if (_running == value) return;
            _running = value;
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        using (var background = new SolidBrush(Theme.Page))
        {
            g.FillRectangle(background, ClientRectangle);
        }

        if (_logo != null)
        {
            Rectangle target = FitRect(_logo.Size, new Rectangle(24, 18, 58, 58));
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(_logo, target);
        }

        TextRenderer.DrawText(
            g,
            UiText.T("app.name"),
            Theme.Font(21f, FontStyle.Bold),
            new Point(94, 12),
            Theme.TextPrimary,
            TextFormatFlags.NoPadding);

        TextRenderer.DrawText(
            g,
            UiText.T("app.subtitle"),
            Theme.Font(9f),
            new Point(96, 54),
            Theme.TextTertiary,
            TextFormatFlags.NoPadding);

        DrawPill(g);
    }

    private void DrawPill(Graphics g)
    {
        const int pillWidth = 104;
        const int pillHeight = 30;

        int x = Width - pillWidth - 24;
        int y = (Height - pillHeight) / 2;
        var rect = new Rectangle(x, y, pillWidth, pillHeight);

        Color back = _running ? Theme.GreenSoft : Theme.SecondaryFill;
        Color fore = _running ? Theme.Green : Theme.TextSecondary;

        using (GraphicsPath path = Theme.RoundedRect(rect, pillHeight / 2))
        {
            using var brush = new SolidBrush(back);
            g.FillPath(brush, path);
        }

        const int dot = 8;
        using (var dotBrush = new SolidBrush(fore))
        {
            g.FillEllipse(dotBrush, x + 14, y + (pillHeight - dot) / 2, dot, dot);
        }

        TextRenderer.DrawText(
            g,
            _running ? UiText.T("pill.running") : UiText.T("pill.stopped"),
            Theme.Font(9f, FontStyle.Bold),
            new Rectangle(x + 28, y, pillWidth - 32, pillHeight),
            fore,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding);
    }

    private static Rectangle FitRect(Size imageSize, Rectangle box)
    {
        if (imageSize.Width <= 0 || imageSize.Height <= 0) return box;

        float scale = Math.Min((float)box.Width / imageSize.Width, (float)box.Height / imageSize.Height);
        int width = Math.Max(1, (int)Math.Round(imageSize.Width * scale));
        int height = Math.Max(1, (int)Math.Round(imageSize.Height * scale));

        return new Rectangle(
            box.X + (box.Width - width) / 2,
            box.Y + (box.Height - height) / 2,
            width,
            height);
    }
}

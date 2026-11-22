namespace NineFlow.Core;

/// <summary>
/// 按键名 → 虚拟键码映射。支持常见写法（Down / Enter / F7 / Volume_Up ...），
/// 查找时忽略大小写、空格、下划线。
/// </summary>
public static class KeyNames
{
    public const int MaxNameLength = 18;

    private static readonly Dictionary<string, (ushort Vk, bool Extended)> KeyMap =
        new(StringComparer.Ordinal);

    private static readonly Dictionary<string, MouseStroke> MouseMap =
        new(StringComparer.Ordinal);

    private static readonly Dictionary<ushort, string> DisplayMap = new();

    static KeyNames()
    {
        // 功能键 F1 - F24
        for (int i = 1; i <= 24; i++)
        {
            ushort vk = (ushort)(0x70 + i - 1);
            AddKey("F" + i, vk, false);
            DisplayMap[vk] = "F" + i;
        }

        // 字母
        for (char c = 'A'; c <= 'Z'; c++)
        {
            AddKey(c.ToString(), c, false);
            DisplayMap[c] = c.ToString();
        }

        // 数字（主键盘）
        for (char c = '0'; c <= '9'; c++)
        {
            AddKey(c.ToString(), c, false);
            DisplayMap[c] = c.ToString();
        }

        // 常用控制键
        AddKey("Space", 0x20, false); DisplayMap[0x20] = "Space";
        AddKey("Tab", 0x09, false); DisplayMap[0x09] = "Tab";
        AddKey("Enter", 0x0D, false); AddKey("Return", 0x0D, false); DisplayMap[0x0D] = "Enter";
        AddKey("Escape", 0x1B, false); AddKey("Esc", 0x1B, false); DisplayMap[0x1B] = "Esc";
        AddKey("Backspace", 0x08, false); AddKey("BS", 0x08, false); DisplayMap[0x08] = "Backspace";
        AddKey("Delete", 0x2E, true); AddKey("Del", 0x2E, true); DisplayMap[0x2E] = "Delete";
        AddKey("Insert", 0x2D, true); AddKey("Ins", 0x2D, true); DisplayMap[0x2D] = "Insert";
        AddKey("Home", 0x24, true); DisplayMap[0x24] = "Home";
        AddKey("End", 0x23, true); DisplayMap[0x23] = "End";
        AddKey("PgUp", 0x21, true); AddKey("PageUp", 0x21, true); AddKey("Prior", 0x21, true); DisplayMap[0x21] = "PgUp";
        AddKey("PgDn", 0x22, true); AddKey("PageDown", 0x22, true); AddKey("Next", 0x22, true); DisplayMap[0x22] = "PgDn";
        AddKey("Up", 0x26, true); DisplayMap[0x26] = "Up";
        AddKey("Down", 0x28, true); DisplayMap[0x28] = "Down";
        AddKey("Left", 0x25, true); DisplayMap[0x25] = "Left";
        AddKey("Right", 0x27, true); DisplayMap[0x27] = "Right";
        AddKey("CapsLock", 0x14, false); DisplayMap[0x14] = "CapsLock";
        AddKey("NumLock", 0x90, true); DisplayMap[0x90] = "NumLock";
        AddKey("ScrollLock", 0x91, false); DisplayMap[0x91] = "ScrollLock";
        AddKey("PrintScreen", 0x2C, true); AddKey("PrintScr", 0x2C, true); AddKey("PrtSc", 0x2C, true); DisplayMap[0x2C] = "PrintScreen";
        AddKey("Pause", 0x13, false); AddKey("Break", 0x13, false); DisplayMap[0x13] = "Pause";
        AddKey("AppsKey", 0x5D, true); AddKey("Menu", 0x5D, true); AddKey("Apps", 0x5D, true); DisplayMap[0x5D] = "AppsKey";
        AddKey("Sleep", 0x5F, false); DisplayMap[0x5F] = "Sleep";

        // 小键盘
        for (int i = 0; i <= 9; i++)
        {
            ushort vk = (ushort)(0x60 + i);
            AddKey("Numpad" + i, vk, false);
            DisplayMap[vk] = "Numpad" + i;
        }
        AddKey("NumpadClear", 0x0C, false);
        AddKey("NumpadDiv", 0x6F, true); AddKey("NumpadDivide", 0x6F, true); DisplayMap[0x6F] = "NumpadDiv";
        AddKey("NumpadMult", 0x6A, false); AddKey("NumpadMultiply", 0x6A, false); DisplayMap[0x6A] = "NumpadMult";
        AddKey("NumpadSub", 0x6D, false); AddKey("NumpadSubtract", 0x6D, false); DisplayMap[0x6D] = "NumpadSub";
        AddKey("NumpadAdd", 0x6B, false); DisplayMap[0x6B] = "NumpadAdd";
        AddKey("NumpadDot", 0x6E, false); AddKey("NumpadDecimal", 0x6E, false); AddKey("NumpadDel", 0x6E, false); DisplayMap[0x6E] = "NumpadDot";
        AddKey("NumpadEnter", 0x0D, true);

        // 修饰键本身
        AddKey("Shift", 0x10, false); AddKey("LShift", 0xA0, false); AddKey("RShift", 0xA1, false);
        AddKey("Ctrl", 0x11, false); AddKey("Control", 0x11, false); AddKey("LCtrl", 0xA2, false); AddKey("RCtrl", 0xA3, true);
        AddKey("Alt", 0x12, false); AddKey("LAlt", 0xA4, false); AddKey("RAlt", 0xA5, true);
        AddKey("LWin", 0x5B, true); AddKey("RWin", 0x5C, true); AddKey("Win", 0x5B, true);
        DisplayMap[0x10] = "Shift"; DisplayMap[0x11] = "Ctrl"; DisplayMap[0x12] = "Alt";

        // 多媒体 / 浏览器
        AddKey("Volume_Mute", 0xAD, false); AddKey("VolumeMute", 0xAD, false);
        AddKey("Volume_Down", 0xAE, false); AddKey("VolumeDown", 0xAE, false);
        AddKey("Volume_Up", 0xAF, false); AddKey("VolumeUp", 0xAF, false);
        AddKey("Media_Next", 0xB0, false); AddKey("MediaNextTrack", 0xB0, false);
        AddKey("Media_Prev", 0xB1, false); AddKey("MediaPrevTrack", 0xB1, false);
        AddKey("Media_Stop", 0xB2, false); AddKey("MediaStop", 0xB2, false);
        AddKey("Media_Play_Pause", 0xB3, false); AddKey("MediaPlayPause", 0xB3, false);
        AddKey("Launch_Mail", 0xB4, false);
        AddKey("Launch_Media", 0xB5, false);
        AddKey("Launch_App1", 0xB6, false);
        AddKey("Launch_App2", 0xB7, false);
        AddKey("Browser_Back", 0xA6, false);
        AddKey("Browser_Forward", 0xA7, false);
        AddKey("Browser_Refresh", 0xA8, false);
        AddKey("Browser_Stop", 0xA9, false);
        AddKey("Browser_Search", 0xAA, false);
        AddKey("Browser_Favorites", 0xAB, false);
        AddKey("Browser_Home", 0xAC, false);

        // 鼠标
        AddMouse("LButton", MouseStroke.Left);
        AddMouse("LeftButton", MouseStroke.Left);
        AddMouse("RButton", MouseStroke.Right);
        AddMouse("RightButton", MouseStroke.Right);
        AddMouse("MButton", MouseStroke.Middle);
        AddMouse("MiddleButton", MouseStroke.Middle);
        AddMouse("XButton1", MouseStroke.X1);
        AddMouse("XButton2", MouseStroke.X2);
        AddMouse("WheelUp", MouseStroke.WheelUp);
        AddMouse("WheelDown", MouseStroke.WheelDown);
        AddMouse("WheelLeft", MouseStroke.WheelLeft);
        AddMouse("WheelRight", MouseStroke.WheelRight);
    }

    private static void AddMouse(string name, MouseStroke stroke)
    {
        MouseMap.TryAdd(Normalize(name), stroke);
    }

    private static void AddKey(string name, ushort vk, bool extended)
    {
        string norm = Normalize(name);
        // 先加入者优先（避免别名覆盖主名）
        KeyMap.TryAdd(norm, (vk, extended));
    }

    private static string Normalize(string name)
    {
        if (string.IsNullOrEmpty(name)) return string.Empty;
        Span<char> buf = stackalloc char[name.Length];
        int n = 0;
        foreach (char c in name)
        {
            if (c is ' ' or '_' or '-' or '\t') continue;
            buf[n++] = char.ToLowerInvariant(c);
        }
        return new string(buf[..n]);
    }

    /// <summary>按键名是否是已知的键盘按键。</summary>
    public static bool IsKnownKey(string name) => KeyMap.ContainsKey(Normalize(name));

    /// <summary>按键名是否是已知的鼠标动作。</summary>
    public static bool IsMouseName(string name) => MouseMap.ContainsKey(Normalize(name));

    /// <summary>解析按键名（可能是鼠标动作），生成一个发送动作。</summary>
    public static bool TryGetNamedKey(string name, uint modifiers, out SendToken token)
    {
        token = null!;
        string norm = Normalize(name);

        if (MouseMap.TryGetValue(norm, out var mouse))
        {
            token = SendToken.ForMouse(mouse, modifiers);
            return true;
        }

        if (KeyMap.TryGetValue(norm, out var info))
        {
            token = SendToken.ForKey(info.Vk, info.Extended, modifiers);
            return true;
        }

        return false;
    }

    /// <summary>解析按键名到虚拟键码（用于注册热键）。</summary>
    public static bool TryGetVirtualKey(string name, out ushort vk, out bool extended)
    {
        string norm = Normalize(name);
        if (KeyMap.TryGetValue(norm, out var info))
        {
            vk = info.Vk;
            extended = info.Extended;
            return true;
        }

        vk = 0;
        extended = false;
        return false;
    }

    /// <summary>虚拟键码 → 展示名（未知键会返回 VKxx 形式的占位文本）。</summary>
    public static string GetDisplayName(ushort vk)
    {
        if (DisplayMap.TryGetValue(vk, out var name)) return name;
        return "VK" + vk.ToString("X2");
    }

    /// <summary>虚拟键码 → 展示名；未知键返回 false（按键捕获需要据此判断能否识别）。</summary>
    public static bool TryGetDisplayName(ushort vk, out string name) => DisplayMap.TryGetValue(vk, out name!);
}

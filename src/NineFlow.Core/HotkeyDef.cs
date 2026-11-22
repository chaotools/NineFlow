namespace NineFlow.Core;

/// <summary>
/// 全局快捷键定义。支持组合键写法（F7 / ^F7 / !q / +#s）和常见写法（Ctrl+F7）。
/// </summary>
public sealed class HotkeyDef
{
    public uint Modifiers { get; }

    public uint VirtualKey { get; }

    /// <summary>展示用文本，如 Ctrl+F7。</summary>
    public string Display { get; }

    private HotkeyDef(uint modifiers, uint virtualKey, string display)
    {
        Modifiers = modifiers;
        VirtualKey = virtualKey;
        Display = display;
    }

    public static bool TryParse(string? text, out HotkeyDef? def, out string error)
    {
        def = null;
        error = string.Empty;

        string s = (text ?? string.Empty).Trim();
        if (s.Length == 0)
        {
            error = "快捷键不能为空（例如 F7 或 Ctrl+F7）";
            return false;
        }

        // Ctrl+F7 风格
        if (TryParseFriendly(s, out uint friendlyMods, out string keyPart, out error))
        {
            if (error.Length > 0) return false;
            return Build(friendlyMods, keyPart, out def, out error);
        }

        // 修饰符前缀（^ Ctrl、! Alt、+ Shift、# Win）
        uint mods = 0;
        int i = 0;
        while (i < s.Length)
        {
            switch (s[i])
            {
                case '^': mods |= SendSpec.ModControl; i++; continue;
                case '!': mods |= SendSpec.ModAlt; i++; continue;
                case '+': mods |= SendSpec.ModShift; i++; continue;
                case '#': mods |= SendSpec.ModWin; i++; continue;
                default: break;
            }

            break;
        }

        keyPart = s[i..].Trim();
        if (keyPart.Length == 0)
        {
            error = "快捷键缺少按键（例如 F7 或 ^F7）";
            return false;
        }

        return Build(mods, keyPart, out def, out error);
    }

    private static bool TryParseFriendly(string s, out uint mods, out string keyPart, out string error)
    {
        mods = 0;
        keyPart = string.Empty;
        error = string.Empty;

        if (s.IndexOf('+') < 0) return false;

        string[] raw = s.Split('+');
        if (raw.Length < 2) return false;

        for (int i = 0; i < raw.Length - 1; i++)
        {
            string m = raw[i].Trim().ToLowerInvariant();
            uint bit = m switch
            {
                "ctrl" or "control" or "lctrl" or "rctrl" => SendSpec.ModControl,
                "alt" or "lalt" or "ralt" => SendSpec.ModAlt,
                "shift" or "lshift" or "rshift" => SendSpec.ModShift,
                "win" or "windows" or "lwin" or "rwin" => SendSpec.ModWin,
                _ => 0u,
            };

            if (bit == 0)
            {
                // 不是修饰键前缀，交回普通解析
                return false;
            }

            mods |= bit;
        }

        keyPart = raw[^1].Trim();
        if (keyPart.Length == 0)
        {
            error = "快捷键缺少按键（例如 F7 或 Ctrl+F7）";
            return true;
        }

        return true;
    }

    private static bool Build(uint mods, string keyPart, out HotkeyDef? def, out string error)
    {
        def = null;
        error = string.Empty;

        if (!TryResolveKey(keyPart, ref mods, out uint vk, out error)) return false;

        // 注册成全局热键后，没有修饰键的普通按键会被系统整个吞掉。
        // 万一填成 Down / a / 2，全系统的这个键就失灵了，所以只放行 F1–F24。
        if (mods == 0 && !IsFunctionKey(vk))
        {
            error = $"“{KeyNames.GetDisplayName((ushort)vk)}”单独做全局热键会吞掉系统中的这个按键，"
                + "请改用 F1–F24，或加一个修饰键（Ctrl / Alt / Shift / Win）";
            return false;
        }

        def = new HotkeyDef(mods, vk, Format(mods, vk));
        return true;
    }

    /// <summary>是否 F1–F24（虚拟键码 0x70–0x87）。</summary>
    private static bool IsFunctionKey(uint vk) => vk >= 0x70 && vk <= 0x87;

    private static bool TryResolveKey(string keyPart, ref uint mods, out uint vk, out string error)
    {
        vk = 0;
        error = string.Empty;

        string key = keyPart.Trim();
        if (key.Length >= 2 && key[0] == '{' && key[^1] == '}') key = key[1..^1].Trim();

        if (key.Length == 0)
        {
            error = "快捷键缺少按键（例如 F7 或 Ctrl+F7）";
            return false;
        }

        if (KeyNames.IsMouseName(key))
        {
            error = "快捷键不支持鼠标按键";
            return false;
        }

        if (KeyNames.TryGetVirtualKey(key, out ushort named, out _))
        {
            vk = named;
            return true;
        }

        if (key.Length == 1)
        {
            short scan = NativeMethods.VkKeyScanEx(key[0], NativeMethods.GetKeyboardLayout(0));
            if (scan == -1)
            {
                error = $"无法识别的按键“{key}”";
                return false;
            }

            vk = (uint)(scan & 0xFF);
            byte state = (byte)((scan >> 8) & 0xFF);
            if ((state & 0x01) != 0) mods |= SendSpec.ModShift;
            if ((state & 0x02) != 0) mods |= SendSpec.ModControl;
            if ((state & 0x04) != 0) mods |= SendSpec.ModAlt;
            return true;
        }

        error = $"无法识别的按键名“{key}”";
        return false;
    }

    private static string Format(uint mods, uint vk)
    {
        var sb = new System.Text.StringBuilder();
        if ((mods & SendSpec.ModControl) != 0) sb.Append("Ctrl+");
        if ((mods & SendSpec.ModAlt) != 0) sb.Append("Alt+");
        if ((mods & SendSpec.ModShift) != 0) sb.Append("Shift+");
        if ((mods & SendSpec.ModWin) != 0) sb.Append("Win+");
        sb.Append(KeyNames.GetDisplayName((ushort)vk));
        return sb.ToString();
    }

    public override string ToString() => Display;
}

using System.Globalization;

namespace NineFlow.Core;

/// <summary>
/// 把界面上的组合按键文本解析成发送动作，兼容常见的按键写法，
/// 并额外接受 Ctrl+A 这类常见写法。
///
/// 支持：
///   Down / Enter / F7 / Space …    普通按键名
///   a / 2 / 中                       单个字符
///   ^a  !q  +s  #r                    修饰符前缀（Ctrl/Alt/Shift/Win）
///   ^+{Tab}  !{F4}                   带花括号的组合键
///   {Tab}{Tab}                       连续动作
///   Ctrl+A / Alt+F4 / Ctrl+Shift+S   常见组合键写法
///   abc                              连续输入多个字符
///   Down,Enter                       逗号/竖线分隔的连续动作
///   LButton / RButton / WheelUp      鼠标动作
/// </summary>
public static class SendSpec
{
    public const uint ModAlt = 0x0001;
    public const uint ModControl = 0x0002;
    public const uint ModShift = 0x0004;
    public const uint ModWin = 0x0008;

    /// <summary>解析组合按键文本。</summary>
    public static bool TryParse(string? text, out List<SendToken> tokens, out string error)
    {
        tokens = new List<SendToken>(4);
        error = string.Empty;

        string s = (text ?? string.Empty).Trim();
        if (s.Length == 0)
        {
            error = "按键不能为空";
            return false;
        }

        // 逗号 / 竖线分隔的多个动作
        if (TrySplitTopLevel(s, out var parts))
        {
            foreach (string part in parts)
            {
                string trimmed = part.Trim();
                if (trimmed.Length == 0) continue;
                if (!TryParse(trimmed, out var sub, out error)) return false;
                tokens.AddRange(sub);
            }

            if (tokens.Count == 0)
            {
                error = "按键不能为空";
                return false;
            }

            return true;
        }

        // 以修饰符或花括号开头
        if (s[0] == '{' || TryMapModifier(s[0], out _))
        {
            return ParseSequence(s, tokens, out error);
        }

        // Ctrl+A / Alt+F4 之类
        if (TryParseFriendlyCombo(s, out var combo, out string comboError))
        {
            if (combo != null)
            {
                tokens.AddRange(combo);
                return true;
            }

            error = comboError;
            return false;
        }

        // 单个字符
        if (s.Length == 1)
        {
            if (char.IsControl(s[0]))
            {
                error = $"无法发送控制字符“{s}”";
                return false;
            }

            tokens.Add(SendToken.ForCharacter(s[0]));
            return true;
        }

        // 普通按键名（Down / Enter / F7 / LButton …）
        if (KeyNames.TryGetNamedKey(s, 0, out var named))
        {
            tokens.Add(named);
            return true;
        }

        // 其余当成连续输入的文字
        return TryTypeText(s, tokens, out error);
    }

    /// <summary>解析后返回可读的动作串，用于界面提示。</summary>
    public static string Describe(IReadOnlyList<SendToken> tokens)
    {
        if (tokens.Count == 0) return string.Empty;
        return string.Join(" ", tokens.Select(token =>
        {
            if (token.Kind == StrokeKind.Mouse) return FormatModifiers(token.Modifiers) + token.Mouse;
            string body = token.IsCharacter ? token.Character.ToString() : KeyNames.GetDisplayName(token.VirtualKey);
            return FormatModifiers(token.Modifiers) + body;
        }));
    }

    private static string FormatModifiers(uint mods)
    {
        var sb = new System.Text.StringBuilder();
        if ((mods & ModControl) != 0) sb.Append("Ctrl+");
        if ((mods & ModAlt) != 0) sb.Append("Alt+");
        if ((mods & ModShift) != 0) sb.Append("Shift+");
        if ((mods & ModWin) != 0) sb.Append("Win+");
        return sb.ToString();
    }

    private static bool TrySplitTopLevel(string s, out List<string> parts)
    {
        parts = new List<string>();
        int depth = 0;
        int start = 0;

        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '{')
            {
                depth++;
            }
            else if (c == '}')
            {
                if (depth > 0) depth--;
            }
            else if ((c == ',' || c == '|') && depth == 0)
            {
                parts.Add(s.Substring(start, i - start));
                start = i + 1;
            }
        }

        if (parts.Count == 0) return false;
        parts.Add(s.Substring(start));
        return true;
    }

    private static bool ParseSequence(string s, List<SendToken> tokens, out string error)
    {
        error = string.Empty;
        int i = 0;

        while (i < s.Length)
        {
            uint mods = 0;
            while (i < s.Length && TryMapModifier(s[i], out uint mod))
            {
                mods |= mod;
                i++;
            }

            if (i >= s.Length)
            {
                error = "组合键不完整：缺少按键部分（如需发送 + 号本身，请写成 {+}）";
                return false;
            }

            if (s[i] == '{')
            {
                int close = s.IndexOf('}', i + 1);
                if (close < 0)
                {
                    error = "组合键缺少右花括号 }";
                    return false;
                }

                string name = s.Substring(i + 1, close - i - 1).Trim();
                i = close + 1;

                if (name.Length == 0)
                {
                    error = "花括号内不能为空，如 {Enter}";
                    return false;
                }

                if (name.Length == 1)
                {
                    if (char.IsControl(name[0]))
                    {
                        error = $"无法发送控制字符“{name}”";
                        return false;
                    }

                    tokens.Add(CreateSingleCharToken(name[0], mods));
                    continue;
                }

                if (!KeyNames.TryGetNamedKey(name, mods, out var named))
                {
                    error = $"无法识别的按键名“{name}”";
                    return false;
                }

                tokens.Add(named);
                continue;
            }

            // 修饰符后面直接跟完整键名，如 ^F7 / !Down
            if (TryMatchNamedKey(s, i, out string matched, out int length))
            {
                KeyNames.TryGetNamedKey(matched, mods, out var named);
                tokens.Add(named);
                i += length;
                continue;
            }

            char c = s[i];
            i++;

            if (char.IsControl(c))
            {
                error = $"无法发送控制字符 U+{(int)c:X4}";
                return false;
            }

            tokens.Add(CreateSingleCharToken(c, mods));
        }

        if (tokens.Count == 0)
        {
            error = "按键不能为空";
            return false;
        }

        return true;
    }

    private static bool TryMatchNamedKey(string s, int start, out string name, out int length)
    {
        name = string.Empty;
        length = 0;

        int max = Math.Min(KeyNames.MaxNameLength, s.Length - start);
        for (int len = max; len >= 2; len--)
        {
            string candidate = s.Substring(start, len);
            if (!IsNameCandidate(candidate)) continue;
            if (!KeyNames.IsKnownKey(candidate) && !KeyNames.IsMouseName(candidate)) continue;

            name = candidate;
            length = len;
            return true;
        }

        return false;
    }

    private static bool IsNameCandidate(string s)
    {
        foreach (char c in s)
        {
            if (!char.IsLetterOrDigit(c) && c != '_') return false;
        }

        return s.Length > 0;
    }

    /// <summary>Ctrl+A 风格的组合键。返回 false 表示“这不是组合键写法”。</summary>
    private static bool TryParseFriendlyCombo(string s, out List<SendToken>? result, out string error)
    {
        result = null;
        error = string.Empty;

        if (s.IndexOf('+') < 0) return false;

        string[] raw = s.Split('+');
        if (raw.Length < 2) return false;

        uint mods = 0;
        for (int i = 0; i < raw.Length - 1; i++)
        {
            string m = raw[i].Trim().ToLowerInvariant();
            uint bit = m switch
            {
                "ctrl" or "control" or "lctrl" or "rctrl" => ModControl,
                "alt" or "lalt" or "ralt" => ModAlt,
                "shift" or "lshift" or "rshift" => ModShift,
                "win" or "windows" or "lwin" or "rwin" => ModWin,
                _ => 0u,
            };

            if (bit == 0) return false; // 前面不是修饰键，不算组合键写法
            mods |= bit;
        }

        string keyPart = raw[^1].Trim();
        if (keyPart.Length == 0) return false;

        result = new List<SendToken>(1);

        if (keyPart.Length == 1)
        {
            if (char.IsControl(keyPart[0]))
            {
                result = null;
                error = $"无法发送控制字符“{keyPart}”";
                return true;
            }

            result.Add(CreateSingleCharToken(keyPart[0], mods));
            return true;
        }

        if (KeyNames.TryGetNamedKey(keyPart, mods, out var named))
        {
            result.Add(named);
            return true;
        }

        result = null;
        error = $"无法识别的按键名“{keyPart}”";
        return true;
    }

    private static bool TryTypeText(string s, List<SendToken> tokens, out string error)
    {
        error = string.Empty;
        var list = new List<SendToken>(s.Length);

        foreach (char c in s)
        {
            if (c == '\r') continue;

            if (c == '\n')
            {
                list.Add(SendToken.ForKey(0x0D, false));
                continue;
            }

            if (c == '\t')
            {
                list.Add(SendToken.ForKey(0x09, false));
                continue;
            }

            if (char.IsControl(c))
            {
                error = $"无法发送控制字符 U+{(int)c:X4}";
                return false;
            }

            list.Add(SendToken.ForCharacter(c));
        }

        if (list.Count == 0)
        {
            error = "没有可发送的按键";
            return false;
        }

        tokens.AddRange(list);
        return true;
    }

    /// <summary>
    /// 组合键里的单个字符：字母/数字按“物理键”处理（Ctrl+A 不该变成 Ctrl+Shift+A），
    /// 其它符号（如 ^;）才按字符解析。
    /// </summary>
    private static SendToken CreateSingleCharToken(char c, uint mods)
    {
        if (mods != 0 && (char.IsAsciiLetter(c) || char.IsAsciiDigit(c)))
        {
            if (KeyNames.TryGetVirtualKey(c.ToString(), out ushort vk, out bool extended))
            {
                return SendToken.ForKey(vk, extended, mods);
            }
        }

        return SendToken.ForCharacter(c, mods);
    }

    private static bool TryMapModifier(char c, out uint modifier)
    {
        switch (c)
        {
            case '^': modifier = ModControl; return true;
            case '!': modifier = ModAlt; return true;
            case '+': modifier = ModShift; return true;
            case '#': modifier = ModWin; return true;
            case '~': modifier = 0; return true;
            default: modifier = 0; return false;
        }
    }
}

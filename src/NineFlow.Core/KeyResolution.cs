namespace NineFlow.Core;

/// <summary>
/// 把一次按键动作归一到「虚拟键码 + 实际要按下的修饰键」。
/// 发送（<see cref="KeySender"/>）和冲突检查（<see cref="KeyConflict"/>）共用这一段，
/// 免得两边各写一份、慢慢走偏。
/// </summary>
internal static class KeyResolution
{
    /// <summary>
    /// 解析一个键盘动作。字符类按键按当前键盘布局反查虚拟键码，
    /// 反查不到时退回 Unicode 注入（此时 <paramref name="unicode"/> 有值、虚拟键码为 0）。
    /// </summary>
    public static void Resolve(
        SendToken token,
        out ushort virtualKey,
        out bool extended,
        out uint modifiers,
        out char? unicode)
    {
        uint mods = token.Modifiers;
        ushort vk;
        bool ext;
        char? uni = null;

        if (token.IsCharacter)
        {
            short scan = NativeMethods.VkKeyScanEx(token.Character, NativeMethods.GetKeyboardLayout(0));

            if (scan != -1)
            {
                vk = (ushort)(scan & 0xFF);
                byte state = (byte)((scan >> 8) & 0xFF);

                if ((state & 0x01) != 0) mods |= SendSpec.ModShift;
                if ((state & 0x02) != 0) mods |= SendSpec.ModControl;
                if ((state & 0x04) != 0) mods |= SendSpec.ModAlt;
                ext = false;
            }
            else
            {
                vk = 0;
                ext = false;
                uni = token.Character;
            }
        }
        else
        {
            vk = token.VirtualKey;
            ext = token.Extended;
        }

        virtualKey = vk;
        extended = ext;
        modifiers = mods;
        unicode = uni;
    }

    /// <summary>解析成真实按键（有虚拟键码、没有退回 Unicode）；用于比较两个动作是否是同一个键。</summary>
    public static bool TryResolve(SendToken token, out ushort virtualKey, out uint modifiers)
    {
        Resolve(token, out virtualKey, out _, out modifiers, out char? unicode);
        return !unicode.HasValue && virtualKey != 0;
    }
}

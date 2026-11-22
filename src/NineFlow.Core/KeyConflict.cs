namespace NineFlow.Core;

/// <summary>
/// 判断「要发送的按键」是否撞上了九序自己的全局热键。
///
/// 撞车的后果非常隐蔽：<c>RegisterHotKey</c> 是在系统输入流这一层把键截走的，
/// 所以 <c>SendInput</c> 注入的按键同样会被截走（实测确认），目标窗口永远收不到。
/// 具体表现取决于撞的是哪个热键：
/// <list type="bullet">
/// <item>撞上「停止」热键 —— 连点跑到那一行会把自己停掉；</item>
/// <item>撞上「启动」热键 —— 这一行静默失效，看起来像连点没发出去。</item>
/// </list>
/// </summary>
public static class KeyConflict
{
    /// <summary>这组发送动作里，是否含有与热键完全相同的按键（虚拟键码与修饰键都一致）。</summary>
    public static bool Contains(IReadOnlyList<SendToken> tokens, HotkeyDef hotkey)
    {
        foreach (SendToken token in tokens)
        {
            if (Matches(token, hotkey)) return true;
        }

        return false;
    }

    /// <summary>单个动作是否就是这个热键。</summary>
    public static bool Matches(SendToken token, HotkeyDef hotkey)
    {
        if (token.Kind != StrokeKind.Keyboard) return false;
        if (!KeyResolution.TryResolve(token, out ushort virtualKey, out uint modifiers)) return false;

        return virtualKey == hotkey.VirtualKey && modifiers == hotkey.Modifiers;
    }
}

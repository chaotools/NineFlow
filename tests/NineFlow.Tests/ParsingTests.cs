using NineFlow.Core;

namespace NineFlow.Tests;

internal static class KeyNamesTests
{
    public static void Run()
    {
        Program.Check("Down 可识别", KeyNames.TryGetVirtualKey("Down", out ushort down, out bool downExt));
        Program.Equal("Down 虚拟键", (ushort)0x28, down);
        Program.Check("Down 是扩展键", downExt);

        Program.Check("Enter 可识别", KeyNames.TryGetVirtualKey("Enter", out ushort enter, out bool enterExt));
        Program.Equal("Enter 虚拟键", (ushort)0x0D, enter);
        Program.Check("Enter 不是扩展键", !enterExt);

        Program.Check("Return 是 Enter 的别名", KeyNames.TryGetVirtualKey("Return", out ushort ret, out _) && ret == 0x0D);
        Program.Check("Page Up 带空格可识别", KeyNames.TryGetVirtualKey("Page Up", out ushort pageUp, out _) && pageUp == 0x21);
        Program.Check("大小写不敏感", KeyNames.TryGetVirtualKey("dOwN", out ushort mixed, out _) && mixed == 0x28);
        Program.Check("Volume_Up 可识别", KeyNames.TryGetVirtualKey("Volume_Up", out ushort volumeUp, out _) && volumeUp == 0xAF);
        Program.Check("Numpad1 可识别", KeyNames.TryGetVirtualKey("Numpad1", out ushort numpad1, out _) && numpad1 == 0x61);
        Program.Check("F24 可识别", KeyNames.TryGetVirtualKey("F24", out ushort f24, out _) && f24 == 0x87);
        Program.Check("未知键名", !KeyNames.IsKnownKey("Qqq"));
        Program.Check("LButton 是鼠标动作", KeyNames.IsMouseName("LButton"));
        Program.Check("WheelUp 是鼠标动作", KeyNames.IsMouseName("WheelUp"));
        Program.Check("Down 不是鼠标动作", !KeyNames.IsMouseName("Down"));

        Program.Equal("显示名 Down", "Down", KeyNames.GetDisplayName(0x28));
        Program.Equal("显示名 F7", "F7", KeyNames.GetDisplayName(0x76));
        Program.Equal("未知虚拟键显示", "VKFE", KeyNames.GetDisplayName(0xFE));
    }
}

internal static class SendSpecTests
{
    public static void Run()
    {
        // 普通按键名
        Program.Check("Down 解析成功", SendSpec.TryParse("Down", out List<SendToken> down, out string downError), downError);
        Program.Equal("Down 动作数", 1, down.Count);
        Program.Equal("Down 虚拟键", (ushort)0x28, down[0].VirtualKey);
        Program.Check("Down 是扩展键", down[0].Extended);
        Program.Equal("Down 无修饰键", 0u, down[0].Modifiers);

        Program.Check("Enter 解析成功", SendSpec.TryParse("Enter", out List<SendToken> enter, out _));
        Program.Equal("Enter 虚拟键", (ushort)0x0D, enter[0].VirtualKey);
        Program.Check("Enter 不是扩展键", !enter[0].Extended);

        // 单字符
        Program.Check("单字符 a 解析成功", SendSpec.TryParse("a", out List<SendToken> letter, out _));
        Program.Check("a 是字符形式", letter[0].IsCharacter);
        Program.Equal("a 字符值", 'a', letter[0].Character);

        // 组合键后缀写法
        Program.Check("^a 解析成功", SendSpec.TryParse("^a", out List<SendToken> ctrlA, out _));
        Program.Equal("^a 动作数", 1, ctrlA.Count);
        Program.Equal("^a 修饰键", SendSpec.ModControl, ctrlA[0].Modifiers);
        Program.Equal("^a 虚拟键", (ushort)0x41, ctrlA[0].VirtualKey);
        Program.Check("^a 不用字符解析（否则会多出 Shift）", !ctrlA[0].IsCharacter);

        Program.Check("!q 解析成功", SendSpec.TryParse("!q", out List<SendToken> altQ, out _));
        Program.Equal("!q 修饰键", SendSpec.ModAlt, altQ[0].Modifiers);
        Program.Equal("!q 虚拟键", (ushort)0x51, altQ[0].VirtualKey);

        Program.Check("+s 解析成功", SendSpec.TryParse("+s", out List<SendToken> shiftS, out _));
        Program.Equal("+s 修饰键", SendSpec.ModShift, shiftS[0].Modifiers);

        // 常见组合键写法
        Program.Check("Ctrl+A 解析成功", SendSpec.TryParse("Ctrl+A", out List<SendToken> friendly, out _));
        Program.Equal("Ctrl+A 修饰键", SendSpec.ModControl, friendly[0].Modifiers);
        Program.Equal("Ctrl+A 虚拟键", (ushort)0x41, friendly[0].VirtualKey);

        Program.Check("Ctrl+Shift+S 解析成功", SendSpec.TryParse("Ctrl+Shift+S", out List<SendToken> css, out _));
        Program.Equal("Ctrl+Shift+S 修饰键", SendSpec.ModControl | SendSpec.ModShift, css[0].Modifiers);

        Program.Check("Alt+F4 解析成功", SendSpec.TryParse("Alt+F4", out List<SendToken> altF4, out _));
        Program.Equal("Alt+F4 虚拟键", (ushort)0x73, altF4[0].VirtualKey);
        Program.Equal("Alt+F4 修饰键", SendSpec.ModAlt, altF4[0].Modifiers);

        // 连续动作
        Program.Check("{Tab}{Tab} 解析成功", SendSpec.TryParse("{Tab}{Tab}", out List<SendToken> tabs, out _));
        Program.Equal("连续动作数", 2, tabs.Count);

        Program.Check("^+{Tab} 解析成功", SendSpec.TryParse("^+{Tab}", out List<SendToken> ctrlShiftTab, out _));
        Program.Equal("^+{Tab} 修饰键", SendSpec.ModControl | SendSpec.ModShift, ctrlShiftTab[0].Modifiers);

        Program.Check("Down,Enter 解析成功", SendSpec.TryParse("Down,Enter", out List<SendToken> pair, out _));
        Program.Equal("逗号分隔动作数", 2, pair.Count);
        Program.Equal("逗号分隔第 1 个", (ushort)0x28, pair[0].VirtualKey);
        Program.Equal("逗号分隔第 2 个", (ushort)0x0D, pair[1].VirtualKey);

        Program.Check("{Enter} 解析成功", SendSpec.TryParse("{Enter}", out List<SendToken> braced, out _));
        Program.Equal("花括号 Enter 虚拟键", (ushort)0x0D, braced[0].VirtualKey);

        // 连续文字
        Program.Check("abc 解析为 3 个字符", SendSpec.TryParse("abc", out List<SendToken> text, out _));
        Program.Equal("abc 动作数", 3, text.Count);

        // 鼠标
        Program.Check("LButton 解析成功", SendSpec.TryParse("LButton", out List<SendToken> mouse, out _));
        Program.Equal("LButton 类型", StrokeKind.Mouse, mouse[0].Kind);
        Program.Equal("LButton 动作", MouseStroke.Left, mouse[0].Mouse);

        // 非法输入
        Program.Check("空字符串无效", !SendSpec.TryParse("", out _, out _));
        Program.Check("仅空白无效", !SendSpec.TryParse("   ", out _, out _));
        Program.Check("单独修饰符无效", !SendSpec.TryParse("^", out _, out _));
        Program.Check("缺右花括号无效", !SendSpec.TryParse("{Tab", out _, out _));
        Program.Check("空花括号无效", !SendSpec.TryParse("{}", out _, out _));
        Program.Check("未知键名无效", !SendSpec.TryParse("{Qqq}", out _, out _));
        Program.Check("未知组合键无效", !SendSpec.TryParse("Ctrl+Qqq", out _, out _));

        // 描述文本（用于统计跳过了几次）
        Program.Check("Describe 解析 Down", SendSpec.TryParse("Down", out List<SendToken> described, out _));
        Program.Equal("Describe 文本", "Down", SendSpec.Describe(described));
        Program.Check("Describe 解析 Ctrl+A", SendSpec.TryParse("Ctrl+A", out List<SendToken> describedCombo, out _));
        Program.Equal("Describe 组合键文本", "Ctrl+A", SendSpec.Describe(describedCombo));
    }
}

internal static class HotkeyDefTests
{
    public static void Run()
    {
        Program.Check("F7 解析成功", HotkeyDef.TryParse("F7", out HotkeyDef? f7, out string f7Error), f7Error);
        Program.Equal("F7 虚拟键", 0x76u, f7!.VirtualKey);
        Program.Equal("F7 无修饰", 0u, f7.Modifiers);
        Program.Equal("F7 显示文本", "F7", f7.Display);

        Program.Check("^F7 解析成功", HotkeyDef.TryParse("^F7", out HotkeyDef? prefixedF7, out _));
        Program.Equal("^F7 修饰键", 2u, prefixedF7!.Modifiers);
        Program.Equal("^F7 虚拟键", 0x76u, prefixedF7.VirtualKey);
        Program.Equal("^F7 显示文本", "Ctrl+F7", prefixedF7.Display);

        Program.Check("Ctrl+F7 解析成功", HotkeyDef.TryParse("Ctrl+F7", out HotkeyDef? ctrlF7, out _));
        Program.Equal("Ctrl+F7 修饰键", 2u, ctrlF7!.Modifiers);
        Program.Equal("Ctrl+F7 虚拟键", 0x76u, ctrlF7.VirtualKey);

        Program.Check("!q 解析成功", HotkeyDef.TryParse("!q", out HotkeyDef? altQ, out _));
        Program.Equal("!q 修饰键", 1u, altQ!.Modifiers);
        Program.Equal("!q 虚拟键", 0x51u, altQ.VirtualKey);

        Program.Check("Ctrl+Shift+S 解析成功", HotkeyDef.TryParse("Ctrl+Shift+S", out HotkeyDef? css, out _));
        Program.Equal("Ctrl+Shift+S 修饰键", 6u, css!.Modifiers);
        Program.Equal("Ctrl+Shift+S 虚拟键", 0x53u, css.VirtualKey);
        Program.Equal("Ctrl+Shift+S 显示文本", "Ctrl+Shift+S", css.Display);

        Program.Check("F12 解析成功", HotkeyDef.TryParse("F12", out HotkeyDef? f12, out _));
        Program.Equal("F12 虚拟键", 0x7Bu, f12!.VirtualKey);

        Program.Check("空热键无效", !HotkeyDef.TryParse("", out _, out _));
        Program.Check("只有修饰符无效", !HotkeyDef.TryParse("^", out _, out _));
        Program.Check("未知键无效", !HotkeyDef.TryParse("Ctrl+Qqq", out _, out _));
        Program.Check("鼠标键不能做热键", !HotkeyDef.TryParse("LButton", out _, out _));
    }
}

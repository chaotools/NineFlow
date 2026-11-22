using NineFlow.Core;

namespace NineFlow.Tests;

/// <summary>
/// 「要发送的按键」是否撞上九序自己的全局热键。
/// 撞车的后果是静默的：RegisterHotKey 在输入流这一层截键，
/// 连 SendInput 注入的按键也会被截走（已实测验证），目标窗口永远收不到。
/// </summary>
internal static class KeyConflictTests
{
    public static void Run()
    {
        // 完全一致才算冲突
        Program.Check("F7 与 F7 热键冲突", Conflicts("F7", "F7"));
        Program.Check("F13 与 F13 热键冲突", Conflicts("F13", "F13"));

        // 修饰键必须完全一致
        Program.Check("^F7 与 F7 热键不冲突", !Conflicts("^F7", "F7"));
        Program.Check("F7 与 Ctrl+F7 热键不冲突", !Conflicts("F7", "Ctrl+F7"));
        Program.Check("^F7 与 Ctrl+F7 热键冲突", Conflicts("^F7", "Ctrl+F7"));
        Program.Check("Down 与 Ctrl+Down 热键不冲突", !Conflicts("Down", "Ctrl+Down"));
        Program.Check("^Down 与 Ctrl+Down 热键冲突", Conflicts("^Down", "Ctrl+Down"));
        Program.Check("!Enter 与 Alt+Enter 热键冲突", Conflicts("!Enter", "Alt+Enter"));

        // 字符要按当前键盘布局反查虚拟键码后再比较
        Program.Check("^a 与 Ctrl+A 热键冲突", Conflicts("^a", "Ctrl+A"));
        Program.Check("a 与 Ctrl+A 热键不冲突", !Conflicts("a", "Ctrl+A"));
        Program.Check("A 与 Ctrl+A 热键不冲突（字符自带 Shift）", !Conflicts("A", "Ctrl+A"));
        Program.Check("^A 与 Ctrl+A 热键冲突（大小写不影响物理键）", Conflicts("^A", "Ctrl+A"));
        Program.Check("^+a 与 Ctrl+Shift+A 热键冲突", Conflicts("^+a", "Ctrl+Shift+A"));

        // 一行里有多个动作时，任意一个撞上都要算
        Program.Check("多动作里含热键", Conflicts("a,F7", "F7"));
        Program.Check("多动作里不含热键", !Conflicts("a,Enter", "F7"));

        // 鼠标动作不参与热键匹配
        Program.Check("鼠标动作不与热键冲突", !Conflicts("LButton", "F7"));

        // 默认配置必须安全：Down / Enter，不会撞上默认热键 F7 / F8
        Program.Check("默认按键不撞启动热键", !Conflicts("Down", "F7") && !Conflicts("Enter", "F7"));
        Program.Check("默认按键不撞停止热键", !Conflicts("Down", "F8") && !Conflicts("Enter", "F8"));
    }

    private static bool Conflicts(string keyText, string hotkeyText)
    {
        if (!SendSpec.TryParse(keyText, out List<SendToken> tokens, out string keyError))
        {
            throw new InvalidOperationException($"测试用例本身有问题：按键“{keyText}”无法解析（{keyError}）");
        }

        if (!HotkeyDef.TryParse(hotkeyText, out HotkeyDef? hotkey, out string hotkeyError))
        {
            throw new InvalidOperationException($"测试用例本身有问题：热键“{hotkeyText}”无法解析（{hotkeyError}）");
        }

        return KeyConflict.Contains(tokens, hotkey!);
    }
}

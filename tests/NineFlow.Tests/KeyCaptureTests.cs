using NineFlow.Core;

namespace NineFlow.Tests;

/// <summary>
/// 按键捕获的翻译逻辑。这里只测纯函数 TryFormat；
/// 钩子本身是 P/Invoke，靠真实键盘操作验证（见 IntegrationTests 的说明）。
/// </summary>
internal static class KeyCaptureTests
{
    public static void Run()
    {
        // 普通按键名
        Program.Check("捕获 Down", KeyCapture.TryFormat(0x28, true, 0, out string down, out string downError), downError);
        Program.Equal("Down 写法", "Down", down);
        Program.Check(
            "Down 能解析回同一个键",
            SendSpec.TryParse(down, out List<SendToken> downTokens, out _) && downTokens[0].VirtualKey == 0x28);

        Program.Check("捕获 Enter", KeyCapture.TryFormat(0x0D, false, 0, out string enter, out _));
        Program.Equal("Enter 写法", "Enter", enter);

        // 小键盘回车和主键盘回车共用 0x0D，要靠扩展位区分
        Program.Check("捕获小键盘回车", KeyCapture.TryFormat(0x0D, true, 0, out string numpadEnter, out _));
        Program.Equal("小键盘回车写法", "NumpadEnter", numpadEnter);
        Program.Check(
            "小键盘回车是扩展键",
            SendSpec.TryParse(numpadEnter, out List<SendToken> numpadTokens, out _) && numpadTokens[0].Extended);

        // 字母识别的是物理按键，统一输出大写键名（R / V），不按字符大小写处理
        Program.Check("捕获 R 键", KeyCapture.TryFormat(0x52, false, 0, out string plainR, out _));
        Program.Equal("R 键写法", "R", plainR);
        Program.Check(
            "R 解析为物理键且不带 Shift",
            SendSpec.TryParse(plainR, out List<SendToken> rTokens, out _)
            && rTokens[0].VirtualKey == 0x52
            && rTokens[0].Modifiers == 0);

        // 手写的小写字母仍按字符发送，保留手动填值的灵活性
        Program.Check("手写 a 解析为字符", SendSpec.TryParse("a", out List<SendToken> aTokens, out _));
        Program.Check("a 是字符而不是大写键", aTokens[0].IsCharacter);

        Program.Check("捕获 Shift+R", KeyCapture.TryFormat(0x52, false, SendSpec.ModShift, out string shiftR, out _));
        Program.Equal("Shift+R 写法", "+R", shiftR);

        // 组合键：统一用 ^ ! + # 前缀写法，比 Ctrl+Shift+S 更适合窄输入框
        Program.Check("捕获 Ctrl+A", KeyCapture.TryFormat(0x41, false, SendSpec.ModControl, out string ctrlA, out _));
        Program.Equal("Ctrl+A 写法", "^A", ctrlA);
        Program.Check(
            "^A 解析回 A 且只带 Ctrl",
            SendSpec.TryParse(ctrlA, out List<SendToken> ctrlATokens, out _)
            && ctrlATokens[0].VirtualKey == 0x41
            && ctrlATokens[0].Modifiers == SendSpec.ModControl);

        Program.Check(
            "捕获 Ctrl+Shift+S",
            KeyCapture.TryFormat(0x53, false, SendSpec.ModControl | SendSpec.ModShift, out string css, out _));
        Program.Equal("Ctrl+Shift+S 写法", "^+S", css);

        Program.Check("捕获 Alt+F4", KeyCapture.TryFormat(0x73, false, SendSpec.ModAlt, out string altF4, out _));
        Program.Equal("Alt+F4 写法", "!F4", altF4);
        Program.Check(
            "!F4 解析回 F4 且带 Alt",
            SendSpec.TryParse(altF4, out List<SendToken> f4Tokens, out _)
            && f4Tokens[0].VirtualKey == 0x73
            && f4Tokens[0].Modifiers == SendSpec.ModAlt);

        Program.Check("捕获 Win+Space", KeyCapture.TryFormat(0x20, false, SendSpec.ModWin, out string winSpace, out _));
        Program.Equal("Win+Space 写法", "#Space", winSpace);

        Program.Check("捕获 Shift+Up", KeyCapture.TryFormat(0x26, true, SendSpec.ModShift, out string shiftUp, out _));
        Program.Equal("Shift+Up 写法", "+Up", shiftUp);

        // 数字
        Program.Check("捕获 5", KeyCapture.TryFormat(0x35, false, 0, out string five, out _));
        Program.Equal("5 写法", "5", five);
        Program.Check("捕获 Shift+5", KeyCapture.TryFormat(0x35, false, SendSpec.ModShift, out string shiftFive, out _));
        Program.Equal("Shift+5 写法", "+5", shiftFive);
        Program.Check(
            "Shift+5 解析回 5 且带 Shift",
            SendSpec.TryParse(shiftFive, out List<SendToken> fiveTokens, out _)
            && fiveTokens[0].VirtualKey == 0x35
            && fiveTokens[0].Modifiers == SendSpec.ModShift);

        // 空格 / Esc 必须走按键名：写成字符会被 Trim 掉或被当成控制字符拒绝
        Program.Check("捕获空格", KeyCapture.TryFormat(0x20, false, 0, out string space, out _));
        Program.Equal("空格写法", "Space", space);
        Program.Check("捕获 Esc", KeyCapture.TryFormat(0x1B, false, 0, out string esc, out _));
        Program.Equal("Esc 写法", "Esc", esc);

        // 标点走字符形式；具体字符取决于当前键盘布局，所以只校验"能解析回去"
        Program.Check("捕获 OEM 分号键", KeyCapture.TryFormat(0xBA, false, 0, out string semicolon, out string semicolonError), semicolonError);
        Program.Check(
            "分号结果能被解析",
            SendSpec.TryParse(semicolon, out _, out string semicolonParseError),
            semicolon + " / " + semicolonParseError);

        Program.Check("捕获 OEM 加号键", KeyCapture.TryFormat(0xBB, false, 0, out string plusKey, out string plusKeyError), plusKeyError);
        Program.Check(
            "加号键结果能被解析",
            SendSpec.TryParse(plusKey, out _, out string plusParseError),
            plusKey + " / " + plusParseError);

        Program.Check(
            "捕获 Ctrl+OEM 加号键",
            KeyCapture.TryFormat(0xBB, false, SendSpec.ModControl, out string ctrlPlus, out string ctrlPlusError),
            ctrlPlusError);
        Program.Check(
            "Ctrl+加号键结果能被解析",
            SendSpec.TryParse(ctrlPlus, out _, out string ctrlPlusParseError),
            ctrlPlus + " / " + ctrlPlusParseError);

        // 认不出来的键要明确失败，而不是产出解析不了的文本
        bool unknownCaptured = KeyCapture.TryFormat(0xFE, false, 0, out string unknownText, out string unknownError);
        Program.Check("未知虚拟键捕获失败", !unknownCaptured, unknownText);
        Program.Check("未知键的提示可读", unknownError.Contains("无法识别"), unknownError);
    }
}

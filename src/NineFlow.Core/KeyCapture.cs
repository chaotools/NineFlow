using System.Runtime.InteropServices;
using System.Text;

namespace NineFlow.Core;

/// <summary>
/// 按键捕获：装一个全局低级键盘钩子，把用户按下的键翻译成九序的按键写法。
///
/// 为什么用钩子而不是读输入框的 KeyDown：九序自己把 F7/F8 注册成了全局热键，
/// 输入框收不到；方向键、Tab 也会被文本框当成光标移动吃掉。
/// 低级钩子比热键更早拿到按键，而且可以把这次按键整个吞掉，
/// 否则「按 Alt+F4 想录下来」会变成「把程序关掉」。
///
/// 安全边界：捕获期间所有键盘输入都会被吞掉，所以必须有无条件兜底——
/// 按 Esc 取消、超时自动取消、<see cref="Stop"/> 一定卸钩子；
/// 钩子是本进程内的，进程退出系统会自动回收。
///
/// 注入的按键（SendInput / 屏幕键盘 / 宏键盘）同样会被接受：连点开始前一定会先
/// <see cref="Stop"/>，所以不存在"把自己发的键录进来"的情况。
/// </summary>
public sealed class KeyCapture : IDisposable
{
    /// <summary>默认捕获超时。超时后自动取消，避免键盘一直被吞。</summary>
    public const int DefaultTimeoutMs = 8000;

    private const ushort VkEscape = 0x1B;

    private readonly object _gate = new();
    private readonly int _timeoutMs;

    /// <summary>必须用字段引用住钩子委托，否则会被 GC 回收，回调就跳到野地址上。</summary>
    private NativeMethods.LowLevelKeyboardProc? _proc;

    private IntPtr _hook = IntPtr.Zero;
    private Timer? _timer;
    private uint _modifiers;

    public KeyCapture(int timeoutMs = DefaultTimeoutMs) => _timeoutMs = Math.Max(0, timeoutMs);

    /// <summary>捕获成功，参数是可以直接填进按键输入框的文本（如 <c>^+s</c>、<c>Down</c>）。</summary>
    public event Action<string>? Captured;

    /// <summary>取消。reason 为 null 表示用户按了 Esc，否则是可以直接显示的原因。</summary>
    public event Action<string?>? Cancelled;

    public bool IsCapturing => _hook != IntPtr.Zero;

    /// <summary>开始捕获。返回 false 表示钩子装不上（被安全软件拦等），调用方应提示手动填写。</summary>
    public bool Start()
    {
        lock (_gate)
        {
            if (_hook != IntPtr.Zero) return true;

            _modifiers = 0;
            _proc = HookCallback;
            _hook = NativeMethods.SetWindowsHookEx(
                NativeMethods.WH_KEYBOARD_LL, _proc, NativeMethods.GetModuleHandle(null), 0);

            if (_hook == IntPtr.Zero)
            {
                _proc = null;
                return false;
            }

            if (_timeoutMs > 0)
            {
                _timer = new Timer(_ => Finish(null, UiText.T("capture.timeout")), null, _timeoutMs, Timeout.Infinite);
            }

            return true;
        }
    }

    /// <summary>卸掉钩子，不触发任何事件（切换界面状态、开始连点前调用）。</summary>
    public void Stop()
    {
        lock (_gate) ReleaseLocked();
    }

    public void Dispose()
    {
        Stop();
        _proc = null;
    }

    /// <summary>
    /// 把一次按键翻译成九序的写法。纯函数，便于单元测试。
    /// 组合键用 <c>^ ! + #</c> 前缀写法；字母统一输出按键名（R / V），不按字符大小写处理。
    /// </summary>
    public static bool TryFormat(ushort virtualKey, bool extended, uint modifiers, out string text, out string error)
    {
        text = string.Empty;
        error = string.Empty;

        // 字母识别的是物理按键，统一写成大写键名（R、V），不是按字符大小写；
        // 数字仍是字符本身，Ctrl+R / Ctrl+Shift+R 这类组合照旧用 ^ / ^+ 前缀。
        if (IsAsciiLetterOrDigit(virtualKey))
        {
            char key = (char)virtualKey;
            text = Prefix(modifiers)
                + (char.IsAsciiDigit(key) ? key.ToString() : char.ToUpperInvariant(key).ToString());
            return true;
        }

        // 标点等可打印键走字符形式，SendSpec 发送时会按当前键盘布局解析
        char printable = PrintableCharacter(virtualKey);
        if (printable != '\0')
        {
            text = Prefix(modifiers) + WrapIfModifierLike(printable);
            return true;
        }

        if (!KeyNames.TryGetDisplayName(virtualKey, out string name))
        {
            error = $"这个按键（VK{virtualKey:X2}）暂时无法识别，请手动填写";
            return false;
        }

        // 小键盘回车和主键盘回车共用虚拟键码 0x0D，靠扩展位区分
        if (virtualKey == 0x0D && extended) name = "NumpadEnter";

        text = Prefix(modifiers) + name;
        return true;
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        // 回调跑在系统的输入链路上，任何异常都不能往外抛
        try
        {
            if (nCode >= 0 && _hook != IntPtr.Zero)
            {
                HandleKey(wParam, lParam);

                // 捕获期间吞掉所有键盘输入，避免 Alt+F4 / Win / F7 漏给系统和其它程序
                return (IntPtr)1;
            }
        }
        catch
        {
            // 忽略：继续往下传，绝不打断输入链路
        }

        return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private void HandleKey(IntPtr wParam, IntPtr lParam)
    {
        int message = wParam.ToInt32();
        bool down = message is NativeMethods.WM_KEYDOWN or NativeMethods.WM_SYSKEYDOWN;
        bool up = message is NativeMethods.WM_KEYUP or NativeMethods.WM_SYSKEYUP;
        if (!down && !up) return;

        var info = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
        ushort vk = (ushort)info.vkCode;

        uint bit = ModifierBit(vk);
        if (bit != 0)
        {
            if (down) _modifiers |= bit;
            else _modifiers &= ~bit;
            return;
        }

        if (!down) return;

        if (vk == VkEscape)
        {
            Finish(null, null);
            return;
        }

        bool extended = (info.flags & NativeMethods.LLKHF_EXTENDED) != 0;
        if (TryFormat(vk, extended, _modifiers, out string text, out string error)) Finish(text, null);
        else Finish(null, error);
    }

    /// <summary>结束本轮捕获并派发事件；同一轮只会派发一次。</summary>
    private void Finish(string? captured, string? reason)
    {
        lock (_gate)
        {
            if (_hook == IntPtr.Zero) return; // 已经被 Stop 或前一个按键结束了
            ReleaseLocked();
        }

        if (captured != null) Captured?.Invoke(captured);
        else Cancelled?.Invoke(reason);
    }

    private void ReleaseLocked()
    {
        _timer?.Dispose();
        _timer = null;

        if (_hook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }

        _modifiers = 0;
    }

    private static uint ModifierBit(ushort vk) => vk switch
    {
        0x10 or 0xA0 or 0xA1 => SendSpec.ModShift,   // Shift / LShift / RShift
        0x11 or 0xA2 or 0xA3 => SendSpec.ModControl, // Ctrl / LCtrl / RCtrl
        0x12 or 0xA4 or 0xA5 => SendSpec.ModAlt,     // Alt / LAlt / RAlt
        0x5B or 0x5C => SendSpec.ModWin,             // LWin / RWin
        _ => 0u,
    };

    private static bool IsAsciiLetterOrDigit(ushort vk) =>
        (vk >= 0x41 && vk <= 0x5A) || (vk >= 0x30 && vk <= 0x39);

    /// <summary>取该键在当前键盘布局下对应的可见字符；控制键和功能键返回 '\0'。</summary>
    private static char PrintableCharacter(ushort vk)
    {
        char c = (char)(NativeMethods.MapVirtualKey(vk, NativeMethods.MAPVK_VK_TO_CHAR) & 0xFFFF);

        // 空格及以下（Tab / Enter / Esc / Backspace）交给按键名处理，
        // 否则 "^ " 这种带尾空格的结果会被 TryParse 的 Trim 吃掉
        return c > ' ' ? c : '\0';
    }

    private static string Prefix(uint modifiers)
    {
        var builder = new StringBuilder(4);
        if ((modifiers & SendSpec.ModControl) != 0) builder.Append('^');
        if ((modifiers & SendSpec.ModAlt) != 0) builder.Append('!');
        if ((modifiers & SendSpec.ModShift) != 0) builder.Append('+');
        if ((modifiers & SendSpec.ModWin) != 0) builder.Append('#');
        return builder.ToString();
    }

    /// <summary>+ ^ ! # 本身就是修饰符前缀，单独作按键时必须写成 {+} 这种形式。</summary>
    private static string WrapIfModifierLike(char c) =>
        c is '+' or '^' or '!' or '#' ? "{" + c + "}" : c.ToString();
}

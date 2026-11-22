using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using NineFlow.Core;

namespace NineFlow.Tests;

/// <summary>
/// 真实环境集成测试：注册全局热键、通过 SendInput 真正把按键发给自己的测试窗口。
/// 所有窗口都放在屏幕外，不会打扰用户桌面。
/// </summary>
internal static class IntegrationTests
{
    private const int WM_HOTKEY = 0x0312;

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    public static void Run()
    {
        RunHotkeyTests();
        RunTimingTest();
        RunRealInputTests();
        RunKeyCaptureTest();
    }

    /// <summary>
    /// 真实按键捕获：装低级键盘钩子后注入 Ctrl+A，应当录成 ^a。
    /// 钩子回调排队在安装钩子的线程上，必须 Application.DoEvents 才会被调用。
    /// 先把一个屏幕外的透明窗口切到前台，万一钩子没生效，注入的键也落不到用户的程序里。
    /// </summary>
    private static void RunKeyCaptureTest()
    {
        using var form = new Form
        {
            ShowInTaskbar = false,
            FormBorderStyle = FormBorderStyle.None,
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-4000, -4000),
            Size = new Size(200, 100),
            Opacity = 0,
        };

        form.Show();
        Application.DoEvents();

        for (int attempt = 0; attempt < 5 && GetForegroundWindow() != form.Handle; attempt++)
        {
            keybd_event(0x12, 0, 0, UIntPtr.Zero);
            keybd_event(0x12, 0, 2, UIntPtr.Zero);
            SetForegroundWindow(form.Handle);
            Thread.Sleep(50);
        }

        using var capture = new KeyCapture(timeoutMs: 4000);
        string? captured = null;
        string? cancelled = null;
        capture.Captured += text => captured = text;
        capture.Cancelled += reason => cancelled = reason;

        Program.Check("按键捕获可以装上钩子", capture.Start());

        // Ctrl 按下 → A 按下 → 抬起。捕获期间这些都会被钩子吞掉。
        keybd_event(0x11, 0, 0, UIntPtr.Zero);
        keybd_event(0x41, 0, 0, UIntPtr.Zero);
        keybd_event(0x41, 0, 2, UIntPtr.Zero);
        keybd_event(0x11, 0, 2, UIntPtr.Zero);

        var clock = Stopwatch.StartNew();
        while (captured == null && cancelled == null && clock.ElapsedMilliseconds < 3000) Application.DoEvents();

        Program.Equal("注入 Ctrl+A 被录成 ^a", "^a", captured ?? (cancelled ?? "(没有事件)"));
        Program.Check("捕获成功后自动卸钩子", !capture.IsCapturing);

        // 取消路径：Stop 之后必须立刻不再处于捕获状态，否则键盘会一直被吞
        using var cancelledCapture = new KeyCapture(timeoutMs: 4000);
        Program.Check("第二次可以装上钩子", cancelledCapture.Start());
        cancelledCapture.Stop();
        Program.Check("Stop 之后不再处于捕获状态", !cancelledCapture.IsCapturing);

        // 兜底超时：没人按键时必须自动卸钩子，否则键盘会被一直吞掉
        using var timeoutCapture = new KeyCapture(timeoutMs: 300);
        using var timeoutSignal = new ManualResetEventSlim();
        string? timeoutReason = null;
        timeoutCapture.Cancelled += reason =>
        {
            timeoutReason = reason;
            timeoutSignal.Set();
        };

        Program.Check("超时测试可以装上钩子", timeoutCapture.Start());
        Program.Check("超时后派发取消事件", timeoutSignal.Wait(4000), "4 秒内没有取消");
        Program.Check("超时后已卸掉钩子", !timeoutCapture.IsCapturing);
        Program.Check("超时取消带原因", !string.IsNullOrEmpty(timeoutReason), timeoutReason ?? "(null)");

        form.Close();
    }

    private sealed class HotkeyTestForm : Form
    {
        public HotkeyRegistry? Registry;

        protected override void WndProc(ref Message m)
        {
            if (Registry != null && Registry.TryHandleMessage(m.Msg, m.WParam)) return;
            base.WndProc(ref m);
        }
    }

    private static void RunHotkeyTests()
    {
        using var form = new HotkeyTestForm
        {
            ShowInTaskbar = false,
            FormBorderStyle = FormBorderStyle.None,
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-4000, -4000),
            Size = new Size(200, 100),
            Opacity = 0,
        };

        form.Show();
        Application.DoEvents();

        using var registry = new HotkeyRegistry(form.Handle);
        form.Registry = registry;

        int fired = 0;

        Program.Check("测试热键 F13 解析", HotkeyDef.TryParse("F13", out HotkeyDef? definition, out string parseError), parseError);
        if (definition == null) return;

        bool registered = registry.TryRegister(9001, definition, () => fired++, out HotkeyRegistrationError registerError, out int registerWin32);
        Program.Check("注册全局热键 F13 成功", registered, registerError.ToString() + "/" + registerWin32);

        if (registered)
        {
            PostMessage(form.Handle, WM_HOTKEY, 9001, IntPtr.Zero);
            Application.DoEvents();
            Program.Equal("收到 WM_HOTKEY 后触发回调", 1, fired);

            // 同一个键重复注册应当失败，并明确是“已被占用”
            bool duplicate = registry.TryRegister(9002, definition, () => { }, out HotkeyRegistrationError duplicateError, out _);
            Program.Check("重复注册同一热键被拒绝", !duplicate);
            Program.Equal("重复注册返回“已被占用”", HotkeyRegistrationError.AlreadyInUse, duplicateError);

            registry.Unregister(9001);
            PostMessage(form.Handle, WM_HOTKEY, 9001, IntPtr.Zero);
            Application.DoEvents();
            Program.Equal("注销后不再触发回调", 1, fired);

            // 注销后可以重新注册
            bool again = registry.TryRegister(9001, definition, () => fired++, out HotkeyRegistrationError againError, out _);
            Program.Check("注销后可以重新注册", again, againError.ToString());

            // 回归：用同一个 id 改注册一个已被占用的热键，失败后原来那个热键必须还在，
            // 否则改错一次快捷键就会让连点器失去“停止”手段。
            Program.Check("准备：解析 F14", HotkeyDef.TryParse("F14", out HotkeyDef? other, out string otherError), otherError);
            bool occupied = registry.TryRegister(9003, other!, () => { }, out HotkeyRegistrationError occupiedError, out _);
            Program.Check("先占用 F14", occupied, occupiedError.ToString());

            bool replaced = registry.TryRegister(9001, other!, () => fired++, out HotkeyRegistrationError replaceError, out _);
            Program.Check("换注册为已被占用的热键被拒绝", !replaced);
            Program.Equal("换注册失败原因是已被占用", HotkeyRegistrationError.AlreadyInUse, replaceError);

            PostMessage(form.Handle, WM_HOTKEY, 9001, IntPtr.Zero);
            Application.DoEvents();
            Program.Equal("换注册失败后旧热键仍然可用", 2, fired);

            registry.Unregister(9001);
            registry.Unregister(9003);
        }

        form.Close();
    }

    private static void RunTimingTest()
    {
        var sender = new RecordingSender();
        using var engine = new ClickEngine(sender, warmupMs: 0, skipDelayMs: 0);

        var slots = new List<SlotState>
        {
            new() { Index = 1, Enabled = true, Key = "Down", Interval = 0.02, MaxCount = 0 },
            new() { Index = 2, Enabled = true, Key = "Enter", Interval = 0.02, MaxCount = 0 },
        };

        Program.Check("计时间隔测试启动", engine.Start(slots, "", 0, out string startError), startError);
        Thread.Sleep(500);

        EngineStopResult? result = engine.Stop();
        int total = sender.CallCount;
        int perSlot = result?.Slots.Sum(slot => slot.RunCount) ?? 0;

        // 两个槽位每 20ms 各发一次 → 理论上 0.5 秒约 50 次；放宽到 30~70 以容忍机器负载
        Program.Check(
            "0.02 秒间隔的发送频率合理",
            total >= 30 && total <= 70,
            "实际发送 " + total + " 次");
        Program.Equal("发送计数与槽位计数一致", total, perSlot);
    }

    private sealed class InputTestForm : Form
    {
        public int DownCount;
        public int EnterCount;
        public int CharCount;
        public bool CtrlACombo;

        public InputTestForm()
        {
            KeyPreview = true;

            KeyDown += (_, e) =>
            {
                switch (e.KeyCode)
                {
                    case Keys.Down:
                        DownCount++;
                        break;
                    case Keys.Enter:
                        EnterCount++;
                        break;
                    case Keys.A when e.Control:
                        CtrlACombo = true;
                        break;
                }
            };

            KeyPress += (_, e) =>
            {
                if (e.KeyChar == 'a') CharCount++;
            };
        }
    }

    private static void RunRealInputTests()
    {
        var form = new InputTestForm
        {
            ShowInTaskbar = false,
            FormBorderStyle = FormBorderStyle.None,
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-4000, -4000),
            Size = new Size(320, 120),
        };

        form.Show();
        Application.DoEvents();

        IntPtr previousForeground = GetForegroundWindow();
        bool focused = false;

        for (int attempt = 0; attempt < 8 && !focused; attempt++)
        {
            form.Activate();
            ShowWindow(form.Handle, 9);

            // 轻按 ALT，让系统允许本进程切换前台窗口
            keybd_event(0x12, 0, 0, UIntPtr.Zero);
            keybd_event(0x12, 0, 2, UIntPtr.Zero);
            SetForegroundWindow(form.Handle);
            Thread.Sleep(80);

            if (GetForegroundWindow() != form.Handle)
            {
                uint foregroundThread = GetWindowThreadProcessId(GetForegroundWindow(), out _);
                uint currentThread = GetCurrentThreadId();
                bool attached = AttachThreadInput(currentThread, foregroundThread, true);
                try
                {
                    BringWindowToTop(form.Handle);
                    SetForegroundWindow(form.Handle);
                    Thread.Sleep(80);
                }
                finally
                {
                    if (attached) AttachThreadInput(currentThread, foregroundThread, false);
                }
            }

            Application.DoEvents();
            focused = GetForegroundWindow() == form.Handle;
        }

        if (!focused)
        {
            Program.Check("真实按键投递测试（跳过：测试窗口未取得前台焦点，避免误发到桌面）", true);
            form.Close();
            return;
        }

        try
        {
            var sender = new KeySender();

            Program.Check("真实发送 Down", sender.Send(Parse("Down"), out string downError), downError);
            Program.Check("真实发送 Enter", sender.Send(Parse("Enter"), out string enterError), enterError);
            Program.Check("真实发送字符 a", sender.Send(Parse("a"), out string charError), charError);
            Program.Check("真实发送 Ctrl+A", sender.Send(Parse("^a"), out string comboError), comboError);

            Thread.Sleep(150);
            Application.DoEvents();

            Program.Check("测试窗口收到 Down", form.DownCount >= 1, "收到 " + form.DownCount + " 次");
            Program.Check("测试窗口收到 Enter", form.EnterCount >= 1, "收到 " + form.EnterCount + " 次");
            Program.Check("测试窗口收到字符 a", form.CharCount >= 1, "收到 " + form.CharCount + " 次");
            Program.Check("测试窗口收到 Ctrl+A 组合键", form.CtrlACombo);
        }
        finally
        {
            if (previousForeground != IntPtr.Zero) SetForegroundWindow(previousForeground);
            form.Close();
        }
    }

    private static List<SendToken> Parse(string text)
    {
        if (!SendSpec.TryParse(text, out List<SendToken> tokens, out string error))
        {
            throw new InvalidOperationException(error);
        }

        return tokens;
    }
}

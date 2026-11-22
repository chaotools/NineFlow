using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace KeyCatcher;

/// <summary>
/// 测试辅助工具：装一个全局低级键盘钩子，把收到的按键虚拟码按顺序写进日志文件。
/// 不依赖窗口焦点，所以能可靠验证「九序到底发了哪些键」。
/// 用法：KeyCatcher.exe "C:\path\log.txt"
/// 日志格式：READY，然后每个按键追加 "vk=74;"
/// </summary>
internal static class Program
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;

    private static string _logPath = string.Empty;
    private static LowLevelKeyboardProc? _proc;
    private static IntPtr _hook = IntPtr.Zero;

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length < 1)
        {
            MessageBox.Show("用法: KeyCatcher.exe <日志文件路径>", "KeyCatcher");
            return;
        }

        _logPath = args[0];
        ApplicationConfiguration.Initialize();

        var form = new Form
        {
            Text = "NineFlow KeyCatcher",
            ClientSize = new Size(360, 120),
            StartPosition = FormStartPosition.Manual,
            Location = new Point(80, 80),
            TopMost = true,
            BackColor = Color.White,
        };

        form.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Microsoft YaHei UI", 11f, FontStyle.Bold),
            ForeColor = Color.FromArgb(0, 122, 255),
            Text = "按键捕获中…",
        });

        form.Shown += (_, _) =>
        {
            _proc = HookCallback;
            _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);

            File.WriteAllText(_logPath, _hook != IntPtr.Zero ? "READY" : "HOOK_FAILED");

            form.Activate();
            SetForegroundWindow(form.Handle);
        };

        form.FormClosed += (_, _) =>
        {
            if (_hook != IntPtr.Zero) UnhookWindowsHookEx(_hook);
        };

        Application.Run(form);
    }

    private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
        {
            var info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            try
            {
                File.AppendAllText(_logPath, "vk=" + info.vkCode + ";");
            }
            catch
            {
                // 忽略写入失败
            }
        }

        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }
}

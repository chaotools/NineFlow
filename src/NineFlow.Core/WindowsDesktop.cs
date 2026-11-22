using System.Text;

namespace NineFlow.Core;

/// <summary>
/// 目标窗口相关的小工具：记录前台窗口、取窗口标题、把焦点切回目标窗口。
/// 九序的按键是发给“当前前台窗口”的，所以点“开始连点”时要把焦点还给用户真正在用的窗口。
/// </summary>
public static class WindowsDesktop
{
    public static IntPtr GetForegroundWindow() => NativeMethods.GetForegroundWindow();

    public static bool IsWindow(IntPtr window) => window != IntPtr.Zero && NativeMethods.IsWindow(window);

    public static bool IsMinimized(IntPtr window) => NativeMethods.IsIconic(window);

    /// <summary>
    /// 把窗口切到前台。先直接切；被系统前台锁拦下时，挂到当前前台线程再强切。
    /// </summary>
    public static bool ActivateWindow(IntPtr window)
    {
        if (!IsWindow(window)) return false;

        if (IsMinimized(window)) NativeMethods.ShowWindow(window, 9); // SW_RESTORE
        NativeMethods.BringWindowToTop(window);

        if (NativeMethods.SetForegroundWindow(window))
        {
            Thread.Sleep(60);
            if (GetForegroundWindow() == window) return true;
        }

        uint foregroundThread = NativeMethods.GetWindowThreadProcessId(GetForegroundWindow(), out _);
        uint currentThread = NativeMethods.GetCurrentThreadId();
        bool attached = foregroundThread != currentThread && NativeMethods.AttachThreadInput(currentThread, foregroundThread, true);

        try
        {
            NativeMethods.BringWindowToTop(window);
            NativeMethods.SetForegroundWindow(window);
            Thread.Sleep(80);
        }
        finally
        {
            if (attached) NativeMethods.AttachThreadInput(currentThread, foregroundThread, false);
        }

        return GetForegroundWindow() == window;
    }

    public static int GetWindowProcessId(IntPtr window)
    {
        NativeMethods.GetWindowThreadProcessId(window, out uint processId);
        return (int)processId;
    }

    public static string GetWindowTitle(IntPtr window)
    {
        if (!IsWindow(window)) return string.Empty;

        int length = NativeMethods.GetWindowTextLength(window);
        if (length <= 0) return string.Empty;

        var buffer = new StringBuilder(length + 2);
        NativeMethods.GetWindowText(window, buffer, buffer.Capacity);
        return buffer.ToString().Trim();
    }

    /// <summary>
    /// 当前前台窗口是不是属于本进程（九序自己的窗口、提示框、ToolTip 都算）。
    /// </summary>
    public static bool IsOwnProcessWindow(IntPtr window, int ownProcessId)
    {
        if (!IsWindow(window)) return false;
        return GetWindowProcessId(window) == ownProcessId;
    }

    /// <summary>取一个可用的目标窗口：优先当前前台窗口（不能是本进程的），其次上次记录的目标窗口。</summary>
    public static IntPtr ResolveSendTarget(int ownProcessId, IntPtr ownHandle, IntPtr remembered)
    {
        IntPtr foreground = GetForegroundWindow();
        if (IsWindow(foreground) && foreground != ownHandle && !IsOwnProcessWindow(foreground, ownProcessId))
        {
            return foreground;
        }

        if (IsWindow(remembered) && !IsMinimized(remembered)) return remembered;
        return IntPtr.Zero;
    }
}

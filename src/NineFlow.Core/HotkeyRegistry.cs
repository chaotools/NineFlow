using System.Runtime.InteropServices;

namespace NineFlow.Core;

public enum HotkeyRegistrationError
{
    None,

    /// <summary>该快捷键已被其它程序占用。</summary>
    AlreadyInUse,

    /// <summary>其它注册失败原因（见 win32Error）。</summary>
    Failed,
}

/// <summary>注册 / 注销全局热键，并在窗口消息里分发 WM_HOTKEY。</summary>
public sealed class HotkeyRegistry : IDisposable
{
    private sealed class Entry
    {
        public required HotkeyDef Def { get; init; }
        public required Action Handler { get; init; }
    }

    private readonly IntPtr _hwnd;
    private readonly Dictionary<int, Entry> _entries = new();
    private bool _disposed;

    public HotkeyRegistry(IntPtr hwnd) => _hwnd = hwnd;

    public bool IsRegistered(int id) => _entries.ContainsKey(id);

    /// <summary>
    /// 注册（会先注销同 id 的旧热键）。
    /// 新热键注册失败时会把旧热键装回去——否则改错一个快捷键，会把原本能用的那个一起丢掉，
    /// 对连点器来说等于运行中失去“停止”手段。
    /// </summary>
    public bool TryRegister(
        int id,
        HotkeyDef def,
        Action handler,
        out HotkeyRegistrationError error,
        out int win32Error)
    {
        error = HotkeyRegistrationError.None;
        win32Error = 0;

        _entries.TryGetValue(id, out Entry? previous);
        Unregister(id);

        if (!NativeMethods.RegisterHotKey(
                _hwnd,
                id,
                def.Modifiers | NativeMethods.MOD_NOREPEAT,
                def.VirtualKey))
        {
            win32Error = Marshal.GetLastWin32Error();
            error = win32Error == NativeMethods.ERROR_HOTKEY_ALREADY_REGISTERED
                ? HotkeyRegistrationError.AlreadyInUse
                : HotkeyRegistrationError.Failed;

            Restore(id, previous, ref error);
            return false;
        }

        _entries[id] = new Entry { Def = def, Handler = handler };
        return true;
    }

    /// <summary>把被替换掉的旧热键装回去；装不回来说明调用方现在什么热键都没有，要单独提示。</summary>
    private void Restore(int id, Entry? previous, ref HotkeyRegistrationError error)
    {
        if (previous == null) return;

        if (NativeMethods.RegisterHotKey(
                _hwnd,
                id,
                previous.Def.Modifiers | NativeMethods.MOD_NOREPEAT,
                previous.Def.VirtualKey))
        {
            _entries[id] = previous;
            return;
        }

        // 连旧的都装不回去了：状态比“注册失败”更糟，交给调用方去报告
        error = HotkeyRegistrationError.Failed;
    }

    public void Unregister(int id)
    {
        if (_entries.Remove(id))
        {
            NativeMethods.UnregisterHotKey(_hwnd, id);
        }
    }

    public void UnregisterAll()
    {
        foreach (int id in _entries.Keys.ToList()) Unregister(id);
    }

    /// <summary>在窗口过程里调用；返回 true 表示消息已被处理。</summary>
    public bool TryHandleMessage(int message, IntPtr wParam)
    {
        if (message != NativeMethods.WM_HOTKEY) return false;

        int id = wParam.ToInt32();
        if (!_entries.TryGetValue(id, out var entry)) return false;

        entry.Handler();
        return true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        UnregisterAll();
    }
}

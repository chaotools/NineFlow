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

    /// <summary>注册（会先注销同 id 的旧热键）。失败时通过 error / win32Error 说明原因。</summary>
    public bool TryRegister(
        int id,
        HotkeyDef def,
        Action handler,
        out HotkeyRegistrationError error,
        out int win32Error)
    {
        error = HotkeyRegistrationError.None;
        win32Error = 0;

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
            return false;
        }

        _entries[id] = new Entry { Def = def, Handler = handler };
        return true;
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

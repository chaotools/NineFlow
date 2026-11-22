using System.Runtime.InteropServices;

namespace NineFlow.Core;

/// <summary>
/// 基于 SendInput 的按键发送器。
/// 每个动作：按下修饰键 → 按键按下/释放 → 释放修饰键。
/// </summary>
public sealed class KeySender : IKeySender
{
    private const ushort VkShift = 0xA0;
    private const ushort VkControl = 0xA2;
    private const ushort VkAlt = 0xA4;
    private const ushort VkLWin = 0x5B;

    public bool Send(IReadOnlyList<SendToken> tokens, out string error)
    {
        error = string.Empty;

        if (tokens.Count == 0)
        {
            error = "没有可发送的按键";
            return false;
        }

        var inputs = new List<NativeMethods.INPUT>(tokens.Count * 4);
        foreach (var token in tokens) BuildToken(token, inputs);

        if (inputs.Count == 0)
        {
            error = "没有可发送的按键";
            return false;
        }

        uint sent = NativeMethods.SendInput(
            (uint)inputs.Count,
            inputs.ToArray(),
            Marshal.SizeOf<NativeMethods.INPUT>());

        if (sent == inputs.Count) return true;

        int lastError = Marshal.GetLastWin32Error();
        error = lastError switch
        {
            5 => "按键发送被系统拦截：目标窗口可能以管理员身份运行，请用相同的权限运行九序。",
            0 => $"按键发送不完整（{sent}/{inputs.Count}），可能被安全软件拦截。",
            _ => $"按键发送失败（Windows 错误 {lastError}）。",
        };
        return false;
    }

    private static void BuildToken(SendToken token, List<NativeMethods.INPUT> inputs)
    {
        if (token.Kind == StrokeKind.Mouse)
        {
            // 鼠标动作同样要带上修饰键，否则 ^LButton（Ctrl+单击）里的 Ctrl 会被静默丢掉
            PressModifiers(inputs, token.Modifiers);
            AddMouse(token, inputs);
            ReleaseModifiers(inputs, token.Modifiers);
            return;
        }

        AddKeyboard(token, inputs);
    }

    private static void AddKeyboard(SendToken token, List<NativeMethods.INPUT> inputs)
    {
        KeyResolution.Resolve(token, out ushort vk, out bool extended, out uint mods, out char? unicode);

        PressModifiers(inputs, mods);

        if (unicode.HasValue)
        {
            AddUnicode(inputs, unicode.Value);
        }
        else
        {
            AddVirtualKey(inputs, vk, extended, keyUp: false);
            AddVirtualKey(inputs, vk, extended, keyUp: true);
        }

        ReleaseModifiers(inputs, mods);
    }

    private static void PressModifiers(List<NativeMethods.INPUT> inputs, uint mods)
    {
        if ((mods & SendSpec.ModControl) != 0) AddVirtualKey(inputs, VkControl, false, false);
        if ((mods & SendSpec.ModAlt) != 0) AddVirtualKey(inputs, VkAlt, false, false);
        if ((mods & SendSpec.ModShift) != 0) AddVirtualKey(inputs, VkShift, false, false);
        if ((mods & SendSpec.ModWin) != 0) AddVirtualKey(inputs, VkLWin, true, false);
    }

    private static void ReleaseModifiers(List<NativeMethods.INPUT> inputs, uint mods)
    {
        if ((mods & SendSpec.ModWin) != 0) AddVirtualKey(inputs, VkLWin, true, true);
        if ((mods & SendSpec.ModShift) != 0) AddVirtualKey(inputs, VkShift, false, true);
        if ((mods & SendSpec.ModAlt) != 0) AddVirtualKey(inputs, VkAlt, false, true);
        if ((mods & SendSpec.ModControl) != 0) AddVirtualKey(inputs, VkControl, false, true);
    }

    private static void AddVirtualKey(List<NativeMethods.INPUT> inputs, ushort vk, bool extended, bool keyUp)
    {
        ushort scan = (ushort)NativeMethods.MapVirtualKey(vk, NativeMethods.MAPVK_VK_TO_VSC);

        uint flags = 0;
        if (keyUp) flags |= NativeMethods.KEYEVENTF_KEYUP;
        if (extended) flags |= NativeMethods.KEYEVENTF_EXTENDEDKEY;

        inputs.Add(new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_KEYBOARD,
            U = new NativeMethods.InputUnion
            {
                ki = new NativeMethods.KEYBDINPUT
                {
                    wVk = vk,
                    wScan = scan,
                    dwFlags = flags,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero,
                },
            },
        });
    }

    private static void AddUnicode(List<NativeMethods.INPUT> inputs, char c)
    {
        inputs.Add(new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_KEYBOARD,
            U = new NativeMethods.InputUnion
            {
                ki = new NativeMethods.KEYBDINPUT
                {
                    wVk = 0,
                    wScan = c,
                    dwFlags = NativeMethods.KEYEVENTF_UNICODE,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero,
                },
            },
        });

        inputs.Add(new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_KEYBOARD,
            U = new NativeMethods.InputUnion
            {
                ki = new NativeMethods.KEYBDINPUT
                {
                    wVk = 0,
                    wScan = c,
                    dwFlags = NativeMethods.KEYEVENTF_UNICODE | NativeMethods.KEYEVENTF_KEYUP,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero,
                },
            },
        });
    }

    private static void AddMouse(SendToken token, List<NativeMethods.INPUT> inputs)
    {
        switch (token.Mouse)
        {
            case MouseStroke.Left:
                AddMouseButton(inputs, NativeMethods.MOUSEEVENTF_LEFTDOWN, NativeMethods.MOUSEEVENTF_LEFTUP, 0);
                break;
            case MouseStroke.Right:
                AddMouseButton(inputs, NativeMethods.MOUSEEVENTF_RIGHTDOWN, NativeMethods.MOUSEEVENTF_RIGHTUP, 0);
                break;
            case MouseStroke.Middle:
                AddMouseButton(inputs, NativeMethods.MOUSEEVENTF_MIDDLEDOWN, NativeMethods.MOUSEEVENTF_MIDDLEUP, 0);
                break;
            case MouseStroke.X1:
                AddMouseButton(inputs, NativeMethods.MOUSEEVENTF_XDOWN, NativeMethods.MOUSEEVENTF_XUP, NativeMethods.XBUTTON1);
                break;
            case MouseStroke.X2:
                AddMouseButton(inputs, NativeMethods.MOUSEEVENTF_XDOWN, NativeMethods.MOUSEEVENTF_XUP, NativeMethods.XBUTTON2);
                break;
            case MouseStroke.WheelUp:
                AddWheel(inputs, 120, NativeMethods.MOUSEEVENTF_WHEEL);
                break;
            case MouseStroke.WheelDown:
                AddWheel(inputs, unchecked((uint)(-120)), NativeMethods.MOUSEEVENTF_WHEEL);
                break;
            case MouseStroke.WheelLeft:
                AddWheel(inputs, unchecked((uint)(-120)), NativeMethods.MOUSEEVENTF_HWHEEL);
                break;
            case MouseStroke.WheelRight:
                AddWheel(inputs, 120, NativeMethods.MOUSEEVENTF_HWHEEL);
                break;
        }
    }

    private static void AddMouseButton(List<NativeMethods.INPUT> inputs, uint downFlag, uint upFlag, int buttonData)
    {
        inputs.Add(new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_MOUSE,
            U = new NativeMethods.InputUnion
            {
                mi = new NativeMethods.MOUSEINPUT
                {
                    dx = 0,
                    dy = 0,
                    mouseData = (uint)buttonData,
                    dwFlags = downFlag,
                },
            },
        });

        inputs.Add(new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_MOUSE,
            U = new NativeMethods.InputUnion
            {
                mi = new NativeMethods.MOUSEINPUT
                {
                    dx = 0,
                    dy = 0,
                    mouseData = (uint)buttonData,
                    dwFlags = upFlag,
                },
            },
        });
    }

    private static void AddWheel(List<NativeMethods.INPUT> inputs, uint delta, uint flag)
    {
        inputs.Add(new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_MOUSE,
            U = new NativeMethods.InputUnion
            {
                mi = new NativeMethods.MOUSEINPUT
                {
                    mouseData = delta,
                    dwFlags = flag,
                },
            },
        });
    }
}

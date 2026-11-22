namespace NineFlow.Core;

/// <summary>一次按键发送动作：可带修饰键，按键或鼠标。</summary>
public enum StrokeKind
{
    Keyboard,
    Mouse,
}

public enum MouseStroke
{
    Left,
    Right,
    Middle,
    X1,
    X2,
    WheelUp,
    WheelDown,
    WheelLeft,
    WheelRight,
}

/// <summary>
/// 解析后的单个发送动作。字符类按键在发送时按当前键盘布局解析成虚拟键码，
/// 无法映射时退回 Unicode 注入。
/// </summary>
public sealed class SendToken
{
    public StrokeKind Kind { get; private init; }
    public uint Modifiers { get; private init; }

    /// <summary>虚拟键码（Kind == Keyboard 且不是字符时有效）。</summary>
    public ushort VirtualKey { get; private init; }

    public bool Extended { get; private init; }

    /// <summary>字符（Kind == Keyboard 且以字符形式给出时有效，'\0' 表示未使用）。</summary>
    public char Character { get; private init; }

    public bool IsCharacter => Kind == StrokeKind.Keyboard && Character != '\0';

    public MouseStroke Mouse { get; private init; }

    public static SendToken ForKey(ushort vk, bool extended, uint modifiers = 0) => new()
    {
        Kind = StrokeKind.Keyboard,
        VirtualKey = vk,
        Extended = extended,
        Modifiers = modifiers,
    };

    public static SendToken ForCharacter(char c, uint modifiers = 0) => new()
    {
        Kind = StrokeKind.Keyboard,
        Character = c,
        Modifiers = modifiers,
    };

    public static SendToken ForMouse(MouseStroke stroke, uint modifiers = 0) => new()
    {
        Kind = StrokeKind.Mouse,
        Mouse = stroke,
        Modifiers = modifiers,
    };

    public override string ToString()
    {
        if (Kind == StrokeKind.Mouse) return "Mouse:" + Mouse;
        if (IsCharacter) return "Char:" + Character;
        return "Key:" + KeyNames.GetDisplayName(VirtualKey);
    }
}

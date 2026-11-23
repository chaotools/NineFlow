namespace NineFlow.Core;

/// <summary>出厂默认值。</summary>
public static class AppDefaults
{
    /// <summary>至少保留两行（默认 Down / Enter 交替）。</summary>
    public const int MinSlots = 2;

    /// <summary>界面允许添加的最大行数，避免无限扩张。</summary>
    public const int MaxSlots = 20;

    public const string HotkeyOn = "F7";
    public const string HotkeyOff = "F8";
    public const string HotkeyReset = "F9";
    public const string SkipKey = "Down";

    /// <summary>
    /// 预置配置：以 0.02 秒间隔交替发送 Down 和 Enter，每轮各 9 次。
    /// 其余行留空禁用；行数按需，最少 <see cref="MinSlots"/> 行。
    /// </summary>
    public static List<SlotState> CreateSlots(int count = MinSlots)
    {
        int effective = Math.Max(MinSlots, count);
        var list = new List<SlotState>(effective);

        void Add(bool enabled, string key, double interval, int maxCount)
        {
            list.Add(new SlotState
            {
                Index = list.Count + 1,
                Enabled = enabled,
                Key = key,
                Interval = interval,
                MaxCount = maxCount,
            });
        }

        Add(true, "Down", 0.02, 9);
        Add(true, "Enter", 0.02, 9);
        while (list.Count < effective) Add(false, string.Empty, 1.5, 0);

        return list;
    }

    /// <summary>创建一行空白行，供界面「添加一行」使用。</summary>
    public static SlotState CreateBlankSlot(int index) => new()
    {
        Index = index,
        Enabled = false,
        Key = string.Empty,
        Interval = 1.5,
        MaxCount = 0,
    };
}

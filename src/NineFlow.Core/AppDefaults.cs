namespace NineFlow.Core;

/// <summary>出厂默认值。</summary>
public static class AppDefaults
{
    public const int SlotCount = 10;

    public const string HotkeyOn = "F7";
    public const string HotkeyOff = "F8";
    public const string SkipKey = "Down";

    /// <summary>
    /// 预置配置：以 0.02 秒间隔交替发送 Down 和 Enter，每轮各 9 次。
    /// </summary>
    public static List<SlotState> CreateSlots()
    {
        var list = new List<SlotState>(SlotCount);

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
        Add(false, "2", 1.5, 0);
        Add(false, "3", 1.5, 0);
        Add(false, "4", 1.5, 0);
        Add(false, "5", 1.5, 0);
        while (list.Count < SlotCount) Add(false, string.Empty, 1.5, 0);

        return list;
    }
}

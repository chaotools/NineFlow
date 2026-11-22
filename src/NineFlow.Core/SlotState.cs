namespace NineFlow.Core;

/// <summary>
/// 一个按键槽位（界面上的一行）：开关、按键、间隔、次数，以及本轮 / 累计进度。
/// </summary>
public sealed class SlotState
{
    /// <summary>最小定时间隔（毫秒）。</summary>
    public const int MinPeriodMs = 10;

    /// <summary>1 基行号。</summary>
    public int Index { get; init; }

    /// <summary>是否勾选启用。</summary>
    public bool Enabled { get; set; }

    /// <summary>要发送的按键（如 Down / Enter / ^a / !q）。</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>间隔秒数，最小 0.01。</summary>
    public double Interval { get; set; } = 1.5;

    /// <summary>每轮次数，0 = 无限连发。</summary>
    public int MaxCount { get; set; }

    /// <summary>本轮已发送次数。</summary>
    public int RunCount { get; set; }

    /// <summary>累计已发送次数（跨轮累加，用于续点）。</summary>
    public int Done { get; set; }

    /// <summary>该行是否会参与连发。</summary>
    public bool IsActive => Enabled && !string.IsNullOrWhiteSpace(Key);

    /// <summary>本轮是否已发满。</summary>
    public bool IsSaturated => MaxCount > 0 && RunCount >= MaxCount;

    /// <summary>换算成定时器周期（毫秒）。</summary>
    public int EffectivePeriodMs =>
        Math.Max(MinPeriodMs, (int)Math.Round(ClampInterval(Interval) * 1000d, MidpointRounding.AwayFromZero));

    public SlotState Clone() => new()
    {
        Index = Index,
        Enabled = Enabled,
        Key = Key,
        Interval = Interval,
        MaxCount = MaxCount,
        RunCount = RunCount,
        Done = Done,
    };

    /// <summary>间隔下限 0.01 秒。</summary>
    public static double ClampInterval(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) return 1.5;
        return Math.Max(0.01, value);
    }

    public static int ClampCount(int value) => Math.Max(0, value);
}

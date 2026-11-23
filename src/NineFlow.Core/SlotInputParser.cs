using System.Globalization;

namespace NineFlow.Core;

/// <summary>界面一行的原始文本（尚未解析）。</summary>
public sealed record SlotInput(
    int Index,
    bool Enabled,
    string? Key,
    string? IntervalText,
    string? CountText,
    int Done = 0,
    int RunCount = 0);

/// <summary>
/// 把界面文本解析成 <see cref="SlotState"/>：空值 / 非法值回落到安全默认值，
/// 需要时同时产出一条可渲染的问题（间隔为空、次数不是整数…）。
///
/// 解析规则集中在 Core，界面只负责把控件值搬进 <see cref="SlotInput"/>，
/// 于是「空间隔回落到 1.5 秒」这类规则可以被单测，而不是只能靠点界面验证。
/// </summary>
public static class SlotInputParser
{
    /// <summary>间隔为空时的回退值（秒）。</summary>
    public const double FallbackInterval = 1.5;

    /// <summary>解析一行；<paramref name="validate"/> 为 true 时对启用行产出问题。</summary>
    public static SlotState Parse(SlotInput input, bool validate, ICollection<UiProblem>? problems = null)
    {
        ArgumentNullException.ThrowIfNull(input);

        var slot = new SlotState
        {
            Index = input.Index,
            Enabled = input.Enabled,
            Key = (input.Key ?? string.Empty).Trim(),
            Done = input.Done,
            RunCount = input.RunCount,
        };

        string intervalText = (input.IntervalText ?? string.Empty).Trim();
        if (intervalText.Length == 0)
        {
            slot.Interval = FallbackInterval;
            if (validate && slot.Enabled) problems?.Add(UiProblem.Error("row.intervalEmpty", input.Index));
        }
        else if (TryParseNumber(intervalText, out double interval))
        {
            slot.Interval = SlotState.ClampInterval(interval);
        }
        else
        {
            slot.Interval = FallbackInterval;
            if (validate && slot.Enabled) problems?.Add(UiProblem.Error("row.intervalInvalid", input.Index, intervalText));
        }

        string countText = (input.CountText ?? string.Empty).Trim();
        if (countText.Length == 0)
        {
            slot.MaxCount = 0;
        }
        else if (int.TryParse(countText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int maxCount))
        {
            slot.MaxCount = SlotState.ClampCount(maxCount);
        }
        else
        {
            slot.MaxCount = 0;
            if (validate && slot.Enabled) problems?.Add(UiProblem.Error("row.countInvalid", input.Index, countText));
        }

        return slot;
    }

    /// <summary>批量解析，返回的槽位与输入同序。</summary>
    public static List<SlotState> ParseAll(
        IEnumerable<SlotInput> inputs,
        bool validate,
        ICollection<UiProblem>? problems = null)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        var slots = new List<SlotState>();
        foreach (SlotInput input in inputs) slots.Add(Parse(input, validate, problems));
        return slots;
    }

    /// <summary>与配置读取一致的数字解析：先不变区域，再当前区域。</summary>
    public static bool TryParseNumber(string text, out double value)
    {
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) return true;
        return double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
    }
}

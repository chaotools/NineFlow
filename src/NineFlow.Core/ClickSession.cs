namespace NineFlow.Core;

/// <summary>
/// 连点调度的纯逻辑部分：不含线程与真实时钟，便于单元测试。
/// 由外部循环调用 开始 → 反复(取到期槽位 → 发送 → 记账) → 判断是否全部完成。
/// </summary>
public sealed class ClickSession
{
    private readonly object _gate = new();
    private readonly List<SlotState> _slots;
    private readonly int[] _periods;
    private readonly bool[] _active;
    private readonly long[] _nextDue;

    public ClickSession(IEnumerable<SlotState> slots)
    {
        _slots = slots.Select(s => s.Clone()).ToList();
        foreach (SlotState slot in _slots) slot.RunCount = 0;

        _periods = new int[_slots.Count];
        _active = new bool[_slots.Count];
        _nextDue = new long[_slots.Count];

        for (int i = 0; i < _slots.Count; i++)
        {
            _periods[i] = _slots[i].EffectivePeriodMs;
            _active[i] = _slots[i].IsActive;
        }
    }

    public IReadOnlyList<SlotState> Slots => _slots;

    public int ActiveCount
    {
        get
        {
            lock (_gate) return _active.Count(flag => flag);
        }
    }

    /// <summary>开始计时：第一次发送发生在“现在 + 一个间隔”之后。</summary>
    public void Begin(long nowMilliseconds)
    {
        lock (_gate)
        {
            for (int i = 0; i < _slots.Count; i++)
            {
                if (_active[i]) _nextDue[i] = nowMilliseconds + _periods[i];
            }
        }
    }

    /// <summary>取出此刻到期的槽位下标（按行号顺序），并推进它们的到期时间。</summary>
    public List<int> CollectDue(long nowMilliseconds)
    {
        var due = new List<int>(4);

        lock (_gate)
        {
            for (int i = 0; i < _slots.Count; i++)
            {
                if (!_active[i]) continue;
                if (_nextDue[i] > nowMilliseconds) continue;

                due.Add(i);
                _nextDue[i] += _periods[i];

                // 落后太多时不要补发，直接以“现在”为基准重排，避免爆发式连发
                if (_nextDue[i] <= nowMilliseconds) _nextDue[i] = nowMilliseconds + _periods[i];
            }
        }

        return due;
    }

    /// <summary>距离下一个到期还有多少毫秒；没有活动槽位时返回 long.MaxValue。</summary>
    public long MillisecondsUntilNextDue(long nowMilliseconds)
    {
        lock (_gate)
        {
            long best = long.MaxValue;
            for (int i = 0; i < _slots.Count; i++)
            {
                if (!_active[i]) continue;
                long delta = _nextDue[i] - nowMilliseconds;
                if (delta < best) best = delta;
            }

            return best == long.MaxValue ? long.MaxValue : Math.Max(0, best);
        }
    }

    /// <summary>记录一次成功发送。</summary>
    public void RecordSent(int index)
    {
        lock (_gate)
        {
            SlotState slot = _slots[index];
            slot.RunCount++;
            slot.Done++;
            if (slot.IsSaturated) _active[index] = false;
        }
    }

    /// <summary>是否所有槽位都已完成（无限次数的槽位永远不会完成）。</summary>
    public bool AllRunsFinished
    {
        get
        {
            lock (_gate) return !_active.Any(flag => flag);
        }
    }

    /// <summary>取一份线程安全的快照。</summary>
    public IReadOnlyList<SlotState> Snapshot()
    {
        lock (_gate) return _slots.Select(s => s.Clone()).ToList();
    }

    /// <summary>累计次数的最大值（自动跳过数据此计算）。</summary>
    public static int MaxDone(IEnumerable<SlotState> slots)
    {
        int max = 0;
        foreach (SlotState slot in slots)
        {
            if (slot.Done > max) max = slot.Done;
        }

        return max;
    }

    /// <summary>
    /// 自动续点要跳过的项数：只统计真正会执行的行（勾选且有按键）。
    /// 已停用的行带着历史进度一起参与取最大值，会把跳过数撑大，从而多跳过还没处理的项目。
    /// </summary>
    public static int AutoSkipCount(IEnumerable<SlotState> slots) => MaxDone(slots.Where(slot => slot.IsActive));

    /// <summary>按「组合轮次」统计进度，而不是把每一行的按键次数相加。</summary>
    public readonly record struct ComboCounts(int Run, int Max, bool Infinite, int Done);

    /// <summary>
    /// 一组 = 所有启用行各发一次。比如 Down ×9 + Enter ×9 是 9 组、18 次按键。
    /// 同一轮内取活动行的最大值：行数不同时，以发得最多的那一行作为组合轮次。
    /// </summary>
    public static ComboCounts CountCombos(IEnumerable<SlotState> activeSlots)
    {
        var slots = activeSlots.Where(slot => slot.IsActive).ToList();
        if (slots.Count == 0) return new ComboCounts(0, 0, false, 0);

        return new ComboCounts(
            Run: slots.Max(slot => slot.RunCount),
            Max: slots.Max(slot => slot.MaxCount > 0 ? slot.MaxCount : 0),
            Infinite: slots.Any(slot => slot.MaxCount == 0),
            Done: slots.Max(slot => slot.Done));
    }
}

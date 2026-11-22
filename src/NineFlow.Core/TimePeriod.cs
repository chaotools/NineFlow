namespace NineFlow.Core;

/// <summary>
/// 提升系统定时器精度（timeBeginPeriod(1)），让 0.02 秒这种小间隔也能稳定。
/// 支持嵌套调用。
/// </summary>
public static class TimePeriod
{
    private static readonly object Gate = new();
    private static int _count;

    public static void Begin(uint milliseconds = 1)
    {
        lock (Gate)
        {
            if (_count++ == 0)
            {
                try { NativeMethods.TimeBeginPeriod(milliseconds); } catch { /* 忽略：非致命 */ }
            }
        }
    }

    public static void End(uint milliseconds = 1)
    {
        lock (Gate)
        {
            if (_count == 0) return;
            if (--_count == 0)
            {
                try { NativeMethods.TimeEndPeriod(milliseconds); } catch { /* 忽略：非致命 */ }
            }
        }
    }
}

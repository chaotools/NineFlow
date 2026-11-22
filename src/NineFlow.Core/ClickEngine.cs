using System.Diagnostics;

namespace NineFlow.Core;

public enum StopReason
{
    /// <summary>用户停止（按钮 / 快捷键 / 关闭程序）。</summary>
    User,

    /// <summary>所有启用行都发满了本轮次数。</summary>
    Completed,

    /// <summary>发送失败等错误。</summary>
    Error,
}

public sealed record EngineStopResult(StopReason Reason, string? Message, IReadOnlyList<SlotState> Slots);

/// <summary>
/// 连点引擎：后台线程按每个槽位各自的间隔发送按键。
/// 按每个槽位各自的间隔发送按键。
/// </summary>
public sealed class ClickEngine : IDisposable
{
    private sealed class KeySendException : Exception
    {
        public KeySendException(string message) : base(message)
        {
        }
    }

    private readonly IKeySender _sender;
    private readonly int _warmupMs;
    private readonly int _skipDelayMs;
    private readonly object _gate = new();

    private ClickSession? _session;
    private List<SendToken>[]? _tokens;
    private List<SendToken>? _skipTokens;
    private int _skipCount;
    private Thread? _thread;
    private volatile bool _running;
    private volatile bool _stopRequested;

    public ClickEngine(IKeySender sender, int warmupMs = 80, int skipDelayMs = 8)
    {
        _sender = sender ?? throw new ArgumentNullException(nameof(sender));
        _warmupMs = Math.Max(0, warmupMs);
        _skipDelayMs = Math.Max(0, skipDelayMs);
    }

    /// <summary>每次成功发送（含跳过）后触发，可能来自后台线程。</summary>
    public event Action? ProgressChanged;

    /// <summary>非用户主动停止时触发（本轮完成 / 出错），可能来自后台线程。</summary>
    public event Action<EngineStopResult>? Stopped;

    public bool IsRunning => _running;

    /// <summary>
    /// 在真正开始发送按键之前调用（在引擎线程里）。用于把前台焦点切回目标窗口，
    /// 因为点“开始连点”会让九序自己变成前台窗口。
    /// </summary>
    public Action? PrepareSendTarget { get; set; }

    /// <summary>切完焦点后给系统留出的稳定时间（毫秒）。</summary>
    public int PrepareSettleMs { get; set; } = 140;

    /// <summary>开始一轮连发。</summary>
    public bool Start(IEnumerable<SlotState> slots, string skipKey, int skipCount, out string error)
    {
        error = string.Empty;

        lock (_gate)
        {
            if (_running)
            {
                error = "连点已经在运行。";
                return false;
            }

            List<SlotState> active = slots.Where(s => s.IsActive).Select(s => s.Clone()).ToList();
            if (active.Count == 0)
            {
                error = "没有启用任何按键：请至少勾选一行并填写组合按键。";
                return false;
            }

            var tokenCache = new List<SendToken>[active.Count];
            for (int i = 0; i < active.Count; i++)
            {
                if (!SendSpec.TryParse(active[i].Key, out List<SendToken> parsed, out string keyError))
                {
                    error = $"第 {active[i].Index} 行按键“{active[i].Key}”无效：{keyError}";
                    return false;
                }

                tokenCache[i] = parsed;
            }

            List<SendToken>? skipTokens = null;
            int effectiveSkip = Math.Max(0, skipCount);
            if (!string.IsNullOrWhiteSpace(skipKey) && effectiveSkip > 0)
            {
                if (!SendSpec.TryParse(skipKey, out List<SendToken> parsedSkip, out string skipError))
                {
                    error = $"跳过键“{skipKey}”无效：{skipError}";
                    return false;
                }

                skipTokens = parsedSkip;
            }

            _session = new ClickSession(active);
            _tokens = tokenCache;
            _skipTokens = skipTokens;
            _skipCount = effectiveSkip;
            _stopRequested = false;
            _running = true;

            var thread = new Thread(RunLoop)
            {
                IsBackground = true,
                Name = "NineFlow.ClickEngine",
                Priority = ThreadPriority.AboveNormal,
            };
            _thread = thread;
            thread.Start();
            return true;
        }
    }

    /// <summary>请求停止并等待后台线程退出（最多 3 秒）。返回最终快照。</summary>
    public EngineStopResult? Stop()
    {
        Thread? thread = _thread;
        if (thread == null) return null;

        _stopRequested = true;

        bool joined;
        try
        {
            joined = thread.Join(3000);
        }
        catch
        {
            joined = false;
        }

        _running = false;
        if (!joined) return null;

        return _session == null ? null : new EngineStopResult(StopReason.User, null, _session.Snapshot());
    }

    /// <summary>当前（或上一轮）槽位快照。</summary>
    public IReadOnlyList<SlotState> Snapshot() => _session?.Snapshot() ?? Array.Empty<SlotState>();

    public void Dispose()
    {
        Stop();
        _thread = null;
    }

    private void RunLoop()
    {
        ClickSession session = _session!;
        List<SendToken>[] tokens = _tokens!;

        try
        {
            TimePeriod.Begin(1);
            try
            {
                // 给触发用的热键一点时间松开修饰键，避免污染发送内容
                if (_warmupMs > 0) Thread.Sleep(_warmupMs);
                if (_stopRequested)
                {
                    Finish(null);
                    return;
                }

                // 发送前先把焦点交给目标窗口
                if (PrepareSendTarget != null)
                {
                    try
                    {
                        PrepareSendTarget();
                    }
                    catch
                    {
                        // 切焦点失败不阻塞连点
                    }

                    if (PrepareSettleMs > 0) Thread.Sleep(PrepareSettleMs);
                    if (_stopRequested)
                    {
                        Finish(null);
                        return;
                    }
                }

                // 续点：先把光标移过已处理的项目
                if (_skipTokens != null && _skipCount > 0)
                {
                    for (int i = 0; i < _skipCount; i++)
                    {
                        if (_stopRequested)
                        {
                            Finish(null);
                            return;
                        }

                        if (!_sender.Send(_skipTokens, out string skipError)) throw new KeySendException(skipError);
                        ProgressChanged?.Invoke();
                        if (_skipDelayMs > 0) Thread.Sleep(_skipDelayMs);
                    }

                    if (_stopRequested)
                    {
                        Finish(null);
                        return;
                    }
                }

                var clock = Stopwatch.StartNew();
                session.Begin(0);

                while (!_stopRequested)
                {
                    long now = clock.ElapsedMilliseconds;
                    List<int> due = session.CollectDue(now);

                    if (due.Count == 0)
                    {
                        long wait = session.MillisecondsUntilNextDue(now);
                        if (wait > 2) Thread.Sleep((int)Math.Min(wait - 1, 15));
                        else Thread.SpinWait(150);
                        continue;
                    }

                    foreach (int index in due)
                    {
                        if (_stopRequested) break;

                        if (!_sender.Send(tokens[index], out string sendError)) throw new KeySendException(sendError);

                        session.RecordSent(index);
                        ProgressChanged?.Invoke();
                    }

                    if (session.AllRunsFinished)
                    {
                        Finish(new EngineStopResult(StopReason.Completed, null, session.Snapshot()));
                        return;
                    }
                }

                Finish(null);
            }
            finally
            {
                TimePeriod.End(1);
            }
        }
        catch (KeySendException ex)
        {
            Finish(new EngineStopResult(StopReason.Error, ex.Message, session.Snapshot()));
        }
        catch (Exception ex)
        {
            Finish(new EngineStopResult(StopReason.Error, "连点异常：" + ex.Message, session.Snapshot()));
        }
    }

    private void Finish(EngineStopResult? result)
    {
        _running = false;

        if (result == null) return;

        try
        {
            Stopped?.Invoke(result);
        }
        catch
        {
            // 事件订阅者异常不应让线程崩溃
        }
    }
}

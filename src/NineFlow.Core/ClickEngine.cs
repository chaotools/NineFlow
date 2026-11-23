using System.Diagnostics;
using System.Globalization;

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

/// <param name="RunId">产生该结果的那一轮编号；界面用它去重，避免同一轮被处理两次。</param>
public sealed record EngineStopResult(StopReason Reason, string? Message, IReadOnlyList<SlotState> Slots, int RunId = 0);

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
    private IReadOnlyList<SendToken>[]? _tokens;
    private List<SendToken>? _skipTokens;
    private int _skipCount;
    private Thread? _thread;
    private volatile bool _running;
    private volatile bool _stopRequested;

    /// <summary>本轮自然结束时留下的结果（完成 / 出错）；用户主动停止不会写这里。</summary>
    private EngineStopResult? _terminalResult;

    /// <summary>轮次编号，每 Start 一次 +1。</summary>
    private int _runId;

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

    /// <summary>开始一轮连发。计划里的按键已经解析过，引擎不再二次解析。</summary>
    public bool Start(StartPlan plan, out string error)
    {
        ArgumentNullException.ThrowIfNull(plan);
        error = string.Empty;

        lock (_gate)
        {
            if (_running)
            {
                error = "连点已经在运行。";
                return false;
            }

            if (plan.ActiveCount == 0)
            {
                error = UiText.T("status.noActive");
                return false;
            }

            _session = new ClickSession(plan.Slots);
            _tokens = plan.Tokens;
            _skipTokens = plan.SkipTokens?.ToList();
            _skipCount = plan.SkipCount;
            _stopRequested = false;
            _terminalResult = null;
            int runId = ++_runId;
            _running = true;

            var thread = new Thread(() => RunLoop(runId))
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

    /// <summary>
    /// 便捷重载：先走 <see cref="StartPlanner"/> 把原始输入校验成计划，再启动。
    /// 界面走 <see cref="Start(StartPlan, out string)"/>，这个重载主要服务测试与脚本。
    /// </summary>
    public bool Start(IEnumerable<SlotState> slots, string skipKey, int skipCount, out string error)
    {
        ArgumentNullException.ThrowIfNull(slots);

        StartPlan? plan = StartPlanner.TryCreate(
            new StartRequest(
                slots.ToList(),
                SkipKey: skipCount > 0 ? skipKey : string.Empty,
                SkipCount: Math.Max(0, skipCount).ToString(CultureInfo.InvariantCulture)),
            out UiProblem? problem);
        if (plan == null)
        {
            error = problem?.Text ?? UiText.T("status.noActive");
            return false;
        }

        return Start(plan, out error);
    }

    /// <summary>
    /// 请求停止并等待后台线程退出（最多 3 秒）。
    /// 返回本轮的真实结果：自然结束（完成 / 出错）就返回那个结果，主动打断才是 <see cref="StopReason.User"/>。
    /// 返回 null 表示**没能停下来**——线程仍在运行，调用方必须保持“运行中”状态，不能当成已停止。
    /// </summary>
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

        // 线程还没退出就清 _running，会让界面以为已经停了，而按键其实还在发；
        // 更糟的是 Start 的防重入检查会失效，可能真的并发起两轮。
        if (!joined) return null;

        _running = false;

        // 本轮已经自己结束了（发满次数 / 发送失败）：如实回报，而不是硬说成用户停止
        EngineStopResult? terminal = _terminalResult;
        if (terminal != null) return terminal;

        return _session == null ? null : new EngineStopResult(StopReason.User, null, _session.Snapshot(), _runId);
    }

    /// <summary>当前（或上一轮）槽位快照。</summary>
    public IReadOnlyList<SlotState> Snapshot() => _session?.Snapshot() ?? Array.Empty<SlotState>();

    public void Dispose()
    {
        Stop();
        _thread = null;
    }

    private void RunLoop(int runId)
    {
        ClickSession session = _session!;
        IReadOnlyList<SendToken>[] tokens = _tokens!;

        try
        {
            TimePeriod.Begin(1);
            try
            {
                // 给触发用的热键一点时间松开修饰键，避免污染发送内容
                if (_warmupMs > 0) Thread.Sleep(_warmupMs);
                if (_stopRequested)
                {
                    Finish(runId, null);
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
                        Finish(runId, null);
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
                            Finish(runId, null);
                            return;
                        }

                        if (!_sender.Send(_skipTokens, out string skipError)) throw new KeySendException(skipError);
                        ProgressChanged?.Invoke();
                        if (_skipDelayMs > 0) Thread.Sleep(_skipDelayMs);
                    }

                    if (_stopRequested)
                    {
                        Finish(runId, null);
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
                        Finish(runId, new EngineStopResult(StopReason.Completed, null, session.Snapshot()));
                        return;
                    }
                }

                Finish(runId, null);
            }
            finally
            {
                TimePeriod.End(1);
            }
        }
        catch (KeySendException ex)
        {
            Finish(runId, new EngineStopResult(StopReason.Error, ex.Message, session.Snapshot()));
        }
        catch (Exception ex)
        {
            Finish(runId, new EngineStopResult(StopReason.Error, "连点异常：" + ex.Message, session.Snapshot()));
        }
    }

    private void Finish(int runId, EngineStopResult? result)
    {
        if (result == null)
        {
            // 用户主动停止也要派发事件：Stop() 3 秒超时返回 null 后，同步路径拿不到结果，
            // 界面只能靠这个事件从「停止中」恢复。正常路径由调用方按轮次编号判重，不会重复处理。
            result = new EngineStopResult(StopReason.User, null, _session?.Snapshot() ?? Array.Empty<SlotState>(), runId);
        }

        // 先把终止结果登记下来，Stop() 才能在 Join 之后读到它——否则界面永远只会看到 User
        EngineStopResult terminal = result with { RunId = runId };

        lock (_gate)
        {
            if (_runId == runId) _terminalResult = terminal;
        }

        _running = false;

        try
        {
            Stopped?.Invoke(terminal);
        }
        catch
        {
            // 事件订阅者异常不应让线程崩溃
        }
    }
}

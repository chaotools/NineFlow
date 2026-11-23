namespace NineFlow.Core;

/// <summary>一轮连点是怎么结束的。</summary>
public enum RunEndKind
{
    /// <summary>用户按了停止（或关闭程序）。</summary>
    UserStopped,

    /// <summary>所有启用行都发满了本轮次数。</summary>
    Completed,

    /// <summary>发送失败等错误。</summary>
    Error,
}

/// <summary>结果的重要程度，界面据此选颜色。</summary>
public enum RunOutcomeSeverity
{
    Normal,
    Success,
    Failure,
}

/// <summary>
/// 一轮结束时界面需要表现的全部内容：进度快照、状态文案、是否弹错误框。
/// 界面只负责把它画出来，不再自己判断「这轮算完成还是算失败」。
/// </summary>
public sealed record RunOutcome(
    RunEndKind Kind,
    IReadOnlyList<SlotState> Slots,
    string StatusKey,
    object[] StatusArgs,
    RunOutcomeSeverity Severity,
    string? ErrorMessage,
    string? ProgressSaveError,
    bool ShowErrorDialog);

/// <summary>
/// 运行结束的协调器：把「用户点停止」和「引擎自然结束（完成 / 出错）」两条路径
/// 合并成同一份状态迁移，并保证同一轮只处理一次。
///
/// 旧实现是两处各写一遍收尾（写进度 → 渲染 → 摘要 → 提示音 → 状态），
/// 靠界面里的 <c>_handledStopRun</c> 手工去重，出错被当成用户停止吞掉过。
/// </summary>
public sealed class RunCoordinator
{
    private readonly Func<IReadOnlyList<SlotState>, string?> _persistProgress;
    private int? _handledRunId;

    /// <param name="persistProgress">把本轮进度并入设置并写盘；成功返回 null，失败返回原因。</param>
    public RunCoordinator(Func<IReadOnlyList<SlotState>, string?> persistProgress)
    {
        _persistProgress = persistProgress ?? throw new ArgumentNullException(nameof(persistProgress));
    }

    /// <summary>
    /// 处理一轮结束。<see cref="EngineStopResult.RunId"/> 必须递增；
    /// 同一轮（或更早的轮次）重复调用会返回 null，调用方不用再判重。
    /// </summary>
    /// <param name="result">引擎给出的停止结果。</param>
    /// <param name="startHotkeyText">启动热键文本，用于「按 X 开始下一轮」这类文案。</param>
    public RunOutcome? Complete(EngineStopResult result, string startHotkeyText)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (_handledRunId.HasValue && result.RunId <= _handledRunId.Value) return null;
        _handledRunId = result.RunId;

        string? saveError = _persistProgress(result.Slots);

        RunEndKind kind = result.Reason switch
        {
            StopReason.Completed => RunEndKind.Completed,
            StopReason.Error => RunEndKind.Error,
            _ => RunEndKind.UserStopped,
        };

        (string key, object[] args, RunOutcomeSeverity severity) = kind switch
        {
            RunEndKind.Completed => ("status.completed", new object[] { startHotkeyText }, RunOutcomeSeverity.Success),
            RunEndKind.Error => ("status.error", new object[] { result.Message ?? string.Empty }, RunOutcomeSeverity.Failure),
            _ => ("status.stoppedNext", new object[] { startHotkeyText }, RunOutcomeSeverity.Normal),
        };

        // 进度没写进磁盘比「已停止」更要紧：状态栏最终显示它（与旧实现一致）
        if (saveError != null)
        {
            (key, args, severity) = ("status.progressSaveFailed", new object[] { saveError }, RunOutcomeSeverity.Failure);
        }

        return new RunOutcome(
            kind,
            result.Slots,
            key,
            args,
            severity,
            result.Message,
            saveError,
            kind == RunEndKind.Error);
    }
}

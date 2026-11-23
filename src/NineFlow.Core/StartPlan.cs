namespace NineFlow.Core;

/// <summary>校验问题做轻量分级，界面据此决定用橙色还是红色显示。</summary>
public enum UiProblemSeverity
{
    /// <summary>输入还不完整 / 尚未准备好：橙色提示，补齐即可。</summary>
    Warning,

    /// <summary>确定无法执行：红色。</summary>
    Error,
}

/// <summary>
/// 一条可以交给界面渲染的问题。Key / Args 直接喂给 <see cref="UiText.T"/>，
/// 界面不再自己拼文案，校验文案也不会在界面和引擎里各存一份。
/// </summary>
public sealed record UiProblem(string Key, object[] Args, UiProblemSeverity Severity = UiProblemSeverity.Error)
{
    public static UiProblem Warn(string key, params object[] args) => new(key, args, UiProblemSeverity.Warning);

    public static UiProblem Error(string key, params object[] args) => new(key, args, UiProblemSeverity.Error);

    /// <summary>渲染后的完整文案（弹窗与测试使用）。</summary>
    public string Text => UiText.T(Key, Args);
}

/// <summary>
/// 一次「开始连点」的原始输入：界面文本 + 已解析的行 + 目标窗口是否可用。
/// 全是数据、不依赖 WinForms，所以可以在控制台里直接测。
/// </summary>
public sealed record StartRequest(
    IReadOnlyList<SlotState> Slots,
    string? StartHotkey = null,
    string? StopHotkey = null,
    string? SkipKey = null,
    string? SkipCount = null,
    bool TargetUsable = true,
    string? ResetHotkey = null);

/// <summary>
/// 校验通过后的执行计划：按键在计划阶段就解析成动作，引擎拿到直接发，不再二次解析。
/// 旧实现是界面解析一遍、引擎再解析一遍，错误文案又只有字符串一种表达。
/// </summary>
public sealed class StartPlan
{
    public StartPlan(
        IReadOnlyList<SlotState> slots,
        IReadOnlyList<SendToken>[] tokens,
        IReadOnlyList<SendToken>? skipTokens,
        int skipCount)
    {
        if (slots.Count != tokens.Length)
        {
            throw new ArgumentException("每个参与发送的行都必须有一份已解析的按键。", nameof(tokens));
        }

        Slots = slots;
        Tokens = tokens;
        SkipTokens = skipTokens;
        SkipCount = Math.Max(0, skipCount);
    }

    /// <summary>本轮参与发送的行（已克隆，按界面行号顺序）。</summary>
    public IReadOnlyList<SlotState> Slots { get; }

    /// <summary>与 <see cref="Slots"/> 一一对应的已解析按键动作。</summary>
    public IReadOnlyList<SendToken>[] Tokens { get; }

    /// <summary>续点要按的键；跳过数为 0 时可能为 null。</summary>
    public IReadOnlyList<SendToken>? SkipTokens { get; }

    /// <summary>本轮开始前要跳过的项数（0 = 不跳过）。</summary>
    public int SkipCount { get; }

    public int ActiveCount => Slots.Count;
}

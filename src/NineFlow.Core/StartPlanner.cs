using System.Globalization;

namespace NineFlow.Core;

/// <summary>
/// 「开始连点」的校验与计划生成：界面只负责读出文本、调用这里、
/// 然后拿到 <see cref="StartPlan"/> 或一条可渲染的 <see cref="UiProblem"/>。
///
/// 校验顺序与旧实现保持一致，用户看到的「第一个问题」不会变：
/// 没有启用行 → 逐行按键合法性与热键撞车 → 跳过键合法性与撞车 → 跳过数 → 目标窗口。
/// </summary>
public static class StartPlanner
{
    /// <summary>校验并生成计划；失败时返回 null，并通过 <paramref name="problem"/> 说明原因。</summary>
    public static StartPlan? TryCreate(StartRequest request, out UiProblem? problem)
    {
        ArgumentNullException.ThrowIfNull(request);

        problem = null;

        List<SlotState> active = request.Slots.Where(slot => slot.IsActive).Select(slot => slot.Clone()).ToList();
        if (active.Count == 0)
        {
            problem = UiProblem.Warn("status.noActive");
            return null;
        }

        HotkeyDef? startHotkey = ParseHotkey(request.StartHotkey);
        HotkeyDef? stopHotkey = ParseHotkey(request.StopHotkey);

        var tokens = new List<SendToken>[active.Count];
        for (int i = 0; i < active.Count; i++)
        {
            SlotState slot = active[i];

            if (!SendSpec.TryParse(slot.Key, out List<SendToken> parsed, out string keyError))
            {
                problem = UiProblem.Error("status.keyInvalid", slot.Index, slot.Key, keyError);
                return null;
            }

            // 撞上自己的全局热键：RegisterHotKey 会在系统输入流这层把键截走，
            // 目标窗口收不到；撞上停止热键还会把正在跑的任务自己停掉。
            if (stopHotkey != null && KeyConflict.Contains(parsed, stopHotkey))
            {
                problem = UiProblem.Error(
                    "status.blocked",
                    UiText.T("row.conflictStop", slot.Index, slot.Key, stopHotkey.Display));
                return null;
            }

            if (startHotkey != null && KeyConflict.Contains(parsed, startHotkey))
            {
                problem = UiProblem.Error(
                    "status.blocked",
                    UiText.T("row.conflictStart", slot.Index, slot.Key, startHotkey.Display));
                return null;
            }

            tokens[i] = parsed;
        }

        string skipKeyText = (request.SkipKey ?? string.Empty).Trim();
        List<SendToken>? skipTokens = null;

        if (skipKeyText.Length > 0)
        {
            if (!SendSpec.TryParse(skipKeyText, out List<SendToken> parsedSkip, out string skipError))
            {
                problem = UiProblem.Error("status.skipInvalid", skipKeyText, skipError);
                return null;
            }

            if (stopHotkey != null && KeyConflict.Contains(parsedSkip, stopHotkey))
            {
                problem = UiProblem.Error(
                    "status.blocked",
                    UiText.T("skip.conflictStop", skipKeyText, stopHotkey.Display));
                return null;
            }

            if (startHotkey != null && KeyConflict.Contains(parsedSkip, startHotkey))
            {
                problem = UiProblem.Error(
                    "status.blocked",
                    UiText.T("skip.conflictStart", skipKeyText, startHotkey.Display));
                return null;
            }

            skipTokens = parsedSkip;
        }

        string skipCountText = (request.SkipCount ?? string.Empty).Trim();
        int skipCount;
        if (skipCountText.Length == 0)
        {
            skipCount = ClickSession.AutoSkipCount(request.Slots);
        }
        else if (int.TryParse(skipCountText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int manualSkip))
        {
            skipCount = Math.Max(0, manualSkip);
        }
        else
        {
            problem = UiProblem.Warn("status.badSkipCount", skipCountText);
            return null;
        }

        if (!request.TargetUsable)
        {
            problem = UiProblem.Warn("status.noTarget", request.StartHotkey ?? string.Empty);
            return null;
        }

        return new StartPlan(active, tokens, skipTokens, skipCount);
    }

    private static HotkeyDef? ParseHotkey(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        return HotkeyDef.TryParse(text, out HotkeyDef? definition, out _) ? definition : null;
    }
}

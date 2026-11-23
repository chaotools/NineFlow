using NineFlow.Core;

namespace NineFlow.Tests;

/// <summary>启动校验与计划生成：这些以前只能靠 UI 自动化验证，现在直接在控制台里测。</summary>
internal static class StartPlannerTests
{
    public static void Run()
    {
        PlansValidRequest();
        RejectsNoActiveRow();
        RejectsInvalidSendKey();
        RejectsConflictWithHotkeys();
        RejectsSkipKeyConflicts();
        RejectsBadSkipCount();
        ResolvesSkipCount();
        WarnsMissingTarget();
        ClonesSlots();
    }

    private static SlotState Slot(int index, string key, bool enabled = true, int done = 0) => new()
    {
        Index = index,
        Enabled = enabled,
        Key = key,
        Interval = 0.02,
        MaxCount = 9,
        Done = done,
    };

    private static void CheckProblem(string name, StartPlan? plan, UiProblem? problem, string expectedKey)
    {
        Program.Check(name, plan == null);
        Program.Equal(name + "：文案键", expectedKey, problem?.Key);
    }

    private static void PlansValidRequest()
    {
        SlotState[] slots =
        {
            Slot(1, "Down"),
            Slot(2, "Enter"),
            Slot(3, "Space", enabled: false),
        };

        StartPlan? plan = StartPlanner.TryCreate(
            new StartRequest(slots, StartHotkey: "F7", StopHotkey: "F8"),
            out UiProblem? problem);

        Program.Check("合法输入生成执行计划", plan != null, problem?.Text);
        Program.Check("合法输入没有问题", problem == null);
        if (plan == null) return;

        Program.Equal("只放行启用且填了按键的行", 2, plan.ActiveCount);
        Program.Check("每个启用行都有已解析的按键", plan.Tokens.Length == 2 && plan.Tokens.All(tokens => tokens.Count > 0));
        Program.Check("不填跳过键时没有跳过动作", plan.SkipTokens == null);
    }

    private static void RejectsNoActiveRow()
    {
        SlotState[] slots = { Slot(1, "Down", enabled: false) };

        StartPlan? plan = StartPlanner.TryCreate(new StartRequest(slots), out UiProblem? problem);

        CheckProblem("没有启用行时拒绝启动", plan, problem, "status.noActive");
        Program.Check("没有启用行属于提示级", problem?.Severity == UiProblemSeverity.Warning);
    }

    private static void RejectsInvalidSendKey()
    {
        SlotState[] slots = { Slot(2, "{Qqq}") };

        StartPlan? plan = StartPlanner.TryCreate(new StartRequest(slots), out UiProblem? problem);

        CheckProblem("无效按键拒绝启动", plan, problem, "status.keyInvalid");
        Program.Check("错误里带行号与原文", problem != null && problem.Text.Contains("第 2 行") && problem.Text.Contains("{Qqq}"));
    }

    private static void RejectsConflictWithHotkeys()
    {
        SlotState[] startConflict = { Slot(1, "F7") };
        StartPlan? plan = StartPlanner.TryCreate(
            new StartRequest(startConflict, StartHotkey: "F7", StopHotkey: "F8"),
            out UiProblem? problem);
        CheckProblem("发送键等于启动热键时拒绝", plan, problem, "status.blocked");
        Program.Check("提示说明撞的是启动热键", problem != null && problem.Text.Contains("启动热键"));

        SlotState[] stopConflict = { Slot(1, "F8") };
        plan = StartPlanner.TryCreate(
            new StartRequest(stopConflict, StartHotkey: "F7", StopHotkey: "F8"),
            out problem);
        CheckProblem("发送键等于停止热键时拒绝", plan, problem, "status.blocked");
        Program.Check("提示说明撞的是停止热键", problem != null && problem.Text.Contains("停止热键"));

        SlotState[] resetConflict = { Slot(1, "F9") };
        plan = StartPlanner.TryCreate(
            new StartRequest(resetConflict, StartHotkey: "F7", StopHotkey: "F8", ResetHotkey: "F9"),
            out problem);
        CheckProblem("发送键等于重置热键时拒绝", plan, problem, "status.blocked");
        Program.Check("提示说明撞的是重置热键", problem != null && problem.Text.Contains("重置进度热键"));
    }

    private static void RejectsSkipKeyConflicts()
    {
        SlotState[] slots = { Slot(1, "Down") };

        StartPlan? plan = StartPlanner.TryCreate(
            new StartRequest(slots, StartHotkey: "F7", SkipKey: "F7", SkipCount: "3"),
            out UiProblem? problem);

        CheckProblem("跳过键等于启动热键时拒绝", plan, problem, "status.blocked");
        Program.Check("提示说明问题出在跳过键", problem != null && problem.Text.Contains("跳过键"));

        plan = StartPlanner.TryCreate(
            new StartRequest(slots, ResetHotkey: "F9", SkipKey: "F9", SkipCount: "3"),
            out problem);
        CheckProblem("跳过键等于重置热键时拒绝", plan, problem, "status.blocked");
    }

    private static void RejectsBadSkipCount()
    {
        SlotState[] slots = { Slot(1, "Down") };

        StartPlan? plan = StartPlanner.TryCreate(
            new StartRequest(slots, SkipKey: "Down", SkipCount: "abc"),
            out UiProblem? problem);

        CheckProblem("跳过数不是整数时拒绝", plan, problem, "status.badSkipCount");
        Program.Check("跳过数问题属于提示级", problem?.Severity == UiProblemSeverity.Warning);
    }

    private static void ResolvesSkipCount()
    {
        SlotState[] slots =
        {
            Slot(1, "Down", done: 40),
            Slot(2, "Enter", done: 7),
            Slot(3, "Space", enabled: false, done: 99),
        };

        StartPlan? plan = StartPlanner.TryCreate(
            new StartRequest(slots, SkipKey: "Down"),
            out UiProblem? problem);

        Program.Check("跳过数留空时生成计划", plan != null, problem?.Text);
        if (plan == null) return;

        Program.Equal("自动跳过数取启用行的累计最大值", 40, plan.SkipCount);
        Program.Check("自动跳过时带上跳过键动作", plan.SkipTokens is { Count: > 0 });

        StartPlan? manual = StartPlanner.TryCreate(new StartRequest(slots, SkipKey: "Down", SkipCount: "3"), out _);
        Program.Check("手动跳过数原样采用", manual?.SkipCount == 3);

        StartPlan? negative = StartPlanner.TryCreate(new StartRequest(slots, SkipCount: "-5"), out _);
        Program.Check("负数跳过数当 0 处理", negative?.SkipCount == 0);
    }

    private static void WarnsMissingTarget()
    {
        SlotState[] slots = { Slot(1, "Down") };

        StartPlan? plan = StartPlanner.TryCreate(
            new StartRequest(slots, StartHotkey: "Ctrl+F7", TargetUsable: false),
            out UiProblem? problem);

        CheckProblem("没有目标窗口时拒绝启动", plan, problem, "status.noTarget");
        Program.Check("提示里带上启动热键", problem != null && problem.Text.Contains("Ctrl+F7"));
    }

    private static void ClonesSlots()
    {
        var slot = new SlotState { Index = 1, Enabled = true, Key = "Down", Interval = 0.02, MaxCount = 9 };

        StartPlan? plan = StartPlanner.TryCreate(new StartRequest(new[] { slot }), out _);

        Program.Check("计划里的槽位是副本", plan != null && !ReferenceEquals(plan.Slots[0], slot));
        if (plan == null) return;

        slot.Key = "Enter";
        Program.Equal("改界面数据不影响已生成的计划", "Down", plan.Slots[0].Key);
    }
}

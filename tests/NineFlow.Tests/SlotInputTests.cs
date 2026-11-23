using NineFlow.Core;

namespace NineFlow.Tests;

/// <summary>界面文本 → SlotState 的解析规则（回退值、钳制、校验时机）。</summary>
internal static class SlotInputTests
{
    public static void Run()
    {
        ParsesNormalRow();
        FallsBackOnEmptyInterval();
        FallsBackOnInvalidInterval();
        ClampsInterval();
        ParsesCount();
        IgnoresProblemsForDisabledOrUnvalidated();
        ParsesBatch();
    }

    private static void ParsesNormalRow()
    {
        var problems = new List<UiProblem>();

        SlotState slot = SlotInputParser.Parse(
            new SlotInput(1, true, " Down ", "0.02", "9", Done: 40, RunCount: 3),
            validate: true,
            problems);

        Program.Equal("按键去空格", "Down", slot.Key);
        Program.Equal("间隔按秒解析", 0.02, slot.Interval);
        Program.Equal("次数按整数解析", 9, slot.MaxCount);
        Program.Equal("保留累计进度", 40, slot.Done);
        Program.Equal("保留本轮进度", 3, slot.RunCount);
        Program.Check("合法行没有问题", problems.Count == 0);
    }

    private static void FallsBackOnEmptyInterval()
    {
        var problems = new List<UiProblem>();

        SlotState slot = SlotInputParser.Parse(new SlotInput(4, true, "Down", string.Empty, "0"), true, problems);

        Program.Equal("空间隔回落到 1.5 秒", SlotInputParser.FallbackInterval, slot.Interval);
        Program.Equal("空间隔产出一条问题", 1, problems.Count);
        Program.Equal("问题键是 intervalEmpty", "row.intervalEmpty", problems[0].Key);
        Program.Check("问题里带行号", problems[0].Text.Contains("第 4 行"));
    }

    private static void FallsBackOnInvalidInterval()
    {
        var problems = new List<UiProblem>();

        SlotState slot = SlotInputParser.Parse(new SlotInput(2, true, "Down", "abc", "9"), true, problems);

        Program.Equal("非法间隔回落到 1.5 秒", SlotInputParser.FallbackInterval, slot.Interval);
        Program.Equal("问题键是 intervalInvalid", "row.intervalInvalid", problems[0].Key);
        Program.Check("问题里带原始文本", problems[0].Text.Contains("abc"));
    }

    private static void ClampsInterval()
    {
        SlotState tiny = SlotInputParser.Parse(new SlotInput(1, true, "Down", "0.001", "9"), false);
        Program.Equal("间隔下限是 0.01 秒", 0.01, tiny.Interval);

        SlotState negative = SlotInputParser.Parse(new SlotInput(1, true, "Down", "-3", "9"), false);
        Program.Equal("负间隔也被抬起", 0.01, negative.Interval);
    }

    private static void ParsesCount()
    {
        SlotState empty = SlotInputParser.Parse(new SlotInput(1, true, "Down", "0.02", string.Empty), false);
        Program.Equal("空次数表示无限", 0, empty.MaxCount);

        var problems = new List<UiProblem>();
        SlotState invalid = SlotInputParser.Parse(new SlotInput(3, true, "Down", "0.02", "xyz"), true, problems);
        Program.Equal("非法次数回落到 0", 0, invalid.MaxCount);
        Program.Equal("问题键是 countInvalid", "row.countInvalid", problems[0].Key);
        Program.Check("次数问题带行号", problems[0].Text.Contains("第 3 行"));

        SlotState negative = SlotInputParser.Parse(new SlotInput(1, true, "Down", "0.02", "-3"), false);
        Program.Equal("负次数被抬到 0", 0, negative.MaxCount);
    }

    private static void IgnoresProblemsForDisabledOrUnvalidated()
    {
        var disabled = new List<UiProblem>();
        SlotInputParser.Parse(new SlotInput(2, false, "Down", "abc", "xyz"), true, disabled);
        Program.Check("未启用的行不报问题", disabled.Count == 0);

        var unvalidated = new List<UiProblem>();
        SlotInputParser.Parse(new SlotInput(2, true, "Down", "abc", "xyz"), false, unvalidated);
        Program.Check("不校验时不报问题", unvalidated.Count == 0);
    }

    private static void ParsesBatch()
    {
        var inputs = new[]
        {
            new SlotInput(1, true, "Down", "0.02", "9"),
            new SlotInput(2, false, string.Empty, string.Empty, string.Empty),
        };

        var problems = new List<UiProblem>();
        List<SlotState> slots = SlotInputParser.ParseAll(inputs, true, problems);

        Program.Equal("批量解析保持条数", 2, slots.Count);
        Program.Equal("第一行行号", 1, slots[0].Index);
        Program.Equal("第二行行号", 2, slots[1].Index);
        Program.Check("未启用的空行不算启用行", !slots[1].IsActive);
        Program.Check("未启用的空行不产生问题", problems.Count == 0);
    }
}

using NineFlow.Core;

namespace NineFlow.Tests;

/// <summary>记录发送内容的假发送器，不触碰真实键盘。</summary>
internal sealed class RecordingSender : IKeySender
{
    private readonly object _gate = new();
    private readonly List<string> _sends = new();

    public bool Fail { get; set; }

    public int CallCount
    {
        get
        {
            lock (_gate) return _sends.Count;
        }
    }

    public bool Send(IReadOnlyList<SendToken> tokens, out string error)
    {
        if (Fail)
        {
            error = "模拟发送失败";
            return false;
        }

        string label = SendSpec.Describe(tokens);
        lock (_gate) _sends.Add(label);

        error = string.Empty;
        return true;
    }

    public int Count(string label)
    {
        lock (_gate) return _sends.Count(item => item == label);
    }

    public List<string> Snapshot()
    {
        lock (_gate) return new List<string>(_sends);
    }
}

internal static class ClickSessionTests
{
    public static void Run()
    {
        // 1) 两行 0.02 秒 / 各 9 次的默认配置
        var slots = new List<SlotState>
        {
            new() { Index = 1, Enabled = true, Key = "Down", Interval = 0.02, MaxCount = 9 },
            new() { Index = 2, Enabled = true, Key = "Enter", Interval = 0.02, MaxCount = 9 },
        };

        var session = new ClickSession(slots);
        session.Begin(0);

        var sentIndexes = new List<int>();
        long firstDue = -1;

        for (long now = 0; now <= 500; now++)
        {
            List<int> due = session.CollectDue(now);
            if (due.Count > 0 && firstDue < 0) firstDue = now;

            foreach (int index in due)
            {
                session.RecordSent(index);
                sentIndexes.Add(index);
            }

            if (session.AllRunsFinished) break;
        }

        Program.Check("默认配置两行都完成", session.AllRunsFinished);
        Program.Equal("发送总数", 18, sentIndexes.Count);
        Program.Equal("第 1 行本轮次数", 9, session.Slots[0].RunCount);
        Program.Equal("第 2 行本轮次数", 9, session.Slots[1].RunCount);
        Program.Equal("第 1 行累计次数", 9, session.Slots[0].Done);
        Program.Equal("首次发送时间点", 20L, firstDue);
        Program.Equal("同刻先发第 1 行", 0, sentIndexes[0]);
        Program.Equal("同刻再发第 2 行", 1, sentIndexes[1]);

        // 2) 不同间隔各自独立
        var mixed = new List<SlotState>
        {
            new() { Index = 1, Enabled = true, Key = "Down", Interval = 0.01, MaxCount = 3 },
            new() { Index = 2, Enabled = true, Key = "Enter", Interval = 0.07, MaxCount = 3 },
        };

        var mixedSession = new ClickSession(mixed);
        mixedSession.Begin(0);
        for (long now = 0; now <= 1000; now++)
        {
            foreach (int index in mixedSession.CollectDue(now)) mixedSession.RecordSent(index);
            if (mixedSession.AllRunsFinished) break;
        }

        Program.Check("混合间隔两行都完成", mixedSession.AllRunsFinished);
        Program.Equal("混合间隔第 1 行", 3, mixedSession.Slots[0].RunCount);
        Program.Equal("混合间隔第 2 行", 3, mixedSession.Slots[1].RunCount);

        // 3) 次数 0 = 无限，永远不会结束
        var infinite = new List<SlotState>
        {
            new() { Index = 1, Enabled = true, Key = "Down", Interval = 0.02, MaxCount = 0 },
        };

        var infiniteSession = new ClickSession(infinite);
        infiniteSession.Begin(0);
        for (long now = 0; now <= 300; now++)
        {
            foreach (int index in infiniteSession.CollectDue(now)) infiniteSession.RecordSent(index);
        }

        Program.Check("无限次数不会结束", !infiniteSession.AllRunsFinished);
        Program.Check("无限次数持续发送", infiniteSession.Slots[0].RunCount >= 10, "次数=" + infiniteSession.Slots[0].RunCount);

        // 4) 停用 / 空按键的行不参与
        var disabled = new List<SlotState>
        {
            new() { Index = 1, Enabled = false, Key = "Down", Interval = 0.02, MaxCount = 2 },
            new() { Index = 2, Enabled = true, Key = "   ", Interval = 0.02, MaxCount = 2 },
        };

        var disabledSession = new ClickSession(disabled);
        Program.Equal("停用与空按键的活动数", 0, disabledSession.ActiveCount);
        Program.Equal("停用与空按键不会到期", 0, disabledSession.CollectDue(1000).Count);

        // 5) 周期换算
        Program.Equal("0.02 秒 → 20 毫秒", 20, new SlotState { Interval = 0.02 }.EffectivePeriodMs);
        Program.Equal("0.07 秒 → 70 毫秒", 70, new SlotState { Interval = 0.07 }.EffectivePeriodMs);
        Program.Equal("1.5 秒 → 1500 毫秒", 1500, new SlotState { Interval = 1.5 }.EffectivePeriodMs);
        Program.Equal("间隔下限 0.01", 0.01, SlotState.ClampInterval(0.0001));
        Program.Equal("间隔下限换算 10 毫秒", 10, new SlotState { Interval = 0.001 }.EffectivePeriodMs);
        Program.Equal("次数下限 0", 0, SlotState.ClampCount(-5));

        // 6) 累计次数取最大值
        var doneSlots = new List<SlotState>
        {
            new() { Index = 1, Done = 5 },
            new() { Index = 2, Done = 12 },
            new() { Index = 3, Done = 0 },
        };
        Program.Equal("MaxDone 取最大值", 12, ClickSession.MaxDone(doneSlots));

        // 6b) 自动续点只统计会真正执行的行：已停用的行不能把跳过数撑大
        var mixedDone = new List<SlotState>
        {
            new() { Index = 1, Enabled = true, Key = "Down", Done = 3 },
            new() { Index = 2, Enabled = true, Key = "Enter", Done = 5 },
            new() { Index = 3, Enabled = false, Key = "2", Done = 40 },
        };
        Program.Equal("MaxDone 仍取全部行的最大值", 40, ClickSession.MaxDone(mixedDone));
        Program.Equal("自动跳过只统计启用的行", 5, ClickSession.AutoSkipCount(mixedDone));
        Program.Equal("全部行都停用时自动跳过为 0", 0, ClickSession.AutoSkipCount(disabled));

        // 7) 快照与时间推进：落后时不会爆发补发
        var burst = new List<SlotState>
        {
            new() { Index = 1, Enabled = true, Key = "Down", Interval = 0.02, MaxCount = 0 },
        };
        var burstSession = new ClickSession(burst);
        burstSession.Begin(0);
        int afterJump = burstSession.CollectDue(1000).Count;
        int immediatelyAgain = burstSession.CollectDue(1000).Count;
        Program.Equal("跳时后只补一次", 1, afterJump);
        Program.Equal("同一时刻不重复补发", 0, immediatelyAgain);
    }
}

internal static class ClickEngineTests
{
    public static void Run()
    {
        // 1) 正常跑完一轮：3 次跳过 + 2 次 Down + 2 次 Enter
        var sender = new RecordingSender();
        using (var engine = new ClickEngine(sender, warmupMs: 0, skipDelayMs: 0))
        {
            var completion = new TaskCompletionSource<EngineStopResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            engine.Stopped += result => completion.TrySetResult(result);

            var slots = new List<SlotState>
            {
                new() { Index = 1, Enabled = true, Key = "Down", Interval = 0.01, MaxCount = 2 },
                new() { Index = 2, Enabled = true, Key = "Enter", Interval = 0.01, MaxCount = 2 },
            };

            Program.Check("引擎可以启动", engine.Start(slots, "Down", 3, out string startError), startError);
            Program.Check("启动后处于运行状态", engine.IsRunning);
            Program.Check("等待本轮完成", completion.Task.Wait(5000), "超时未完成");

            EngineStopResult result = completion.Task.Result;
            Program.Equal("完成时的停止原因", StopReason.Completed, result.Reason);
            Program.Equal("Down 发送总数（含跳过 3 次）", 5, sender.Count("Down"));
            Program.Equal("Enter 发送次数", 2, sender.Count("Enter"));
            Program.Equal("完成后不再运行", false, engine.IsRunning);
            Program.Equal("完成结果中第 1 行累计", 2, result.Slots[0].Done);
            Program.Equal("完成结果中第 2 行累计", 2, result.Slots[1].Done);

            // 回归：本轮已经自然结束时再调 Stop，结果不能被伪装成“用户停止”，
            // 否则界面永远走不到“本轮已完成”的提示。
            EngineStopResult? afterComplete = engine.Stop();
            Program.Check(
                "完成后调用 Stop 仍报告本轮完成",
                afterComplete != null && afterComplete.Reason == StopReason.Completed,
                afterComplete == null ? "null" : afterComplete.Reason.ToString());
            Program.Equal("完成后 Stop 带上同一个轮次编号", result.RunId, afterComplete!.RunId);
            Program.Check("轮次编号有效", result.RunId > 0, "RunId=" + result.RunId);
        }

        // 2) 发送失败 → 以错误停止
        var failing = new RecordingSender { Fail = true };
        using (var failingEngine = new ClickEngine(failing, warmupMs: 0, skipDelayMs: 0))
        {
            var failure = new TaskCompletionSource<EngineStopResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            failingEngine.Stopped += result => failure.TrySetResult(result);

            var slots = new List<SlotState>
            {
                new() { Index = 1, Enabled = true, Key = "Down", Interval = 0.01, MaxCount = 2 },
            };

            Program.Check("失败场景仍可启动", failingEngine.Start(slots, "", 0, out _));
            Program.Check("等待错误停止", failure.Task.Wait(5000), "超时未停止");
            Program.Equal("错误停止原因", StopReason.Error, failure.Task.Result.Reason);
            Program.Check("错误停止带消息", !string.IsNullOrWhiteSpace(failure.Task.Result.Message));
        }

        // 3) 无限模式 + 主动停止
        var infiniteSender = new RecordingSender();
        using (var infiniteEngine = new ClickEngine(infiniteSender, warmupMs: 0, skipDelayMs: 0))
        {
            var slots = new List<SlotState>
            {
                new() { Index = 1, Enabled = true, Key = "Down", Interval = 0.01, MaxCount = 0 },
            };

            Program.Check("无限模式可以启动", infiniteEngine.Start(slots, "", 0, out _));
            Thread.Sleep(150);

            EngineStopResult? stopResult = infiniteEngine.Stop();
            Program.Check("主动停止返回结果", stopResult != null);
            Program.Equal("主动停止原因", StopReason.User, stopResult!.Reason);
            Program.Check("主动停止后不在运行", !infiniteEngine.IsRunning);
            Program.Check(
                "无限模式确实发送了多次",
                stopResult.Slots[0].Done >= 3,
                "累计=" + stopResult.Slots[0].Done);
        }

        // 4) 参数校验
        var guardSender = new RecordingSender();
        using (var guardEngine = new ClickEngine(guardSender, warmupMs: 0, skipDelayMs: 0))
        {
            var invalidKey = new List<SlotState>
            {
                new() { Index = 1, Enabled = true, Key = "{Qqq}", Interval = 0.02, MaxCount = 1 },
            };

            Program.Check("无效按键拒绝启动", !guardEngine.Start(invalidKey, "", 0, out string invalidError));
            Program.Check("无效按键错误信息可读", invalidError.Contains("无效"), invalidError);

            var invalidSkip = new List<SlotState>
            {
                new() { Index = 1, Enabled = true, Key = "Down", Interval = 0.02, MaxCount = 1 },
            };

            Program.Check("无效跳过键拒绝启动", !guardEngine.Start(invalidSkip, "{Qqq}", 3, out _));

            var noneActive = new List<SlotState>
            {
                new() { Index = 1, Enabled = false, Key = "Down", Interval = 0.02, MaxCount = 1 },
            };

            Program.Check("没有启用行拒绝启动", !guardEngine.Start(noneActive, "", 0, out _));
        }
    }
}

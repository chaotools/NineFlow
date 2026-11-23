using NineFlow.Core;

namespace NineFlow.Tests;

/// <summary>一轮结束后的状态迁移：完成 / 出错 / 用户停止不再由界面各写一遍。</summary>
internal static class RunCoordinatorTests
{
    public static void Run()
    {
        MapsCompleted();
        MapsError();
        MapsUserStop();
        IgnoresDuplicateRun();
        SurfacesSaveFailure();
        ErrorKeepsDialogWhenSaveFails();
    }

    private static EngineStopResult Result(StopReason reason, int runId, int done, string? message = null) => new(
        reason,
        message,
        new List<SlotState> { new() { Index = 1, Enabled = true, Key = "Down", Done = done } },
        runId);

    private static RunCoordinator Coordinator(out List<int> persisted, string? saveError = null)
    {
        var recorded = new List<int>();
        persisted = recorded;

        return new RunCoordinator(slots =>
        {
            recorded.Add(slots[0].Done);
            return saveError;
        });
    }

    private static void MapsCompleted()
    {
        RunCoordinator coordinator = Coordinator(out List<int> persisted);

        RunOutcome? outcome = coordinator.Complete(Result(StopReason.Completed, 1, 9), "F7");

        Program.Check("完成时给出结果", outcome != null);
        Program.Equal("完成的状态键", "status.completed", outcome?.StatusKey);
        Program.Check("完成是成功色", outcome?.Severity == RunOutcomeSeverity.Success);
        Program.Check("完成不弹错误框", outcome?.ShowErrorDialog == false);
        Program.Check("完成时会写进度", persisted.Count == 1 && persisted[0] == 9);
        Program.Check("完成文案带上启动热键", outcome is { StatusArgs: [string hotkey] } && hotkey == "F7");
    }

    private static void MapsError()
    {
        RunCoordinator coordinator = Coordinator(out _);

        RunOutcome? outcome = coordinator.Complete(Result(StopReason.Error, 1, 3, "模拟发送失败"), "F7");

        Program.Equal("出错的状态键", "status.error", outcome?.StatusKey);
        Program.Check("出错是失败色", outcome?.Severity == RunOutcomeSeverity.Failure);
        Program.Check("出错要弹提示框", outcome?.ShowErrorDialog == true);
        Program.Check("错误信息原样带出", outcome?.ErrorMessage == "模拟发送失败");
    }

    private static void MapsUserStop()
    {
        RunCoordinator coordinator = Coordinator(out _);

        RunOutcome? outcome = coordinator.Complete(Result(StopReason.User, 1, 2), "F7");

        Program.Equal("主动停止的状态键", "status.stoppedNext", outcome?.StatusKey);
        Program.Check("主动停止是普通色", outcome?.Severity == RunOutcomeSeverity.Normal);
        Program.Check("主动停止不弹提示框", outcome?.ShowErrorDialog == false);
    }

    private static void IgnoresDuplicateRun()
    {
        RunCoordinator coordinator = Coordinator(out List<int> persisted);

        RunOutcome? first = coordinator.Complete(Result(StopReason.Completed, 2, 9), "F7");
        RunOutcome? second = coordinator.Complete(Result(StopReason.Completed, 2, 9), "F7");
        RunOutcome? older = coordinator.Complete(Result(StopReason.Error, 1, 1, "旧轮次"), "F7");

        Program.Check("第一轮正常产出", first != null);
        Program.Check("同一轮重复上报被忽略", second == null);
        Program.Check("更早的轮次也被忽略", older == null);
        Program.Check("重复上报不会重复写盘", persisted.Count == 1);
    }

    private static void SurfacesSaveFailure()
    {
        RunCoordinator coordinator = Coordinator(out _, saveError: "磁盘只读");

        RunOutcome? outcome = coordinator.Complete(Result(StopReason.Completed, 1, 9), "F7");

        Program.Equal("写盘失败时状态被覆盖", "status.progressSaveFailed", outcome?.StatusKey);
        Program.Check("写盘失败是失败色", outcome?.Severity == RunOutcomeSeverity.Failure);
        Program.Check("写盘失败原因带出", outcome?.ProgressSaveError == "磁盘只读");
    }

    private static void ErrorKeepsDialogWhenSaveFails()
    {
        RunCoordinator coordinator = Coordinator(out _, saveError: "磁盘只读");

        RunOutcome? outcome = coordinator.Complete(Result(StopReason.Error, 1, 3, "发送失败"), "F7");

        Program.Check("出错又写盘失败时仍弹提示框", outcome?.ShowErrorDialog == true);
        Program.Equal("状态优先显示写盘失败", "status.progressSaveFailed", outcome?.StatusKey);
    }
}

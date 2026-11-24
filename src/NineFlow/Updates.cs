using System.Globalization;
using System.Windows.Forms;
using NineFlow.Core;

namespace NineFlow;

/// <summary>
/// 检查更新与静默安装。独立成 partial，避免 MainForm.cs 继续变长。
/// 流程：检查 GitHub Release → 有新版弹窗确认 → 下载并校验 → 启动替换脚本 → 退出主程序。
/// </summary>
public sealed partial class MainForm
{
    /// <summary>当前程序版本（只取主/次/修订）。</summary>
    private static Version CurrentVersion =>
        UpdateService.Normalize(typeof(MainForm).Assembly.GetName().Version ?? new Version(0, 0, 0));

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        ReportPreviousUpdateFailure();
        if (_settings.AutoCheckUpdate) _ = AutoCheckForUpdatesAsync();
    }

    /// <summary>上次替换脚本失败会留下日志：启动时提示一次并清理，避免“点了更新却什么都没发生”。</summary>
    private void ReportPreviousUpdateFailure()
    {
        string logPath = Path.Combine(UpdateInstaller.WorkDirectory, "apply-update.log");
        if (!File.Exists(logPath)) return;

        try
        {
            File.Delete(logPath);
        }
        catch
        {
            // 删不掉不影响提示
        }

        SetStatus("status.previousUpdateFailed", Theme.Orange, Array.Empty<object>());
    }

    /// <summary>启动后延迟几秒再查，避免和窗口初始化抢时间；失败保持静默。</summary>
    private async Task AutoCheckForUpdatesAsync()
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(3));
            if (_closing || IsDisposed || _updateBusy || _engine.IsRunning) return;
            await CheckForUpdatesAsync(manual: false);
        }
        catch
        {
            // 自动检查失败不打扰用户
        }
    }

    private async Task CheckForUpdatesAsync(bool manual)
    {
        if (_updateBusy || _closing) return;

        // 录键时钩子会吞掉键盘输入，弹出来的确认框没法用键盘操作；先收掉再检查
        if (_keyCapture.IsCapturing) CancelKeyCapture(restoreValue: true);

        _updateBusy = true;
        _miUpdate.Enabled = false;
        DiagnosticLog.Info("update", (manual ? "手动" : "自动") + "检查更新开始");

        try
        {
            if (manual) SetStatus("status.checkingUpdate", Theme.TextSecondary);

            UpdateCheckResult result = await UpdateService.CheckAsync(CurrentVersion);

            if (result.Status == UpdateCheckStatus.Failed)
            {
                DiagnosticLog.Warn("update", "检查更新失败：" + (result.Error ?? "未知原因"));
                if (manual) SetStatus("status.updateCheckFailed", Theme.Orange, result.Error ?? string.Empty);
                return;
            }

            if (result.Status == UpdateCheckStatus.UpToDate || result.Release is not { } release)
            {
                DiagnosticLog.Info("update", $"已是最新版（{VersionText(result.CurrentVersion)}）");
                if (manual) SetStatus("status.upToDate", Theme.Green, VersionText(result.CurrentVersion));
                return;
            }

            // 自动检查可能在用户开始连点之后才拿到结果：这时绝不能弹模态框抢走前台窗口，
            // 否则后续 SendInput 会打进九序自己的对话框。运行中只给手动检查留一条状态提示。
            if (_engine.IsRunning)
            {
                if (manual)
                {
                    SetStatus(
                        "status.updateAvailableRunning",
                        Theme.Accent,
                        VersionText(release.Version),
                        VersionText(result.CurrentVersion));
                }

                return;
            }

            // 检查期间用户可能刚好开始录键：弹模态框前也必须把钩子收掉，否则键盘全被吞
            if (_keyCapture.IsCapturing) CancelKeyCapture(restoreValue: true);

            string text = UiText.T(
                "dialog.update.text",
                VersionText(result.CurrentVersion),
                VersionText(release.Version),
                FormatSize(release.Size));

            DialogResult answer = MessageBox.Show(
                this,
                text,
                UiText.T("dialog.update.title"),
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information,
                MessageBoxDefaultButton.Button1);

            if (answer != DialogResult.Yes)
            {
                DiagnosticLog.Info("update", "用户取消了更新");
                SetStatus(
                    "status.updateAvailable",
                    Theme.Accent,
                    VersionText(release.Version),
                    VersionText(result.CurrentVersion));
                return;
            }

            await DownloadAndApplyAsync(release);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Warn("update", "检查更新异常：" + ex.Message);
            if (manual) SetStatus("status.updateCheckFailed", Theme.Orange, ex.Message);
        }
        finally
        {
            _updateBusy = false;
            if (!_closing && !IsDisposed) _miUpdate.Enabled = !_engine.IsRunning;
        }
    }

    /// <summary>下载、校验、交给替换脚本；成功返回后主程序退出，由脚本重启。</summary>
    private async Task DownloadAndApplyAsync(UpdateRelease release)
    {
        string fileName = Path.GetFileName(release.AssetName);
        string downloadedPath = Path.Combine(UpdateInstaller.WorkDirectory, fileName);

        var progress = new Progress<int>(percent =>
        {
            if (_closing) return;
            SetStatus("status.downloading", Theme.Accent, VersionText(release.Version), percent);
        });

        SetStatus("status.downloading", Theme.Accent, VersionText(release.Version), 0);

        try
        {
            await UpdateService.DownloadAsync(release, downloadedPath, progress);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Error("update", "更新包下载失败：" + ex.Message);
            ShowUpdateFailure(ex.Message);
            return;
        }

        DiagnosticLog.Info("update", $"更新包下载完成：{downloadedPath}");
        SetStatus("status.updateReady", Theme.Green);

        if (!UpdateInstaller.TryStart(Application.ExecutablePath, downloadedPath, _processId, out string error))
        {
            DiagnosticLog.Error("update", "替换脚本启动失败：" + error);
            ShowUpdateFailure(error);
            return;
        }

        DiagnosticLog.Info("update", "替换脚本已启动，主程序退出等待重启");

        _exiting = true;
        Close();
    }

    private void ShowUpdateFailure(string message)
    {
        SetStatus("status.updateFailed", Theme.Red, message);
        MessageBox.Show(
            this,
            UiText.T("dialog.updateFailed.text", message),
            UiText.T("dialog.updateFailed.title"),
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
    }

    private static string VersionText(Version version) =>
        string.Format(CultureInfo.InvariantCulture, "{0}.{1}.{2}", version.Major, version.Minor, version.Build);

    private static string FormatSize(long bytes) =>
        bytes <= 0
            ? UiText.T("update.sizeUnknown")
            : UiText.T("update.size", (bytes / 1024.0 / 1024.0).ToString("0.0", CultureInfo.InvariantCulture));
}

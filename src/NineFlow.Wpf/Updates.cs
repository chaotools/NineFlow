using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using NineFlow.Core;

namespace NineFlow.Wpf;

/// <summary>
/// 检查更新与静默安装（WPF 版）。流程与 WinForms 版一致：
/// 检查 GitHub Release → 有新版确认 → 下载校验 → 启动替换脚本 → 退出由脚本重启。
/// </summary>
public partial class MainWindow
{
    private static Version CurrentVersion =>
        UpdateService.Normalize(typeof(MainWindow).Assembly.GetName().Version ?? new Version(0, 0, 0));

    private void OnLoadedForUpdates()
    {
        ReportPreviousUpdateFailure();
        if (_settings.AutoCheckUpdate) _ = AutoCheckForUpdatesAsync();
    }

    /// <summary>上次替换脚本失败会留下日志：启动时提示一次并清理。</summary>
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

        SetStatus("status.previousUpdateFailed", "OrangeBrush");
    }

    private async Task AutoCheckForUpdatesAsync()
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(3));
            if (_closing || _updateBusy || _engine.IsRunning) return;
            await CheckForUpdatesAsync(manual: false);
        }
        catch
        {
            // 自动检查失败不打扰
        }
    }

    private void CheckUpdate_Click(object sender, RoutedEventArgs e) => _ = CheckForUpdatesAsync(manual: true);

    private async Task CheckForUpdatesAsync(bool manual)
    {
        if (_updateBusy || _closing) return;

        if (_keyCapture.IsCapturing) CancelKeyCapture(restoreValue: true);

        _updateBusy = true;
        MiUpdate.IsEnabled = false;

        try
        {
            if (manual) SetStatus("status.checkingUpdate", "TextSecondaryBrush");

            UpdateCheckResult result = await UpdateService.CheckAsync(CurrentVersion);

            if (result.Status == UpdateCheckStatus.Failed)
            {
                if (manual) SetStatus("status.updateCheckFailed", "OrangeBrush", result.Error ?? string.Empty);
                return;
            }

            if (result.Status == UpdateCheckStatus.UpToDate || result.Release is not { } release)
            {
                if (manual) SetStatus("status.upToDate", "GreenBrush", VersionText(result.CurrentVersion));
                return;
            }

            // 运行中绝不弹模态框：否则 SendInput 会打进九序自己的对话框
            if (_engine.IsRunning)
            {
                if (manual)
                {
                    SetStatus("status.updateAvailableRunning", "AccentBrush",
                        VersionText(release.Version), VersionText(result.CurrentVersion));
                }

                return;
            }

            if (_keyCapture.IsCapturing) CancelKeyCapture(restoreValue: true);

            string text = UiText.T(
                "dialog.update.text",
                VersionText(result.CurrentVersion),
                VersionText(release.Version),
                FormatSize(release.Size));

            MessageBoxResult answer = System.Windows.MessageBox.Show(
                this,
                text,
                UiText.T("dialog.update.title"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Information,
                MessageBoxResult.Yes);

            if (answer != MessageBoxResult.Yes)
            {
                SetStatus("status.updateAvailable", "AccentBrush",
                    VersionText(release.Version), VersionText(result.CurrentVersion));
                return;
            }

            await DownloadAndApplyAsync(release);
        }
        catch (Exception ex)
        {
            if (manual) SetStatus("status.updateCheckFailed", "OrangeBrush", ex.Message);
        }
        finally
        {
            _updateBusy = false;
            if (!_closing) MiUpdate.IsEnabled = !_engine.IsRunning;
        }
    }

    private async Task DownloadAndApplyAsync(UpdateRelease release)
    {
        string fileName = Path.GetFileName(release.AssetName);
        string downloadedPath = Path.Combine(UpdateInstaller.WorkDirectory, fileName);

        var progress = new Progress<int>(percent =>
        {
            if (_closing) return;
            SetStatus("status.downloading", "AccentBrush", VersionText(release.Version), percent);
        });

        SetStatus("status.downloading", "AccentBrush", VersionText(release.Version), 0);

        try
        {
            await UpdateService.DownloadAsync(release, downloadedPath, progress);
        }
        catch (Exception ex)
        {
            ShowUpdateFailure(ex.Message);
            return;
        }

        SetStatus("status.updateReady", "GreenBrush");

        string exePath = Environment.ProcessPath
                         ?? Process.GetCurrentProcess().MainModule?.FileName
                         ?? string.Empty;

        if (!UpdateInstaller.TryStart(exePath, downloadedPath, _processId, out string error))
        {
            ShowUpdateFailure(error);
            return;
        }

        _exiting = true;
        Close();
    }

    private void ShowUpdateFailure(string message)
    {
        SetStatus("status.updateFailed", "RedBrush", message);
        System.Windows.MessageBox.Show(
            this,
            UiText.T("dialog.updateFailed.text", message),
            UiText.T("dialog.updateFailed.title"),
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private static string VersionText(Version version) =>
        string.Format(CultureInfo.InvariantCulture, "{0}.{1}.{2}", version.Major, version.Minor, version.Build);

    private static string FormatSize(long bytes) =>
        bytes <= 0
            ? UiText.T("update.sizeUnknown")
            : UiText.T("update.size", (bytes / 1024.0 / 1024.0).ToString("0.0", CultureInfo.InvariantCulture));
}

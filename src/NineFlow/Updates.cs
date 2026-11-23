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
        if (_settings.AutoCheckUpdate) _ = AutoCheckForUpdatesAsync();
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

        _updateBusy = true;
        _btnUpdate.Enabled = false;

        try
        {
            if (manual) SetStatus("status.checkingUpdate", Theme.TextSecondary);

            UpdateCheckResult result = await UpdateService.CheckAsync(CurrentVersion);

            if (result.Status == UpdateCheckStatus.Failed)
            {
                if (manual) SetStatus("status.updateCheckFailed", Theme.Orange, result.Error ?? string.Empty);
                return;
            }

            if (result.Status == UpdateCheckStatus.UpToDate || result.Release is not { } release)
            {
                if (manual) SetStatus("status.upToDate", Theme.Green, VersionText(result.CurrentVersion));
                return;
            }

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
            if (manual) SetStatus("status.updateCheckFailed", Theme.Orange, ex.Message);
        }
        finally
        {
            _updateBusy = false;
            if (!_closing && !IsDisposed) _btnUpdate.Enabled = true;
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
            ShowUpdateFailure(ex.Message);
            return;
        }

        SetStatus("status.updateReady", Theme.Green);

        if (!UpdateInstaller.TryStart(Application.ExecutablePath, downloadedPath, _processId, out string error))
        {
            ShowUpdateFailure(error);
            return;
        }

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

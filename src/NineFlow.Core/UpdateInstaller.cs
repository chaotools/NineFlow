namespace NineFlow.Core;

/// <summary>
/// 静默替换正在运行的 exe：
/// 写一个 cmd 脚本 → 启动脚本 → 主程序退出 → 脚本等进程结束、覆盖 exe、重新启动。
/// 覆盖失败（例如目录不可写）时脚本会把旧版重新拉起来，并写一行日志。
/// </summary>
public static class UpdateInstaller
{
    private const string ScriptFileName = "apply-update.cmd";
    private const string LogFileName = "apply-update.log";

    /// <summary>临时脚本所在目录。</summary>
    public static string WorkDirectory => Path.Combine(Path.GetTempPath(), "NineFlow-update");

    /// <summary>启动替换脚本；返回 false 表示脚本都没能起来（此时主程序不应退出）。</summary>
    public static bool TryStart(string targetExe, string downloadedExe, int processId, out string error)
    {
        error = string.Empty;

        try
        {
            if (!File.Exists(downloadedExe))
            {
                throw new FileNotFoundException("找不到已下载的更新包。", downloadedExe);
            }

            if (!File.Exists(targetExe))
            {
                throw new FileNotFoundException("找不到当前程序。", targetExe);
            }

            Directory.CreateDirectory(WorkDirectory);

            string scriptPath = Path.Combine(WorkDirectory, ScriptFileName);
            File.WriteAllText(scriptPath, BuildCommandScript(), new System.Text.UTF8Encoding(false));

            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
                Arguments = $"/c call \"{scriptPath}\" \"{targetExe}\" \"{downloadedExe}\" {processId}",
                WorkingDirectory = WorkDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
            };

            System.Diagnostics.Process.Start(startInfo);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>替换脚本本体；独立出来便于测试与排查。</summary>
    public static string BuildCommandScript() =>
        string.Join(
            Environment.NewLine,
            "@echo off",
            "setlocal enableextensions",
            "set \"TARGET=%~1\"",
            "set \"NEW=%~2\"",
            "set \"PID=%~3\"",
            "",
            "rem wait for the running instance to exit, at most 30 seconds",
            "set /a waits=0",
            ":wait",
            "tasklist /FI \"PID eq %PID%\" /NH 2>nul | find /I \"%PID%\" >nul",
            "if errorlevel 1 goto copy",
            "set /a waits+=1",
            "if %waits% GEQ 30 goto copy",
            "ping -n 2 127.0.0.1 >nul",
            "goto wait",
            "",
            ":copy",
            "set /a tries=0",
            ":copytry",
            "copy /Y \"%NEW%\" \"%TARGET%\" >nul 2>&1",
            "if not errorlevel 1 goto done",
            "set /a tries+=1",
            "if %tries% LSS 10 (",
            "  ping -n 2 127.0.0.1 >nul",
            "  goto copytry",
            ")",
            "echo [%date% %time%] update failed: cannot write \"%TARGET%\" >> \"%~dp0" + LogFileName + "\"",
            "start \"\" \"%TARGET%\"",
            "del \"%~f0\" >nul 2>&1",
            "exit /b 1",
            "",
            ":done",
            "del \"%NEW%\" >nul 2>&1",
            "start \"\" \"%TARGET%\"",
            "del \"%~f0\" >nul 2>&1",
            "exit /b 0",
            "");
}

using System.Text;
using NineFlow.Core;

namespace NineFlow.Tests;

/// <summary>诊断日志测试。测试宿主单线程顺序执行，可以安全地复用 DiagnosticLog.Current 单例。</summary>
internal static class DiagnosticsTests
{
    public static void Run()
    {
        // 无论前面发生了什么，先把全局实例复位成「关闭」
        DiagnosticLog.Current.Disable();

        DisabledByDefault();
        WritesWhenEnabled();
        LevelFormat();
        Rotation();
        BrokenDirectoryDoesNotThrow();
        ResolveDirectoryCreatesFolder();
        SettingsRoundtrip();
        UiTextForDiagnostics();
    }

    private static string NewDirectory(string tag)
    {
        string directory = Path.Combine(Path.GetTempPath(), "nineflow-diag-" + tag + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void DisabledByDefault()
    {
        string directory = NewDirectory("off");
        try
        {
            DiagnosticLog.Current.Configure(directory);
            DiagnosticLog.Info("engine", "这条不应该被写下来");

            Program.Check("关闭状态 Enabled 为 false", !DiagnosticLog.Current.Enabled);
            Program.Check("关闭状态不产生日志文件", !File.Exists(Path.Combine(directory, DiagnosticLog.FileName)));
        }
        finally
        {
            DiagnosticLog.Current.Disable();
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void WritesWhenEnabled()
    {
        string directory = NewDirectory("on");
        try
        {
            DiagnosticLog.Current.Configure(directory);
            DiagnosticLog.Current.Enable();
            DiagnosticLog.Info("engine", "测试消息一");
            DiagnosticLog.Info("app", "测试消息二");
            DiagnosticLog.Current.Disable();

            string path = Path.Combine(directory, DiagnosticLog.FileName);
            Program.Check("开启后产生日志文件", File.Exists(path));

            string text = File.ReadAllText(path, Encoding.UTF8);
            Program.Check("包含会话头（会话标记）", text.Contains("会话"), text);
            Program.Check("会话头包含版本标记", text.Contains("版本 "), text);
            Program.Check("包含第一条日志", text.Contains("[信息] engine · 测试消息一"), text);
            Program.Check("包含第二条日志", text.Contains("[信息] app · 测试消息二"), text);
            Program.Check("日志行带时间戳（ yyyy-MM-dd）", text.Contains(DateTime.Now.ToString("yyyy-MM-dd")), text);
        }
        finally
        {
            DiagnosticLog.Current.Disable();
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void LevelFormat()
    {
        string directory = NewDirectory("level");
        try
        {
            DiagnosticLog.Current.Configure(directory);
            DiagnosticLog.Current.Enable();
            DiagnosticLog.Warn("hotkey", "警告内容");
            DiagnosticLog.Error("engine", "错误内容");
            DiagnosticLog.Exception("engine", new InvalidOperationException("boom"));
            DiagnosticLog.Current.Disable();

            string text = File.ReadAllText(Path.Combine(directory, DiagnosticLog.FileName), Encoding.UTF8);
            Program.Check("警告级别格式", text.Contains("[警告] hotkey · 警告内容"), text);
            Program.Check("错误级别格式", text.Contains("[错误] engine · 错误内容"), text);
            Program.Check("异常类型被记录", text.Contains("InvalidOperationException"), text);
            Program.Check("异常消息被记录", text.Contains("boom"), text);
        }
        finally
        {
            DiagnosticLog.Current.Disable();
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void Rotation()
    {
        string directory = NewDirectory("rotate");
        try
        {
            // 下限 64KB：写约 90 行 1KB 的日志，让当前文件必然超过上限触发轮转
            DiagnosticLog.Current.Configure(directory, maxBytes: 64 * 1024);
            DiagnosticLog.Current.Enable();

            string payload = new('x', 1000);
            for (int i = 0; i < 90; i++)
            {
                DiagnosticLog.Info("rotate", i + " " + payload);
            }

            DiagnosticLog.Current.Disable();

            string oldPath = Path.Combine(directory, DiagnosticLog.OldFileName);
            string currentPath = Path.Combine(directory, DiagnosticLog.FileName);
            Program.Check("轮转后存在 .old 文件", File.Exists(oldPath));
            Program.Check("轮转后当前文件仍在", File.Exists(currentPath));
            Program.Check("当前文件小于上限", new FileInfo(currentPath).Length <= 64 * 1024 + 4096);
        }
        finally
        {
            DiagnosticLog.Current.Disable();
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void BrokenDirectoryDoesNotThrow()
    {
        // 把目录指到一个「文件的子路径」上，让每次写入都必然失败
        string root = NewDirectory("broken");
        string filePath = Path.Combine(root, "occupied.txt");
        File.WriteAllText(filePath, "占位");
        string impossible = Path.Combine(filePath, "sub");

        try
        {
            DiagnosticLog.Current.Configure(impossible);
            DiagnosticLog.Current.Enable();

            Program.Check("坏目录下 Enable 不抛异常", true);
            Program.Check("坏目录下 Enabled 变为 false", !DiagnosticLog.Current.Enabled);
            DiagnosticLog.Info("engine", "这条写不进去也不该抛异常");
        }
        finally
        {
            DiagnosticLog.Current.Disable();
            Directory.Delete(root, recursive: true);
        }
    }

    private static void ResolveDirectoryCreatesFolder()
    {
        string baseDirectory = NewDirectory("resolve");
        try
        {
            string resolved = DiagnosticLog.ResolveDirectory(baseDirectory);
            Program.Check("返回 exe 旁的 logs 目录", resolved == Path.Combine(baseDirectory, "logs"), resolved);
            Program.Check("目录已被创建", Directory.Exists(resolved));

            // 再次解析得到同一结果（幂等）
            string again = DiagnosticLog.ResolveDirectory(baseDirectory);
            Program.Equal("重复解析结果一致", resolved, again);
        }
        finally
        {
            DiagnosticLog.Current.Disable();
            Directory.Delete(baseDirectory, recursive: true);
        }
    }

    private static void SettingsRoundtrip()
    {
        string directory = NewDirectory("store");
        try
        {
            string path = Path.Combine(directory, SettingsStore.FileName);
            var store = new SettingsStore(path);

            // 默认关闭
            AppSettings defaults = store.Load();
            Program.Check("诊断日志默认关闭", !defaults.Diagnostics);

            var settings = new AppSettings { Diagnostics = true };
            settings.Slots[0].Done = 12;
            Program.Check("准备：保存配置", store.SaveConfig(settings, out string saveError), saveError);

            AppSettings loaded = store.Load();
            Program.Check("诊断开关已持久化", loaded.Diagnostics);
            Program.Equal("开关持久化不碰进度", 12, loaded.Slots[0].Done);

            // SaveOptions 单独保存也要带上它
            var options = new AppSettings { Diagnostics = true, Beep = false };
            Program.Check("准备：保存界面选项", store.SaveOptions(options, out string optionError), optionError);
            Program.Check("SaveOptions 持久化诊断开关", store.Load().Diagnostics);
            Program.Check("SaveOptions 持久化提示音开关", !store.Load().Beep);
        }
        finally
        {
            DiagnosticLog.Current.Disable();
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void UiTextForDiagnostics()
    {
        string[] required =
        {
            "switch.diagnostics", "menu.openLog", "tooltip.diagnostics", "tooltip.openLog",
            "status.logEmpty", "status.logOpenFailed",
        };

        foreach (string key in required)
        {
            Program.Check("文案存在：" + key, UiText.HasKey(key));
        }
    }
}

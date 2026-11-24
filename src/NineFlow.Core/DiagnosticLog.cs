using System.Security.Principal;
using System.Text;

namespace NineFlow.Core;

/// <summary>
/// 诊断日志：把启动、热键、连点、更新等关键动作记到本地文件，用于排查
/// 「按键没发出去」「热键没反应」这类问题。默认关闭，由设置开关控制。
///
/// 设计约束：
/// - 日志只写本机（exe 同目录 logs\，写不动就退回 %LOCALAPPDATA%\NineFlow\logs），不联网不上传；
/// - 写失败一律静默吞掉：诊断是锦上添花，绝不能反过来影响连点主流程；
/// - 全部方法线程安全：引擎线程、界面线程、ShowListener 线程都会来写；
/// - 按大小轮转（当前文件超过上限就改名 .old，最多保留两份），避免无限膨胀。
/// </summary>
public sealed class DiagnosticLog
{
    /// <summary>单文件大小上限，超过就轮转。</summary>
    public const long DefaultMaxBytes = 1024 * 1024;

    public const string FileName = "NineFlow-diagnostics.log";
    public const string OldFileName = "NineFlow-diagnostics.old.log";

    private static readonly DiagnosticLog Shared = new();

    /// <summary>全局实例。Core 层的类没有公共入口传依赖，用静态门面最省事；测试请自建实例。</summary>
    public static DiagnosticLog Current => Shared;

    private readonly object _gate = new();
    private bool _enabled;
    private bool _broken;
    private string? _directory;
    private string? _filePath;
    private long _maxBytes = DefaultMaxBytes;

    private DiagnosticLog()
    {
    }

    /// <summary>是否正在记录。关闭时所有写入都是空操作。</summary>
    public bool Enabled
    {
        get
        {
            lock (_gate) return _enabled && !_broken;
        }
    }

    /// <summary>当前日志目录；未配置时为 null。</summary>
    public string? Directory
    {
        get
        {
            lock (_gate) return _directory;
        }
    }

    /// <summary>当前日志文件完整路径；尚未写出任何内容或未开启时可能为 null。</summary>
    public string? FilePath
    {
        get
        {
            lock (_gate) return _filePath;
        }
    }

    /// <summary>
    /// 选定日志目录：exe 同目录可写就用它（便携式安装的直觉位置），
    /// 写不动（Program Files 等只读位置）就退回 %LOCALAPPDATA%\NineFlow\logs。
    /// 目录不存在会创建；创建失败会抛异常，调用方决定如何兜底。
    /// </summary>
    public static string ResolveDirectory(string baseDirectory)
    {
        string candidate = Path.Combine(baseDirectory, "logs");
        try
        {
            System.IO.Directory.CreateDirectory(candidate);
            string probe = Path.Combine(candidate, ".write-probe");
            File.WriteAllText(probe, "probe");
            File.Delete(probe);
            return candidate;
        }
        catch
        {
            string fallback = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NineFlow", "logs");
            System.IO.Directory.CreateDirectory(fallback);
            return fallback;
        }
    }

    /// <summary>配置目录与轮转上限（在 Enabled = true 之前调用）。</summary>
    public void Configure(string directory, long maxBytes = DefaultMaxBytes)
    {
        lock (_gate)
        {
            _directory = directory;
            _filePath = Path.Combine(directory, FileName);
            _maxBytes = Math.Max(64 * 1024, maxBytes);
        }
    }

    /// <summary>开启记录；首次开启会写入一段会话头（版本 / 系统 / 是否管理员）。</summary>
    public void Enable()
    {
        lock (_gate)
        {
            if (_enabled) return;
            if (_filePath == null)
            {
                // 没先 Configure 也能用：退回 %LOCALAPPDATA%，保证开关永远可用
                string fallback = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "NineFlow", "logs");
                Configure(fallback);
            }

            _enabled = true;
            _broken = false;
            WriteHeaderLocked();
        }
    }

    /// <summary>关闭记录。已写出的文件保留在磁盘上，供用户回看。</summary>
    public void Disable()
    {
        lock (_gate)
        {
            _enabled = false;
        }
    }

    public static void Info(string category, string message) => Shared.Write("信息", category, message);

    public static void Warn(string category, string message) => Shared.Write("警告", category, message);

    public static void Error(string category, string message) => Shared.Write("错误", category, message);

    /// <summary>记一条异常（类型 + 消息 + 堆栈）。</summary>
    public static void Exception(string category, Exception? exception)
    {
        if (exception == null) return;
        Shared.Write("错误", category, exception.ToString());
    }

    private void Write(string level, string category, string message)
    {
        lock (_gate)
        {
            if (!_enabled || _broken) return;
            WriteLocked(level, category, message);
        }
    }

    private void WriteHeaderLocked()
    {
        string text =
            "版本 " + AppVersion() +
            " · " + Environment.OSVersion.VersionString +
            " · " + Environment.Version.ToString() +
            " · " + (IsElevated() ? "管理员" : "普通权限");
        WriteLocked("信息", "会话", text);
    }

    private void WriteLocked(string level, string category, string message)
    {
        try
        {
            string path = _filePath!;
            RotateIfNeededLocked(path);
            string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture)
                + " [" + level + "] " + category + " · " + message.ReplaceLineEndings(" ");
            File.AppendAllText(path, line + Environment.NewLine, new UTF8Encoding(false));
        }
        catch
        {
            // 目录被删 / 磁盘满 / 杀软拦截：放弃本次写入；连续失败不再反复尝试
            _broken = true;
        }
    }

    /// <summary>超过上限就把当前文件改名成 .old（覆盖旧的），再从头写。</summary>
    private void RotateIfNeededLocked(string path)
    {
        try
        {
            FileInfo info = new(path);
            if (!info.Exists || info.Length < _maxBytes) return;

            string old = Path.Combine(Path.GetDirectoryName(path)!, OldFileName);
            File.Delete(old);
            File.Move(path, old);
        }
        catch
        {
            // 轮转失败不致命：文件继续追加，下次再试
        }
    }

    private static string AppVersion()
    {
        Version version = typeof(DiagnosticLog).Assembly.GetName().Version ?? new Version(0, 0, 0);
        return "v" + version.Major + "." + version.Minor + "." + version.Build;
    }

    private static bool IsElevated()
    {
        try
        {
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }
}

using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace NineFlow.Wpf;

/// <summary>
/// 入口：单实例（命名互斥量 + 命名事件）、全局异常兜底、把临时目录指到 exe 旁边。
/// 入口只做一次性初始化，真正的界面与业务编排在 MainWindow。
/// </summary>
public partial class App : System.Windows.Application
{
    private const string MutexName = @"Local\NineFlow.SingleInstance.v1";
    private const string ShowEventName = @"Local\NineFlow.ShowWindow.v1";

    private Mutex? _mutex;
    private EventWaitHandle? _showEvent;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        RedirectTempToAppDirectory();

        _mutex = new Mutex(initiallyOwned: true, name: MutexName, out bool createdNew);
        if (!createdNew)
        {
            SignalExistingInstance();
            Shutdown();
            return;
        }

        _showEvent = new EventWaitHandle(initialState: false, EventResetMode.AutoReset, ShowEventName);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => ShowFatalError(args.ExceptionObject as Exception);

        var window = new MainWindow(_showEvent);
        MainWindow = window;
        window.Show();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        ShowFatalError(e.Exception);
        e.Handled = true;
    }

    private static void RedirectTempToAppDirectory()
    {
        try
        {
            string directory = System.IO.Path.Combine(AppContext.BaseDirectory, ".temp");
            System.IO.Directory.CreateDirectory(directory);
            Environment.SetEnvironmentVariable("TMP", directory);
            Environment.SetEnvironmentVariable("TEMP", directory);
            Environment.SetEnvironmentVariable("TMPDIR", directory);
        }
        catch
        {
            // 目录不可写时继续用系统 TEMP
        }
    }

    private static void SignalExistingInstance()
    {
        try
        {
            using EventWaitHandle handle = EventWaitHandle.OpenExisting(ShowEventName);
            handle.Set();
        }
        catch
        {
            System.Windows.MessageBox.Show(
                "九序已经在运行，已为你唤出窗口。",
                "九序",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    private static void ShowFatalError(Exception? exception)
    {
        try
        {
            NineFlow.Core.DiagnosticLog.Exception("崩溃", exception);
        }
        catch
        {
            // 诊断日志失败不影响原有的临时目录日志
        }

        try
        {
            string logPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "NineFlow-wpf-error.log");
            System.IO.File.AppendAllText(
                logPath,
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + Environment.NewLine + exception +
                Environment.NewLine + new string('-', 60) + Environment.NewLine);
        }
        catch
        {
            // 写日志失败不影响提示
        }

        System.Windows.MessageBox.Show(
            (exception?.Message ?? "Unknown error") + Environment.NewLine + Environment.NewLine + exception?.GetType().Name,
            "九序",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }
}

using System.Windows.Forms;
using NineFlow.Core;

namespace NineFlow;

internal static class Program
{
    private const string MutexName = @"Local\NineFlow.SingleInstance.v1";
    private const string ShowEventName = @"Local\NineFlow.ShowWindow.v1";

    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(initiallyOwned: true, name: MutexName, out bool createdNew);

        if (!createdNew)
        {
            SignalExistingInstance();
            return;
        }

        using var showEvent = new EventWaitHandle(initialState: false, EventResetMode.AutoReset, ShowEventName);

        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, args) => ShowFatalError(args.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, args) => ShowFatalError(args.ExceptionObject as Exception);

        Application.Run(new MainForm(showEvent));

        GC.KeepAlive(mutex);
    }

    /// <summary>已经有实例在运行：让它把窗口唤到前台，然后自己退出。</summary>
    private static void SignalExistingInstance()
    {
        try
        {
            using EventWaitHandle handle = EventWaitHandle.OpenExisting(ShowEventName);
            handle.Set();
        }
        catch
        {
            MessageBox.Show(
                UiText.T("dialog.running.text"),
                UiText.T("app.name"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
    }

    private static void ShowFatalError(Exception? exception)
    {
        // 出错时把完整堆栈写到临时目录，便于排查（不联网、不上传）
        try
        {
            string logPath = Path.Combine(Path.GetTempPath(), "NineFlow-error.log");
            File.AppendAllText(logPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + Environment.NewLine + exception + Environment.NewLine + new string('-', 60) + Environment.NewLine);
        }
        catch
        {
            // 写日志失败不影响提示
        }

        MessageBox.Show(
            (exception?.Message ?? "Unknown error") + Environment.NewLine + Environment.NewLine + exception?.GetType().Name,
            UiText.T("app.name"),
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }
}

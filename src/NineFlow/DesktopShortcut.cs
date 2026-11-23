using NineFlow.Core;

namespace NineFlow;

/// <summary>
/// 桌面快捷方式：首次运行静默创建一次（NineFlow.ini 里有 [App] ShortcutCreated=1 标记就不再动）。
/// 快捷方式指向 exe 绝对路径，工作目录设为 exe 所在目录，保证 NineFlow.ini 跟着 exe 走。
/// 注意：以管理员身份运行时拿到的是当前（提权）用户的桌面；快捷方式记录绝对路径，
/// 之后挪动文件夹的话快捷方式会失效，需要删掉重开一次程序重建。
/// </summary>
internal static class DesktopShortcut
{
    public static string ShortcutPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            UiText.T("app.name") + ".lnk");

    /// <summary>创建桌面快捷方式；任何失败（COM 缺失、桌面不可写等）都静默返回 false，不阻塞启动。</summary>
    public static bool TryCreate(string exePath)
    {
        try
        {
            Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return false;

            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic shortcut = shell.CreateShortcut(ShortcutPath);

            shortcut.TargetPath = exePath;
            shortcut.WorkingDirectory = Path.GetDirectoryName(exePath) ?? string.Empty;
            shortcut.Description = UiText.T("app.name");
            shortcut.Save();
            return true;
        }
        catch
        {
            return false;
        }
    }
}

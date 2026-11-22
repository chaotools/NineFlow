using NineFlow.Core;

namespace NineFlow.Tests;

internal static class UiTextTests
{
    public static void Run()
    {
        Program.Equal("软件中文名是九序", "九序", UiText.T("app.name"));
        Program.Equal("带参数的文案", "状态：配置已保存到 C:\\demo", UiText.T("status.saved", "C:\\demo"));
        Program.Check("未知键回退为键名", UiText.T("no.such.key") == "no.such.key");
        Program.Check("文案表非空", UiText.AllKeys.Any());

        // 关键界面文案必须都存在
        string[] required =
        {
            "app.name", "app.subtitle", "card.slots.title", "card.hotkeys.title",
            "button.save", "button.load", "button.start", "button.stop", "button.reset",
            "status.stopped", "status.running", "summary.prefix", "summary.none",
            "tray.show", "tray.exit", "switch.beep", "switch.closeToTray", "switch.topMost",
            "hint.resume", "note.read",
        };

        foreach (string key in required)
        {
            Program.Check("文案存在：" + key, UiText.HasKey(key));
        }
    }
}

internal static class AppSettingsOptionsTests
{
    public static void Run()
    {
        string directory = Path.Combine(Path.GetTempPath(), "nineflow-options-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            string path = Path.Combine(directory, SettingsStore.FileName);
            var store = new SettingsStore(path);

            var settings = new AppSettings();
            settings.Slots[0].Done = 40;
            settings.HotkeyOn = "Ctrl+F7";
            Program.Check("准备：保存完整配置", store.SaveConfig(settings, out string saveError), saveError);

            settings.Beep = false;
            settings.CloseToTray = false;
            settings.TopMost = true;
            Program.Check("保存界面选项成功", store.SaveOptions(settings, out string optionError), optionError);

            AppSettings loaded = store.Load();
            Program.Check("提示音开关已持久化", !loaded.Beep);
            Program.Check("关闭到托盘开关已持久化", !loaded.CloseToTray);
            Program.Check("窗口置顶开关已持久化", loaded.TopMost);
            Program.Equal("界面选项不覆盖累计进度", 40, loaded.Slots[0].Done);
            Program.Equal("界面选项不覆盖热键", "Ctrl+F7", loaded.HotkeyOn);

            var defaults = new AppSettings();
            Program.Check("默认关闭到托盘", defaults.CloseToTray);
            Program.Check("默认开启提示音", defaults.Beep);
            Program.Check("默认不置顶", !defaults.TopMost);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}

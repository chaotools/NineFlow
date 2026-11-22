using System.Text;
using NineFlow.Core;

namespace NineFlow.Tests;

internal static class IniDocumentTests
{
    public static void Run()
    {
        IniDocument document = IniDocument.Parse(
            "; 注释行\r\n" +
            "[Slot1]\r\n" +
            "Enabled=1\r\n" +
            "Key=Down\r\n" +
            "\r\n" +
            "[Hotkey]\r\n" +
            "On=F7\r\n");

        Program.Equal("解析 Enabled", "1", document.Get("Slot1", "Enabled"));
        Program.Equal("解析 Key", "Down", document.Get("Slot1", "Key"));
        Program.Equal("解析 Hotkey.On", "F7", document.Get("Hotkey", "On"));
        Program.Equal("缺失键返回 null", null, document.Get("Slot1", "Nope"));
        Program.Equal("缺失节返回 null", null, document.Get("Nope", "Key"));
        Program.Equal("节名大小写不敏感", "1", document.Get("slot1", "ENABLED"));

        IniDocument quoted = IniDocument.Parse("[A]\nB=\"hello world\"\nC='x'\n");
        Program.Equal("剥离双引号", "hello world", quoted.Get("A", "B"));
        Program.Equal("剥离单引号", "x", quoted.Get("A", "C"));

        // 写入时保留未知的键
        document.Set("Slot1", "Key", "Enter");
        document.Set("Slot1", "Extra", "x");
        string serialized = document.Serialize();
        Program.Check("序列化包含新值", serialized.Contains("Key=Enter"), serialized);
        Program.Check("序列化保留未知键", serialized.Contains("Extra=x"), serialized);

        string tempPath = Path.Combine(Path.GetTempPath(), "nineflow-ini-" + Guid.NewGuid().ToString("N") + ".ini");
        string utf16Path = Path.Combine(Path.GetTempPath(), "nineflow-ini16-" + Guid.NewGuid().ToString("N") + ".ini");

        try
        {
            document.Save(tempPath);
            IniDocument reloaded = IniDocument.Load(tempPath);
            Program.Equal("往返 Key", "Enter", reloaded.Get("Slot1", "Key"));
            Program.Equal("往返 Extra", "x", reloaded.Get("Slot1", "Extra"));
            Program.Equal("往返 Enabled", "1", reloaded.Get("Slot1", "Enabled"));

            // UTF-16（带 BOM）也要能读
            File.WriteAllText(utf16Path, "[Slot1]\r\nKey=回车\r\n", Encoding.Unicode);
            IniDocument fromUtf16 = IniDocument.Load(utf16Path);
            Program.Equal("识别 UTF-16 BOM", "回车", fromUtf16.Get("Slot1", "Key"));
        }
        finally
        {
            File.Delete(tempPath);
            File.Delete(utf16Path);
        }
    }
}

internal static class SettingsStoreTests
{
    public static void Run()
    {
        string directory = Path.Combine(Path.GetTempPath(), "nineflow-store-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            string path = Path.Combine(directory, SettingsStore.FileName);
            var store = new SettingsStore(path);

            // 1) 无配置文件 → 出厂默认值
            AppSettings defaults = store.Load();
            Program.Equal("默认行数", 10, defaults.Slots.Count);
            Program.Check("默认第 1 行启用", defaults.Slots[0].Enabled);
            Program.Equal("默认第 1 行按键", "Down", defaults.Slots[0].Key);
            Program.Equal("默认第 1 行间隔", 0.02, defaults.Slots[0].Interval);
            Program.Equal("默认第 1 行次数", 9, defaults.Slots[0].MaxCount);
            Program.Check("默认第 2 行启用", defaults.Slots[1].Enabled);
            Program.Equal("默认第 2 行按键", "Enter", defaults.Slots[1].Key);
            Program.Check("默认第 3 行禁用", !defaults.Slots[2].Enabled);
            Program.Equal("默认启动热键", "F7", defaults.HotkeyOn);
            Program.Equal("默认停止热键", "F8", defaults.HotkeyOff);
            Program.Equal("默认跳过键", "Down", defaults.SkipKey);
            Program.Equal("默认跳过数为空", string.Empty, defaults.SkipCount);
            Program.Check("读取前文件不存在", !store.Exists);

            // 2) 保存 → 读取
            var settings = new AppSettings();
            settings.Slots[0].Done = 27;
            settings.Slots[1].MaxCount = 5;
            settings.Slots[2].Enabled = true;
            settings.Slots[2].Key = "^a";
            settings.Slots[2].Interval = 0.35;
            settings.Slots[2].MaxCount = 3;
            settings.HotkeyOn = "Ctrl+F7";
            settings.SkipCount = "7";

            Program.Check("保存配置成功", store.SaveConfig(settings, out string saveError), saveError);
            Program.Check("保存后文件存在", store.Exists);

            AppSettings loaded = store.Load();
            Program.Equal("读取累计次数", 27, loaded.Slots[0].Done);
            Program.Equal("读取次数", 5, loaded.Slots[1].MaxCount);
            Program.Check("读取启用状态", loaded.Slots[2].Enabled);
            Program.Equal("读取组合键", "^a", loaded.Slots[2].Key);
            Program.Equal("读取间隔", 0.35, loaded.Slots[2].Interval);
            Program.Equal("读取启动热键", "Ctrl+F7", loaded.HotkeyOn);
            Program.Equal("读取跳过数", "7", loaded.SkipCount);

            // 3) 保存进度不应破坏其它配置
            List<SlotState> runtime = settings.Slots.Select(slot => slot.Clone()).ToList();
            runtime[0].Done = 40;
            Program.Check("保存进度成功", store.SaveProgress(runtime, out string progressError), progressError);

            AppSettings afterProgress = store.Load();
            Program.Equal("进度已更新", 40, afterProgress.Slots[0].Done);
            Program.Equal("进度保存后配置仍在", 5, afterProgress.Slots[1].MaxCount);
            Program.Equal("进度保存后热键仍在", "Ctrl+F7", afterProgress.HotkeyOn);

            // 4) 损坏的文件不应该抛异常
            File.WriteAllText(path, "这不是 ini 文件\n[Slot1\nEnabled=\n");
            AppSettings damaged = store.Load();
            Program.Equal("损坏文件回退默认行数", 10, damaged.Slots.Count);
            Program.Equal("损坏文件回退默认按键", "Down", damaged.Slots[0].Key);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

}

using System.Globalization;

namespace NineFlow.Core;

/// <summary>应用设置（界面 + 累计进度）。</summary>
public sealed class AppSettings
{
    public List<SlotState> Slots { get; set; } = AppDefaults.CreateSlots();

    public string HotkeyOn { get; set; } = AppDefaults.HotkeyOn;

    public string HotkeyOff { get; set; } = AppDefaults.HotkeyOff;

    /// <summary>重置进度的全局快捷键。</summary>
    public string HotkeyReset { get; set; } = AppDefaults.HotkeyReset;

    public string SkipKey { get; set; } = AppDefaults.SkipKey;

    /// <summary>跳过数，空 = 自动取累计次数。</summary>
    public string SkipCount { get; set; } = string.Empty;

    /// <summary>点关闭按钮时隐藏到托盘而不是退出。</summary>
    public bool CloseToTray { get; set; } = true;

    /// <summary>开始 / 停止时播放提示音。</summary>
    public bool Beep { get; set; } = true;

    /// <summary>主窗口置顶。</summary>
    public bool TopMost { get; set; }

    /// <summary>启动后自动检查 GitHub 上的新版本。</summary>
    public bool AutoCheckUpdate { get; set; } = true;

    /// <summary>读取配置过程中的提示信息（键名，由界面翻译）。</summary>
    public List<string> Notes { get; } = new();
}

/// <summary>配置文件的读写：NineFlow.ini。</summary>
public sealed class SettingsStore
{
    public const string FileName = "NineFlow.ini";

    public SettingsStore(string path)
    {
        Path = path;
    }

    public string Path { get; }

    public bool Exists => File.Exists(Path);

    public static string DefaultIniPath(string baseDirectory) =>
        System.IO.Path.Combine(baseDirectory, FileName);

    /// <summary>读取设置；文件不存在时返回出厂默认值。</summary>
    public AppSettings Load()
    {
        var settings = new AppSettings();

        if (!File.Exists(Path)) return settings;

        IniDocument document;
        try
        {
            document = IniDocument.Load(Path);
        }
        catch (Exception ex)
        {
            settings.Notes.Add("read|" + ex.Message);
            return settings;
        }

        // 行数由文件里实际存在的 SlotN 决定：旧配置有 10 行就读出 10 行，
        // 新配置只有 2 行就只读 2 行；至少保留 MinSlots 行。
        int maxSlot = 0;
        foreach (IniSection section in document.Sections)
        {
            if (!section.Name.StartsWith("Slot", StringComparison.OrdinalIgnoreCase)) continue;
            if (!int.TryParse(section.Name.AsSpan(4), out int number) || number <= 0) continue;
            if (number > maxSlot) maxSlot = number;
        }

        settings.Slots = AppDefaults.CreateSlots(Math.Max(AppDefaults.MinSlots, maxSlot));

        for (int index = 1; index <= settings.Slots.Count; index++)
        {
            SlotState slot = settings.Slots[index - 1];
            string section = "Slot" + index.ToString(CultureInfo.InvariantCulture);

            bool? enabled = document.GetBoolOrNull(section, "Enabled");
            if (enabled.HasValue) slot.Enabled = enabled.Value;

            string? key = document.Get(section, "Key");
            if (key != null) slot.Key = key.Trim();

            string? intervalText = document.Get(section, "Interval");
            if (!string.IsNullOrWhiteSpace(intervalText) && TryParseDouble(intervalText, out double interval))
            {
                slot.Interval = SlotState.ClampInterval(interval);
            }

            string? maxCountText = document.Get(section, "MaxCount");
            if (!string.IsNullOrWhiteSpace(maxCountText) &&
                int.TryParse(maxCountText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int maxCount))
            {
                slot.MaxCount = SlotState.ClampCount(maxCount);
            }

            string? doneText = document.Get(section, "Done");
            if (!string.IsNullOrWhiteSpace(doneText) &&
                int.TryParse(doneText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int done))
            {
                slot.Done = SlotState.ClampCount(done);
            }
        }

        settings.HotkeyOn = NonEmpty(document.Get("Hotkey", "On")) ?? AppDefaults.HotkeyOn;
        settings.HotkeyOff = NonEmpty(document.Get("Hotkey", "Off")) ?? AppDefaults.HotkeyOff;
        settings.HotkeyReset = NonEmpty(document.Get("Hotkey", "Reset")) ?? AppDefaults.HotkeyReset;
        settings.SkipKey = document.Get("Skip", "Key") ?? AppDefaults.SkipKey;
        settings.SkipCount = document.Get("Skip", "Count") ?? string.Empty;

        settings.CloseToTray = document.GetBoolOrNull("General", "CloseToTray") ?? true;
        settings.Beep = document.GetBoolOrNull("General", "Beep") ?? true;
        settings.TopMost = document.GetBoolOrNull("General", "TopMost") ?? false;
        settings.AutoCheckUpdate = document.GetBoolOrNull("General", "AutoCheckUpdate") ?? true;

        return settings;
    }

    /// <summary>保存完整配置。</summary>
    public bool SaveConfig(AppSettings settings, out string error)
    {
        try
        {
            IniDocument document = LoadForUpdate();

            int count = Math.Max(AppDefaults.MinSlots, settings.Slots.Count);
            var all = new List<SlotState>(settings.Slots);
            while (all.Count < count) all.Add(AppDefaults.CreateBlankSlot(all.Count + 1));

            for (int index = 1; index <= count; index++)
            {
                SlotState slot = all[index - 1];
                string section = "Slot" + index.ToString(CultureInfo.InvariantCulture);
                document.Set(section, "Enabled", slot.Enabled ? "1" : "0");
                document.Set(section, "Key", slot.Key ?? string.Empty);
                document.Set(section, "Interval", FormatDouble(slot.Interval));
                document.Set(section, "MaxCount", slot.MaxCount.ToString(CultureInfo.InvariantCulture));
                document.Set(section, "Done", slot.Done.ToString(CultureInfo.InvariantCulture));
            }

            // 删行后清掉多余的 SlotN，否则下次读取又会把删掉的行变回来
            for (int stale = count + 1; stale <= MaxSlotNumber(document); stale++)
            {
                document.RemoveSection("Slot" + stale.ToString(CultureInfo.InvariantCulture));
            }

            document.Set("Hotkey", "On", settings.HotkeyOn ?? string.Empty);
            document.Set("Hotkey", "Off", settings.HotkeyOff ?? string.Empty);
            document.Set("Hotkey", "Reset", settings.HotkeyReset ?? string.Empty);
            document.Set("Skip", "Key", settings.SkipKey ?? string.Empty);
            document.Set("Skip", "Count", settings.SkipCount ?? string.Empty);

            WriteGeneral(document, settings);

            document.Save(Path);
            error = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>只保存界面选项（语言 / 关闭到托盘 / 提示音 / 置顶），不碰其它配置与进度。</summary>
    public bool SaveOptions(AppSettings settings, out string error)
    {
        try
        {
            IniDocument document = LoadForUpdate();
            WriteGeneral(document, settings);
            document.Save(Path);
            error = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static void WriteGeneral(IniDocument document, AppSettings settings)
    {
        document.Set("General", "CloseToTray", settings.CloseToTray ? "1" : "0");
        document.Set("General", "Beep", settings.Beep ? "1" : "0");
        document.Set("General", "TopMost", settings.TopMost ? "1" : "0");
        document.Set("General", "AutoCheckUpdate", settings.AutoCheckUpdate ? "1" : "0");
    }

    /// <summary>保存累计进度（只写各行的 Done，不碰其它配置）。</summary>
    public bool SaveProgress(IReadOnlyList<SlotState> slots, out string error)
    {
        try
        {
            IniDocument document = LoadForUpdate();

            // 按列表位置写，而不是 slot.Index：删行后行号会整体上移，
            // 用旧 Index 会把进度写回已经不存在的节。
            for (int i = 0; i < slots.Count; i++)
            {
                string section = "Slot" + (i + 1).ToString(CultureInfo.InvariantCulture);
                document.Set(section, "Done", Math.Max(0, slots[i].Done).ToString(CultureInfo.InvariantCulture));
            }

            document.Save(Path);
            error = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private IniDocument LoadForUpdate()
    {
        if (!File.Exists(Path)) return new IniDocument();

        try
        {
            return IniDocument.Load(Path);
        }
        catch
        {
            // 读取失败（文件损坏 / 编码异常）：先备份原文件再从空文档继续。
            // 否则下一次保存——哪怕只是切个开关——会用空配置直接覆盖整个 ini，
            // 全部槽位、热键和未知键一起丢掉。
            TryBackupUnreadableFile();
            return new IniDocument();
        }
    }

    /// <summary>把无法读取的配置文件备份成 NineFlow.ini.corrupt-时间戳，失败不阻塞保存。</summary>
    private void TryBackupUnreadableFile()
    {
        try
        {
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            File.Copy(Path, Path + ".corrupt-" + stamp, overwrite: true);
        }
        catch
        {
            // 备份失败也不阻塞保存：至少进度还能往下写
        }
    }

    /// <summary>配置文件里最大的 SlotN 编号，用来清理删掉的行留下的旧节。</summary>
    private static int MaxSlotNumber(IniDocument document)
    {
        int max = 0;
        foreach (IniSection section in document.Sections)
        {
            if (!section.Name.StartsWith("Slot", StringComparison.OrdinalIgnoreCase)) continue;
            if (!int.TryParse(section.Name.AsSpan(4), out int number) || number <= 0) continue;
            if (number > max) max = number;
        }

        return max;
    }

    private static string? NonEmpty(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static string FormatDouble(double value) =>
        value.ToString("0.####", CultureInfo.InvariantCulture);

    private static bool TryParseDouble(string text, out double value)
    {
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) return true;
        return double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
    }
}

internal static class IniDocumentExtensions
{
    /// <summary>读取可空布尔值（键不存在时返回 null）。</summary>
    public static bool? GetBoolOrNull(this IniDocument document, string section, string key)
    {
        string? text = document.Get(section, key);
        if (text == null) return null;

        text = text.Trim();
        return text.Equals("1", StringComparison.OrdinalIgnoreCase)
            || text.Equals("true", StringComparison.OrdinalIgnoreCase)
            || text.Equals("yes", StringComparison.OrdinalIgnoreCase)
            || text.Equals("on", StringComparison.OrdinalIgnoreCase);
    }
}

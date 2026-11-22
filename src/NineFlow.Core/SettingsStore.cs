using System.Globalization;

namespace NineFlow.Core;

/// <summary>应用设置（界面 + 累计进度）。</summary>
public sealed class AppSettings
{
    public List<SlotState> Slots { get; set; } = AppDefaults.CreateSlots();

    public string HotkeyOn { get; set; } = AppDefaults.HotkeyOn;

    public string HotkeyOff { get; set; } = AppDefaults.HotkeyOff;

    public string SkipKey { get; set; } = AppDefaults.SkipKey;

    /// <summary>跳过数，空 = 自动取累计次数。</summary>
    public string SkipCount { get; set; } = string.Empty;

    /// <summary>上一次实际跳过的项数（仅记录，不入界面）。</summary>
    public int LastSkip { get; set; }


    /// <summary>点关闭按钮时隐藏到托盘而不是退出。</summary>
    public bool CloseToTray { get; set; } = true;

    /// <summary>开始 / 停止时播放提示音。</summary>
    public bool Beep { get; set; } = true;

    /// <summary>主窗口置顶。</summary>
    public bool TopMost { get; set; }

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

        for (int index = 1; index <= AppDefaults.SlotCount; index++)
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
        settings.SkipKey = document.Get("Skip", "Key") ?? AppDefaults.SkipKey;
        settings.SkipCount = document.Get("Skip", "Count") ?? string.Empty;

        settings.CloseToTray = document.GetBoolOrNull("General", "CloseToTray") ?? true;
        settings.Beep = document.GetBoolOrNull("General", "Beep") ?? true;
        settings.TopMost = document.GetBoolOrNull("General", "TopMost") ?? false;

        string? lastSkip = document.Get("Progress", "LastSkip");
        if (!string.IsNullOrWhiteSpace(lastSkip) &&
            int.TryParse(lastSkip, NumberStyles.Integer, CultureInfo.InvariantCulture, out int lastSkipValue))
        {
            settings.LastSkip = Math.Max(0, lastSkipValue);
        }

        return settings;
    }

    /// <summary>保存完整配置。</summary>
    public bool SaveConfig(AppSettings settings, out string error)
    {
        try
        {
            IniDocument document = LoadForUpdate();

            for (int index = 1; index <= AppDefaults.SlotCount; index++)
            {
                SlotState slot = index <= settings.Slots.Count
                    ? settings.Slots[index - 1]
                    : AppDefaults.CreateSlots()[index - 1];

                string section = "Slot" + index.ToString(CultureInfo.InvariantCulture);
                document.Set(section, "Enabled", slot.Enabled ? "1" : "0");
                document.Set(section, "Key", slot.Key ?? string.Empty);
                document.Set(section, "Interval", FormatDouble(slot.Interval));
                document.Set(section, "MaxCount", slot.MaxCount.ToString(CultureInfo.InvariantCulture));
                document.Set(section, "Done", slot.Done.ToString(CultureInfo.InvariantCulture));
            }

            document.Set("Hotkey", "On", settings.HotkeyOn ?? string.Empty);
            document.Set("Hotkey", "Off", settings.HotkeyOff ?? string.Empty);
            document.Set("Skip", "Key", settings.SkipKey ?? string.Empty);
            document.Set("Skip", "Count", settings.SkipCount ?? string.Empty);
            document.Set("Progress", "LastSkip", settings.LastSkip.ToString(CultureInfo.InvariantCulture));
            document.Set("Progress", "SkipKey", settings.SkipKey ?? string.Empty);

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
    }

    /// <summary>保存累计进度（只写 Done 与 Progress 节）。</summary>
    public bool SaveProgress(IReadOnlyList<SlotState> slots, int lastSkip, string skipKey, out string error)
    {
        try
        {
            IniDocument document = LoadForUpdate();

            foreach (SlotState slot in slots)
            {
                string section = "Slot" + slot.Index.ToString(CultureInfo.InvariantCulture);
                document.Set(section, "Done", Math.Max(0, slot.Done).ToString(CultureInfo.InvariantCulture));
            }

            document.Set("Progress", "LastSkip", Math.Max(0, lastSkip).ToString(CultureInfo.InvariantCulture));
            document.Set("Progress", "SkipKey", skipKey ?? string.Empty);

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
            return new IniDocument();
        }
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

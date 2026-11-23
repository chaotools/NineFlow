using System.Globalization;
using System.Text;
using NineFlow.Core;

namespace NineFlow.Tests;

/// <summary>极简测试宿主：不依赖任何测试框架，直接 dotnet run 即可。</summary>
internal static class Program
{
    private static int _passed;
    private static readonly List<string> Failures = new();

    [STAThread]
    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.WriteLine("== 九序 NineFlow 核心逻辑测试 ==");

        // CI 环境没有真实桌面/前台窗口，可跳过需要真实按键投递的集成测试
        bool skipIntegration = args.Any(arg =>
            string.Equals(arg, "--skip-integration", StringComparison.OrdinalIgnoreCase));

        RunGroup("IniDocument", IniDocumentTests.Run);
        RunGroup("SettingsStore", SettingsStoreTests.Run);
        RunGroup("KeyNames", KeyNamesTests.Run);
        RunGroup("SendSpec", SendSpecTests.Run);
        RunGroup("HotkeyDef", HotkeyDefTests.Run);
        RunGroup("UiText", UiTextTests.Run);
        RunGroup("AppSettingsOptions", AppSettingsOptionsTests.Run);
        RunGroup("ClickSession", ClickSessionTests.Run);
        RunGroup("ClickEngine", ClickEngineTests.Run);
        RunGroup("StartPlanner", StartPlannerTests.Run);
        RunGroup("RunCoordinator", RunCoordinatorTests.Run);
        RunGroup("SlotInput", SlotInputTests.Run);
        RunGroup("KeyCapture", KeyCaptureTests.Run);
        RunGroup("KeyConflict", KeyConflictTests.Run);
        RunGroup("Update", UpdateTests.Run);

        if (skipIntegration)
        {
            Console.WriteLine();
            Console.WriteLine("-- Integration --");
            Console.WriteLine("  (已跳过：需要真实桌面与按键投递)");
        }
        else
        {
            RunGroup("Integration", IntegrationTests.Run);
        }

        Console.WriteLine();
        Console.WriteLine($"通过 {_passed} 项，失败 {Failures.Count} 项。");

        if (Failures.Count == 0) return 0;

        Console.WriteLine();
        foreach (string failure in Failures) Console.WriteLine("  失败: " + failure);
        return 1;
    }

    private static void RunGroup(string name, Action action)
    {
        Console.WriteLine();
        Console.WriteLine("-- " + name + " --");
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Failures.Add($"{name} 抛出异常: {ex.Message}");
            Console.WriteLine("  异常: " + ex);
        }
    }

    public static void Check(string name, bool condition, string? detail = null)
    {
        if (condition)
        {
            _passed++;
            Console.WriteLine("  ok   " + name);
            return;
        }

        string message = detail == null ? name : $"{name}（{detail}）";
        Failures.Add(message);
        Console.WriteLine("  FAIL " + message);
    }

    public static void Equal<T>(string name, T expected, T actual)
    {
        bool same = EqualityComparer<T>.Default.Equals(expected, actual);
        Check(name, same, $"期望 {Format(expected)}，实际 {Format(actual)}");
    }

    private static string Format<T>(T value) => value switch
    {
        null => "null",
        string text => "\"" + text + "\"",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "null",
    };
}

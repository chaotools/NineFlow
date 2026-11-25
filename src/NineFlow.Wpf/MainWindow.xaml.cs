using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using NineFlow.Core;
using WinForms = System.Windows.Forms;

namespace NineFlow.Wpf;

/// <summary>
/// WPF 版主窗口。行为与 WinForms 版一致：同样的 Core 调用顺序、同样的状态文案，
/// 只是把「手算坐标 + 自绘控件」换成「Grid 行列 + 数据绑定」。
/// </summary>
public partial class MainWindow : Window
{
    private const int HotkeyStartId = 0x4E46;
    private const int HotkeyStopId = 0x4E47;
    private const int HotkeyResetId = 0x4E48;

    private readonly SettingsStore _store;
    private readonly ClickEngine _engine;
    private readonly KeyCapture _keyCapture;
    private readonly EventWaitHandle? _showEvent;
    private readonly RunCoordinator _runCoordinator;
    private readonly int _processId = Environment.ProcessId;

    private AppSettings _settings;
    private HotkeyRegistry? _hotkeys;
    private WinForms.NotifyIcon? _tray;
    private DispatcherTimer? _targetTimer;
    private IntPtr _windowHandle;
    private IntPtr _targetWindow;
    private string _targetTitle = string.Empty;

    private volatile bool _closing;
    private bool _exiting;
    private bool _rebuilding;
    private bool _suppressOption;
    private bool _updateBusy;
    private bool _balloonShown;
    private bool _hotkeyProblemShown;
    private int _captureIndex = -1;
    private string _captureBackup = string.Empty;
    private int _statusQueued;

    public ObservableCollection<SlotRowVm> Rows { get; } = new();

    public MainWindow(EventWaitHandle? showEvent)
    {
        _showEvent = showEvent;
        InitializeComponent();
        DataContext = this;

        // 列表出现滚动条时，列头要让出同样宽度，否则列会和行错开
        RowsScroll.ScrollChanged += (_, _) => SyncListGutter();
        SizeChanged += (_, _) => SyncListGutter();

        _store = new SettingsStore(SettingsStore.DefaultIniPath(AppContext.BaseDirectory));
        _settings = _store.Load();
        if (!_store.Exists) _store.SaveConfig(_settings, out _);

        // 首次运行静默创建桌面快捷方式；成功才写标记，失败的话下次启动会再试一次
        bool shortcutCreated = false;
        if (!_settings.ShortcutCreated && Environment.ProcessPath is { Length: > 0 } exePath)
        {
            shortcutCreated = DesktopShortcut.TryCreate(exePath);
            if (shortcutCreated)
            {
                _settings.ShortcutCreated = true;
                _store.SaveConfig(_settings, out _);
            }
        }

        // Windows 关机 / 注销时直接退出并落盘，而不是藏进托盘被系统强杀
        // 全限定：UseWPF + UseWindowsForms 双开时，裸 Application 与 WinForms 歧义（CS0104）
        System.Windows.Application.Current.SessionEnding += (_, _) => _exiting = true;

        _runCoordinator = new RunCoordinator(PersistProgress);
        _engine = new ClickEngine(new KeySender()) { PrepareSendTarget = RestoreTargetFocus };
        _engine.ProgressChanged += HandleEngineProgressChanged;
        _engine.Stopped += HandleEngineStopped;

        _keyCapture = new KeyCapture();
        _keyCapture.Captured += HandleKeyCaptured;
        _keyCapture.Cancelled += HandleKeyCaptureCancelled;

        BtnStart.Click += (_, _) =>
        {
            if (_engine.IsRunning) StopClicking();
            else StartClicking();
        };
        BtnReset.Click += (_, _) => ResetProgress();

        SetupTray();
        ApplySettings(_settings);
        SetRunningState(false);
        SetupTargetTracking();
        RenderProgress(_settings.Slots);
        RefreshSummary();
        Topmost = _settings.TopMost;

        Loaded += (_, _) => OnLoadedForUpdates();

        if (_settings.Notes.Count > 0)
        {
            SetStatus("note.read", "OrangeBrush", UnwrapNote(_settings.Notes[0]));
        }
        else if (shortcutCreated)
        {
            SetStatus("status.shortcutCreated", "GreenBrush");
        }
        else
        {
            SetIdleStatus("TextPrimaryBrush");
        }
    }

    // ---------------------------------------------------------------- 窗口生命周期

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        _windowHandle = new WindowInteropHelper(this).Handle;
        _hotkeys = new HotkeyRegistry(_windowHandle);

        if (HwndSource.FromHwnd(_windowHandle) is { } source)
        {
            source.AddHook(HwndHook);
        }

        ShowHotkeyProblem(RegisterHotkeys(), "status.hotkeyProblem");
        StartShowListener();
    }

    private IntPtr HwndHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (_hotkeys != null && _hotkeys.TryHandleMessage(message, wParam))
        {
            handled = true;
        }

        return IntPtr.Zero;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel) return;

        // 关闭按钮 = 收进托盘：与 WinForms 版一致，只有托盘菜单的「退出」才真正结束
        if (!_exiting && _settings.CloseToTray)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }

        _closing = true;

        _targetTimer?.Stop();
        _targetTimer = null;

        EngineStopResult? result = _engine.Stop();

        // 退出前把界面改动一并落盘（用户可能改了按键却没点保存）
        SyncSettingsFromUi();
        PersistProgress(result?.Slots ?? _settings.Slots);
        _store.SaveConfig(_settings, out _);

        _hotkeys?.Dispose();
        _hotkeys = null;

        if (HwndSource.FromHwnd(_windowHandle) is { } source)
        {
            source.RemoveHook(HwndHook);
        }

        _keyCapture.Dispose();
        _engine.Dispose();

        if (_tray != null)
        {
            _tray.Visible = false;
            _tray.Dispose();
            _tray = null;
        }
    }

    private void StartShowListener()
    {
        if (_showEvent == null) return;

        var thread = new Thread(() =>
        {
            while (!_closing)
            {
                try
                {
                    if (!_showEvent.WaitOne(300)) continue;
                }
                catch
                {
                    return;
                }

                try
                {
                    Dispatcher.BeginInvoke(new Action(ShowFromTray));
                }
                catch (InvalidOperationException)
                {
                    return;
                }
            }
        })
        {
            IsBackground = true,
            Name = "NineFlow.Wpf.ShowListener",
        };

        thread.Start();
    }

    // ---------------------------------------------------------------- 托盘

    private void SetupTray()
    {
        _tray = new WinForms.NotifyIcon
        {
            Icon = Assets.AppIcon ?? System.Drawing.SystemIcons.Application,
            Text = UiText.T("tray.tooltip"),
            Visible = true,
            ContextMenuStrip = new WinForms.ContextMenuStrip(),
        };

        _tray.DoubleClick += (_, _) => Dispatcher.BeginInvoke(new Action(ShowFromTray));
        BuildTrayMenu();
    }

    private void BuildTrayMenu()
    {
        if (_tray?.ContextMenuStrip is not { } menu) return;

        menu.Items.Clear();

        WinForms.ToolStripMenuItem Add(string text, Action action)
        {
            var item = new WinForms.ToolStripMenuItem(text);
            item.Click += (_, _) => Dispatcher.BeginInvoke(action);
            menu.Items.Add(item);
            return item;
        }

        Add(UiText.T("tray.show"), ShowFromTray);
        Add(UiText.T("tray.start"), StartClicking);
        Add(UiText.T("tray.stop"), StopClicking);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        Add(UiText.T("tray.exit"), ExitApplication);
    }

    private void UpdateTrayTexts()
    {
        if (_tray == null) return;

        _tray.Text = _engine.IsRunning ? UiText.T("tray.tooltipRunning") : UiText.T("tray.tooltip");
        BuildTrayMenu();
    }

    private void HideToTray()
    {
        // 录键的钩子会吞掉全部键盘输入，窗口一藏就看不见提示，先收掉再隐藏
        CancelKeyCapture(restoreValue: true);

        Hide();

        if (!_balloonShown && _tray != null)
        {
            _balloonShown = true;
            try
            {
                _tray.ShowBalloonTip(
                    4000,
                    UiText.T("tray.balloon.title"),
                    UiText.T("tray.balloon.text", HotkeyOnText, HotkeyOffText),
                    WinForms.ToolTipIcon.Info);
            }
            catch
            {
                // 托盘气泡不可用时忽略
            }
        }
    }

    private void ShowFromTray()
    {
        if (_closing) return;

        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;

        // 从后台唤到前台：临时置顶，Activate 后再恢复——先恢复再激活会被前台锁拦下。
        // 不要动 ShowInTaskbar：WPF 切它会把 Win32 窗口句柄整个重建，
        // 全局热键和 HwndSource 消息钩子都会失效（Hide() 本身就不占任务栏）
        bool previousTopMost = Topmost;
        Topmost = true;
        Activate();
        if (!_settings.TopMost) Topmost = previousTopMost;

        RefreshSummary();
    }

    private void ExitApplication()
    {
        _exiting = true;
        Close();
    }

    // ---------------------------------------------------------------- 设置读写

    private string HotkeyOnText => NonEmptyOr(TxtHotkeyOn.Text, AppDefaults.HotkeyOn);

    private string HotkeyOffText => NonEmptyOr(TxtHotkeyOff.Text, AppDefaults.HotkeyOff);

    private string HotkeyResetText => NonEmptyOr(TxtHotkeyReset.Text, AppDefaults.HotkeyReset);

    private static string NonEmptyOr(string? text, string fallback) =>
        string.IsNullOrWhiteSpace(text) ? fallback : text.Trim();

    private void ApplySettings(AppSettings settings)
    {
        while (settings.Slots.Count < AppDefaults.MinSlots)
        {
            settings.Slots.Add(AppDefaults.CreateBlankSlot(settings.Slots.Count + 1));
        }

        RebuildRows(settings.Slots);

        TxtHotkeyOn.Text = settings.HotkeyOn;
        TxtHotkeyOff.Text = settings.HotkeyOff;
        TxtHotkeyReset.Text = settings.HotkeyReset;
        TxtSkipKey.Text = settings.SkipKey;
        TxtSkipCount.Text = settings.SkipCount ?? string.Empty;

        _suppressOption = true;
        MiBeep.IsChecked = settings.Beep;
        MiCloseToTray.IsChecked = settings.CloseToTray;
        MiTopMost.IsChecked = settings.TopMost;
        MiAutoUpdate.IsChecked = settings.AutoCheckUpdate;
        _suppressOption = false;

        UpdateAutoSkipLabel();
        RefreshSummary();
    }

    private void RebuildRows(IReadOnlyList<SlotState> slots)
    {
        CancelKeyCapture(restoreValue: true);

        _rebuilding = true;

        foreach (SlotRowVm old in Rows) old.PropertyChanged -= OnRowChanged;
        Rows.Clear();

        for (int i = 0; i < slots.Count; i++)
        {
            SlotState slot = slots[i];
            var vm = new SlotRowVm
            {
                Index = i + 1,
                Enabled = slot.Enabled,
                Key = slot.Key,
                IntervalText = FormatInterval(slot.Interval),
                CountText = slot.MaxCount.ToString(CultureInfo.InvariantCulture),
                ProgressText = string.Empty,
            };
            vm.PropertyChanged += OnRowChanged;
            Rows.Add(vm);
        }

        _rebuilding = false;

        UpdateAddRemoveButtons();
        RefreshSummary();
        UpdateAutoSkipLabel();
    }

    private void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_rebuilding) return;

        RefreshSummary();
        UpdateAutoSkipLabel();
    }

    private List<SlotState> ReadSlotsFromUi(List<string> errors, bool validate)
    {
        var inputs = new List<SlotInput>(Rows.Count);

        for (int i = 0; i < Rows.Count; i++)
        {
            SlotRowVm row = Rows[i];
            SlotState previous = i < _settings.Slots.Count
                ? _settings.Slots[i]
                : new SlotState { Index = i + 1 };

            inputs.Add(new SlotInput(
                i + 1,
                row.Enabled,
                row.Key,
                row.IntervalText,
                row.CountText,
                previous.Done,
                previous.RunCount));
        }

        var problems = new List<UiProblem>();
        List<SlotState> slots = SlotInputParser.ParseAll(inputs, validate, problems);

        foreach (UiProblem problem in problems) errors.Add(problem.Text);
        return slots;
    }

    private List<SlotState> ReadSlotsFromUi() => ReadSlotsFromUi(new List<string>(), validate: false);

    private void SyncSettingsFromUi()
    {
        _settings.Slots = ReadSlotsFromUi();
        _settings.HotkeyOn = TxtHotkeyOn.Text.Trim();
        _settings.HotkeyOff = TxtHotkeyOff.Text.Trim();
        _settings.HotkeyReset = TxtHotkeyReset.Text.Trim();
        _settings.SkipKey = TxtSkipKey.Text.Trim();
        _settings.SkipCount = TxtSkipCount.Text.Trim();
        _settings.Beep = MiBeep.IsChecked;
        _settings.CloseToTray = MiCloseToTray.IsChecked;
        _settings.TopMost = MiTopMost.IsChecked;
        _settings.AutoCheckUpdate = MiAutoUpdate.IsChecked;
    }

    private void SaveOptionsQuietly() => _store.SaveOptions(_settings, out _);

    // ---------------------------------------------------------------- 按钮动作

    private void SaveConfig_Click(object sender, RoutedEventArgs e) => SaveConfig();

    private void SaveConfig()
    {
        CancelKeyCapture(restoreValue: true);

        var errors = new List<string>();
        ReadSlotsFromUi(errors, validate: true);
        if (ReportValidationErrors(errors)) return;

        SyncSettingsFromUi();

        if (!_store.SaveConfig(_settings, out string error))
        {
            SetStatus("status.saveFailed", "RedBrush", error);
            return;
        }

        List<string> hotkeyProblems = RegisterHotkeys();
        if (hotkeyProblems.Count > 0) ShowHotkeyProblem(hotkeyProblems, "status.savedHotkeyProblem");
        else SetStatus("status.saved", "GreenBrush", _store.Path);

        RenderProgress(_settings.Slots);
        RefreshSummary();
        UpdateAutoSkipLabel();
    }

    private void LoadConfig_Click(object sender, RoutedEventArgs e) => LoadConfig();

    private void LoadConfig()
    {
        CancelKeyCapture(restoreValue: true);

        if (!_store.Exists)
        {
            SetStatus("status.noConfig", "OrangeBrush");
            Beep(beepOn: true);
            return;
        }

        _settings = _store.Load();
        ApplySettings(_settings);
        List<string> hotkeyProblems = RegisterHotkeys();
        RenderProgress(_settings.Slots);
        Topmost = _settings.TopMost;

        if (hotkeyProblems.Count > 0) ShowHotkeyProblem(hotkeyProblems, "status.loadedHotkeyProblem");
        else if (_settings.Notes.Count > 0) SetStatus("note.read", "OrangeBrush", UnwrapNote(_settings.Notes[0]));
        else SetStatus("status.loaded", "GreenBrush");
    }

    private void BtnMenu_Click(object sender, RoutedEventArgs e)
    {
        CancelKeyCapture(restoreValue: true);
        ActionMenu.PlacementTarget = sender as FrameworkElement ?? BtnMenu;
        ActionMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        ActionMenu.HorizontalOffset = 0;
        ActionMenu.VerticalOffset = 6;
        ActionMenu.IsOpen = true;
    }

    private void Option_Click(object sender, RoutedEventArgs e)
    {
        if (_suppressOption) return;

        _settings.Beep = MiBeep.IsChecked;
        _settings.CloseToTray = MiCloseToTray.IsChecked;
        _settings.TopMost = MiTopMost.IsChecked;
        _settings.AutoCheckUpdate = MiAutoUpdate.IsChecked;
        Topmost = _settings.TopMost;
        SaveOptionsQuietly();

        var item = sender as MenuItem;
        string name = item?.Header?.ToString() ?? string.Empty;
        bool on = item?.IsChecked ?? false;
        SetStatus(on ? "status.optionOn" : "status.optionOff", on ? "GreenBrush" : "TextSecondaryBrush", name);
    }

    private void AddSlot_Click(object sender, RoutedEventArgs e)
    {
        if (_closing || _engine.IsRunning || Rows.Count >= AppDefaults.MaxSlots) return;

        SyncSettingsFromUi();
        _settings.Slots.Add(AppDefaults.CreateBlankSlot(_settings.Slots.Count + 1));
        RebuildRows(_settings.Slots);
        SetStatus("status.slotAdded", "GreenBrush", Rows.Count);
    }

    private void RemoveRow_Click(object sender, RoutedEventArgs e)
    {
        if (_closing || _engine.IsRunning || Rows.Count <= AppDefaults.MinSlots) return;

        if ((sender as FrameworkElement)?.DataContext is not SlotRowVm row) return;

        int index = Rows.IndexOf(row);
        if (index < 0) return;

        SyncSettingsFromUi();
        SlotState slot = _settings.Slots[index];

        if (slot.Done > 0 &&
            System.Windows.MessageBox.Show(
                this,
                UiText.T("dialog.removeSlot.text", slot.Done),
                UiText.T("dialog.removeSlot.title"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        _settings.Slots.RemoveAt(index);
        RebuildRows(_settings.Slots);
        SetStatus("status.slotRemoved", "GreenBrush", Rows.Count);
    }

    private void StartClicking()
    {
        if (_closing || _engine.IsRunning) return;

        CancelKeyCapture(restoreValue: true);

        var errors = new List<string>();
        List<SlotState> slots = ReadSlotsFromUi(errors, validate: true);
        if (ReportValidationErrors(errors)) return;

        IntPtr sendTarget = WindowsDesktop.ResolveSendTarget(_processId, _windowHandle, _targetWindow);
        bool targetUsable = sendTarget != IntPtr.Zero && !WindowsDesktop.IsMinimized(sendTarget);

        var request = new StartRequest(
            slots,
            StartHotkey: HotkeyOnText,
            StopHotkey: HotkeyOffText,
            SkipKey: TxtSkipKey.Text,
            SkipCount: TxtSkipCount.Text,
            TargetUsable: targetUsable,
            ResetHotkey: HotkeyResetText);

        StartPlan? plan = StartPlanner.TryCreate(request, out UiProblem? problem);
        if (plan == null)
        {
            Beep(beepOn: true);
            ShowProblem(problem!);
            return;
        }

        List<string> hotkeyProblems = RegisterHotkeys();

        if (!_engine.Start(plan, out string startError))
        {
            Beep(beepOn: true);
            SetStatus("status.error", "RedBrush", startError);
            return;
        }

        SetRunningState(true);
        PlayStartSound();
        RenderProgress(_engine.Snapshot());

        if (hotkeyProblems.Count > 0)
        {
            SetStatus("status.runningHint", "GreenBrush", HotkeyOffText, string.Join("；", hotkeyProblems));
        }
        else
        {
            SetStatus("status.running", "GreenBrush", HotkeyOffText);
        }

        RefreshSummary();
    }

    private void ShowProblem(UiProblem problem)
    {
        SetStatus(problem.Key, problem.Severity == UiProblemSeverity.Warning ? "OrangeBrush" : "RedBrush", problem.Args);
    }

    private void StopClicking()
    {
        if (_closing) return;
        if (!_engine.IsRunning) return;

        EngineStopResult? result = _engine.Stop();

        if (result == null)
        {
            SetStatus("status.stopping", "OrangeBrush", HotkeyOffText);
            UpdateTrayTexts();
            return;
        }

        SetRunningState(false);

        RunOutcome? outcome = _runCoordinator.Complete(result, HotkeyOnText);
        if (outcome == null) return;

        ApplyRunOutcome(outcome);
    }

    private void ResetProgress()
    {
        if (_closing) return;

        CancelKeyCapture(restoreValue: true);

        if (_engine.IsRunning)
        {
            SetStatus("status.resetWhileRunning", "OrangeBrush", HotkeyOffText);
            return;
        }

        foreach (SlotState slot in _settings.Slots)
        {
            slot.Done = 0;
            slot.RunCount = 0;
        }

        TxtSkipCount.Text = string.Empty;

        string? saveError = PersistProgress(_settings.Slots);
        RenderProgress(_settings.Slots);
        SetRunningState(false);
        SetStatus("status.reset", "GreenBrush");
        if (saveError != null) SetStatus("status.progressSaveFailed", "RedBrush", saveError);
        RefreshSummary();
        UpdateAutoSkipLabel();
    }

    // ---------------------------------------------------------------- 热键

    private List<string> RegisterHotkeys()
    {
        var problems = new List<string>();
        if (_hotkeys == null) return problems;

        RegisterHotkey(HotkeyStartId, TxtHotkeyOn.Text, UiText.T("label.start"), StartClicking, problems);
        RegisterHotkey(HotkeyStopId, TxtHotkeyOff.Text, UiText.T("label.stop"), StopClicking, problems);
        RegisterHotkey(HotkeyResetId, TxtHotkeyReset.Text, UiText.T("label.reset"), ResetProgress, problems);

        return problems;
    }

    private void RegisterHotkey(int id, string text, string label, Action action, List<string> problems)
    {
        if (_hotkeys == null) return;

        if (string.IsNullOrWhiteSpace(text))
        {
            _hotkeys.Unregister(id);
            problems.Add(UiText.T("hotkey.empty", label));
            return;
        }

        if (!HotkeyDef.TryParse(text, out HotkeyDef? definition, out string parseError))
        {
            _hotkeys.Unregister(id);
            problems.Add(UiText.T("hotkey.invalid", label, text, parseError));
            return;
        }

        if (!_hotkeys.TryRegister(id, definition!, action, out HotkeyRegistrationError registrationError, out int win32Error))
        {
            string reason = registrationError == HotkeyRegistrationError.AlreadyInUse
                ? UiText.T("hotkey.taken", definition!.Display)
                : UiText.T("hotkey.failed", definition!.Display, win32Error);
            problems.Add(reason);
        }
    }

    private void ShowHotkeyProblem(IReadOnlyList<string> problems, string statusKey)
    {
        if (problems.Count == 0) return;

        string summary = string.Join("；", problems);
        SetStatus(statusKey, "RedBrush", summary);

        if (_hotkeyProblemShown) return;
        _hotkeyProblemShown = true;

        try
        {
            _tray?.ShowBalloonTip(6000, UiText.T("hotkey.problemTitle"), summary, WinForms.ToolTipIcon.Warning);
        }
        catch
        {
            // 状态栏已经写明原因
        }
    }

    // ---------------------------------------------------------------- 按键捕获

    private void KeyBorder_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not SlotRowVm row) return;
        BeginKeyCapture(Rows.IndexOf(row));
    }

    private void BeginKeyCapture(int index)
    {
        if (_closing || _engine.IsRunning || index < 0 || index >= Rows.Count) return;

        CancelKeyCapture(restoreValue: true);

        if (!_keyCapture.Start())
        {
            SetStatus("status.captureFailed", "OrangeBrush", UiText.T("capture.failed"));
            return;
        }

        _captureIndex = index;
        _captureBackup = Rows[index].Key;

        MiUpdate.IsEnabled = false;

        Rows[index].Key = string.Empty;
        Rows[index].Capturing = true;

        SetStatus("status.capturing", "OrangeBrush");
    }

    private void EndKeyCapture(bool restoreValue)
    {
        if (_captureIndex < 0) return;

        if (_captureIndex >= Rows.Count)
        {
            _captureIndex = -1;
            _captureBackup = string.Empty;
            MiUpdate.IsEnabled = !_engine.IsRunning && !_updateBusy;
            return;
        }

        SlotRowVm row = Rows[_captureIndex];
        row.Capturing = false;
        if (restoreValue) row.Key = _captureBackup;

        _captureIndex = -1;
        _captureBackup = string.Empty;
        MiUpdate.IsEnabled = !_engine.IsRunning && !_updateBusy;
    }

    private void CancelKeyCapture(bool restoreValue)
    {
        _keyCapture.Stop();
        EndKeyCapture(restoreValue);
    }

    private void HandleKeyCaptured(string text)
    {
        if (_closing) return;

        Dispatcher.BeginInvoke(new Action(() =>
        {
            int index = _captureIndex;
            EndKeyCapture(restoreValue: false);

            if (index < 0 || index >= Rows.Count) return;

            Rows[index].Key = text;
            SetStatus("status.keyCaptured", "GreenBrush", text);
        }));
    }

    private void HandleKeyCaptureCancelled(string? reason)
    {
        if (_closing) return;

        Dispatcher.BeginInvoke(new Action(() =>
        {
            EndKeyCapture(restoreValue: true);

            if (reason != null) SetStatus("status.captureFailed", "OrangeBrush", reason);
            else SetIdleStatus("TextPrimaryBrush");
        }));
    }

    // ---------------------------------------------------------------- 引擎回调

    private void HandleEngineProgressChanged()
    {
        if (_closing) return;
        if (Interlocked.Exchange(ref _statusQueued, 1) == 1) return;

        try
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                Interlocked.Exchange(ref _statusQueued, 0);
                if (_closing) return;
                RenderProgress(_engine.Snapshot());
            }));
        }
        catch (InvalidOperationException)
        {
            Interlocked.Exchange(ref _statusQueued, 0);
        }
    }

    private void HandleEngineStopped(EngineStopResult result)
    {
        if (_closing) return;

        try
        {
            Dispatcher.BeginInvoke(new Action(() => HandleEngineStoppedOnUi(result)));
        }
        catch (InvalidOperationException)
        {
            // 窗口已销毁
        }
    }

    private void HandleEngineStoppedOnUi(EngineStopResult result)
    {
        if (_closing) return;

        RunOutcome? outcome = _runCoordinator.Complete(result, HotkeyOnText);
        if (outcome == null) return;

        SetRunningState(false);
        ApplyRunOutcome(outcome);
    }

    private void ApplyRunOutcome(RunOutcome outcome)
    {
        RenderProgress(outcome.Slots);
        RefreshSummary();
        UpdateAutoSkipLabel();
        PlayStopSound();

        string brush = outcome.Severity switch
        {
            RunOutcomeSeverity.Success => "GreenBrush",
            RunOutcomeSeverity.Failure => "RedBrush",
            _ => "TextPrimaryBrush",
        };
        SetStatus(outcome.StatusKey, brush, outcome.StatusArgs);

        if (outcome.ShowErrorDialog)
        {
            System.Windows.MessageBox.Show(
                this,
                outcome.ErrorMessage ?? string.Empty,
                UiText.T("dialog.title"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        UpdateTrayTexts();
    }

    // ---------------------------------------------------------------- 界面状态

    private void SetRunningState(bool running)
    {
        Title = UiText.T("app.name") + " · " + UiText.T(running ? "pill.running" : "pill.stopped");

        // 一个按钮两种身份：停止时是「开始连点」，运行中变成「停止连点」
        BtnStart.IsEnabled = true;
        BtnStart.Content = UiText.T(running ? "button.stop" : "button.start");
        BtnStart.Style = (Style)FindResource(running ? "DangerButton" : "PrimaryButton");
        TxtPill.Text = UiText.T(running ? "pill.running" : "pill.stopped");
        DotState.Fill = (System.Windows.Media.Brush)FindResource(running ? "GreenBrush" : "TextTertiaryBrush");

        MiSave.IsEnabled = !running;
        MiLoad.IsEnabled = !running;
        BtnReset.IsEnabled = !running;
        MiUpdate.IsEnabled = !running && !_updateBusy;

        TxtHotkeyOn.IsEnabled = !running;
        TxtHotkeyOff.IsEnabled = !running;
        TxtHotkeyReset.IsEnabled = !running;
        TxtSkipKey.IsEnabled = !running;
        TxtSkipCount.IsEnabled = !running;

        foreach (SlotRowVm row in Rows)
        {
            row.Editable = !running;
        }

        UpdateAddRemoveButtons();
        UpdateTrayTexts();
    }

    /// <summary>滚动条出现时列头让出同样的宽度，保证列头与行仍然对齐。</summary>
    private void SyncListGutter()
    {
        double gutter = RowsScroll.ComputedVerticalScrollBarVisibility == Visibility.Visible
            ? SystemParameters.VerticalScrollBarWidth
            : 0;
        HeaderRow.Margin = new Thickness(0, 0, gutter, 0);
    }

    private void UpdateAddRemoveButtons()
    {
        bool running = _engine.IsRunning;
        BtnAddSlot.IsEnabled = !running && Rows.Count < AppDefaults.MaxSlots;

        foreach (SlotRowVm row in Rows)
        {
            row.CanRemove = !running && Rows.Count > AppDefaults.MinSlots;
        }
    }

    private void SetStatus(string key, string brushKey, params object[] args)
    {
        string text;
        try
        {
            text = UiText.T(key, args);
        }
        catch (FormatException)
        {
            text = key;
        }

        TxtState.Text = text;
        TxtState.Foreground = (System.Windows.Media.Brush)FindResource(brushKey);
    }

    private void SetIdleStatus(string brushKey) =>
        SetStatus("status.stopped", brushKey, HotkeyOnText, HotkeyOffText, HotkeyResetText);

    private void RenderProgress(IReadOnlyList<SlotState> slots)
    {
        var parts = new List<string>();
        var active = new List<SlotState>();

        foreach (SlotState slot in slots)
        {
            bool isActive = slot.Enabled && !string.IsNullOrWhiteSpace(slot.Key);
            if (isActive) active.Add(slot);

            // 引擎快照只含启用行（StartPlanner 已过滤），按 Index 对位而不是按下标，
            // 否则禁用行排在前面时进度会错行
            int rowIndex = slot.Index - 1;
            if (rowIndex >= 0 && rowIndex < Rows.Count)
            {
                Rows[rowIndex].ProgressText = slot.MaxCount > 0
                    ? slot.RunCount + "/" + slot.MaxCount
                    : (slot.RunCount > 0 ? slot.RunCount.ToString(CultureInfo.InvariantCulture) : "∞");
            }

            if (!isActive) continue;

            parts.Add(slot.MaxCount > 0
                ? UiText.T("progress.itemMax", slot.Key, slot.RunCount, slot.MaxCount, slot.Done, slot.Done + 1)
                : UiText.T("progress.itemInf", slot.Key, slot.RunCount, slot.Done));
        }

        TxtProgress.Text = parts.Count == 0 ? UiText.T("progress.empty") : string.Join("    ", parts);

        ClickSession.ComboCounts combos = ClickSession.CountCombos(active);

        TxtMetricRound.Text = combos.Infinite
            ? combos.Run.ToString(CultureInfo.InvariantCulture) + " / ∞"
            : combos.Run.ToString(CultureInfo.InvariantCulture) + " / " + combos.Max.ToString(CultureInfo.InvariantCulture);
        TxtMetricTotal.Text = combos.Done.ToString(CultureInfo.InvariantCulture);
        TxtMetricNext.Text = (combos.Done + 1).ToString(CultureInfo.InvariantCulture);
        TxtMetricSkip.Text = ClickSession.AutoSkipCount(slots).ToString(CultureInfo.InvariantCulture);
    }

    private void RefreshSummary()
    {
        if (TxtSummary == null || Rows.Count == 0) return;

        List<SlotState> slots = ReadSlotsFromUi();
        List<SlotState> active = slots.Where(slot => slot.IsActive).ToList();

        bool targetReady = _targetWindow != IntPtr.Zero && WindowsDesktop.IsWindow(_targetWindow);
        string targetText = targetReady
            ? UiText.T("summary.target", ShortenTitle(_targetTitle))
            : UiText.T("summary.targetNone");

        TxtTarget.Text = UiText.T("label.target") + "：" +
            (targetReady ? ShortenTitle(_targetTitle) : UiText.T("target.none"));

        if (active.Count == 0)
        {
            var warnings = slots.Where(s => s.Enabled && string.IsNullOrWhiteSpace(s.Key)).Select(s => s.Index).ToList();
            string none = UiText.T("summary.none");
            if (warnings.Count > 0) none += " · " + UiText.T("summary.warnEmptyKey", string.Join(",", warnings));
            TxtSummary.Text = none;
            return;
        }

        string items = string.Join("、", active.Select(slot =>
            UiText.T("summary.item", slot.Key,
                slot.MaxCount > 0 ? slot.MaxCount.ToString(CultureInfo.InvariantCulture) : "∞")));

        string manualSkip = TxtSkipCount.Text.Trim();
        string skipText = manualSkip.Length == 0
            ? UiText.T("summary.skipAuto", ClickSession.AutoSkipCount(slots))
            : UiText.T("summary.skipManual", manualSkip);

        string text = UiText.T("summary.prefix", items) + " · " +
                      UiText.T("summary.rows", active.Count) + " · " + skipText;

        List<int> emptyChecked = slots.Where(s => s.Enabled && string.IsNullOrWhiteSpace(s.Key)).Select(s => s.Index).ToList();
        if (emptyChecked.Count > 0)
        {
            text += " · " + UiText.T("summary.warnEmptyKey", string.Join(",", emptyChecked));
        }

        text += " · " + targetText;
        TxtSummary.Text = text;
    }

    private void RestoreTargetFocus()
    {
        IntPtr foreground = WindowsDesktop.GetForegroundWindow();
        if (!WindowsDesktop.IsOwnProcessWindow(foreground, _processId) && foreground != IntPtr.Zero)
        {
            return; // 已经是别的程序在前台，按键直接发过去
        }

        IntPtr target = _targetWindow;
        if (!WindowsDesktop.IsWindow(target) || WindowsDesktop.IsMinimized(target))
        {
            target = WindowsDesktop.ResolveSendTarget(_processId, _windowHandle, IntPtr.Zero);
        }

        if (target == IntPtr.Zero || target == _windowHandle) return;

        for (int attempt = 0; attempt < 4; attempt++)
        {
            IntPtr current = WindowsDesktop.GetForegroundWindow();
            if (!WindowsDesktop.IsOwnProcessWindow(current, _processId) && current != IntPtr.Zero) return;
            WindowsDesktop.ActivateWindow(target);
            Thread.Sleep(80);
        }
    }

    private void SetupTargetTracking()
    {
        IntPtr foreground = WindowsDesktop.GetForegroundWindow();
        if (foreground != IntPtr.Zero) RememberTarget(foreground);

        _targetTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _targetTimer.Tick += (_, _) =>
        {
            if (_closing) return;
            IntPtr current = WindowsDesktop.GetForegroundWindow();
            if (current != IntPtr.Zero) RememberTarget(current);
        };
        _targetTimer.Start();
    }

    private void RememberTarget(IntPtr window)
    {
        if (window == IntPtr.Zero || window == _windowHandle) return;
        if (!WindowsDesktop.IsWindow(window)) return;
        if (WindowsDesktop.GetWindowProcessId(window) == _processId) return;

        string title = WindowsDesktop.GetWindowTitle(window);
        if (title.Length == 0) title = "(无标题窗口)";
        if (window == _targetWindow && title == _targetTitle) return;

        _targetWindow = window;
        _targetTitle = title;
        RefreshSummary();
    }

    private static string ShortenTitle(string title)
    {
        const int max = 18;
        return title.Length <= max ? title : title[..max] + "…";
    }

    private static string UnwrapNote(string note)
    {
        int separator = note.IndexOf('|');
        return separator >= 0 ? note[(separator + 1)..] : note;
    }

    private void UpdateAutoSkipLabel()
    {
        if (TxtSkipAuto == null) return;

        string manual = TxtSkipCount.Text.Trim();
        TxtSkipAuto.Text = manual.Length > 0
            ? UiText.T("skip.manual", manual)
            : UiText.T("skip.auto", ClickSession.AutoSkipCount(ReadSlotsFromUi()));
    }

    private string? PersistProgress(IReadOnlyList<SlotState> slots)
    {
        foreach (SlotState runtime in slots)
        {
            foreach (SlotState slot in _settings.Slots)
            {
                if (slot.Index != runtime.Index) continue;
                slot.Done = runtime.Done;
                slot.RunCount = runtime.RunCount;
                break;
            }
        }

        if (_store.SaveProgress(_settings.Slots, out string error)) return null;

        return error;
    }

    // ---------------------------------------------------------------- 提示

    private void PlayStartSound() => Beep(beepOn: true);

    private void PlayStopSound() => Beep(beepOn: false);

    private void Beep(bool beepOn)
    {
        if (!_settings.Beep) return;

        try
        {
            if (beepOn) SystemSounds.Asterisk.Play();
            else SystemSounds.Exclamation.Play();
        }
        catch
        {
            // 无声卡时忽略
        }
    }

    private bool ReportValidationErrors(List<string> errors)
    {
        if (errors.Count == 0) return false;

        Beep(beepOn: false);
        SetStatus("status.error", "RedBrush", errors[0]);
        System.Windows.MessageBox.Show(
            this,
            string.Join(Environment.NewLine, errors),
            UiText.T("dialog.settingsIssue"),
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        return true;
    }

    // ---------------------------------------------------------------- 小工具

    private static string FormatInterval(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);
}

using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Media;
using System.Windows.Forms;
using NineFlow.Core;

namespace NineFlow;

/// <summary>
/// 主窗口（苹果风）。
/// 中文界面名称为「九序」，英文界面名称为「NineFlow」。
/// 关闭窗口默认隐藏到托盘继续运行，热键始终可用。
/// </summary>
public sealed class MainForm : Form
{
    private const int SlotCount = AppDefaults.SlotCount;

    private const int DesignWidth = 1000;
    private const int DesignHeight = 790;
    private const int PageMargin = 24;
    private const int CardWidth = DesignWidth - PageMargin * 2;

    private const int Card1Y = 104;
    private const int Card1H = 312;
    private const int Card2Y = 428;
    private const int Card2H = 148;
    private const int ButtonsY = 588;
    private const int ButtonH = 40;
    private const int StatusY = 646;
    private const int StatusH = 126;

    private const int HotkeyStartId = 0x4E46;
    private const int HotkeyStopId = 0x4E47;

    private readonly SettingsStore _store;
    private readonly ClickEngine _engine;
    private readonly KeyCapture _keyCapture;
    private readonly EventWaitHandle? _showEvent;

    private AppSettings _settings;
    private HotkeyRegistry? _hotkeys;
    private NotifyIcon? _tray;
    private ToolTip _toolTip = null!;

    private Panel _root = null!;
    private HeaderPanel _header = null!;
    private CardPanel _cardSlots = null!;
    private CardPanel _cardHotkeys = null!;
    private CardPanel _cardStatus = null!;

    private readonly AppSwitch[] _sw = new AppSwitch[SlotCount];
    private readonly KeyField[] _keyFields = new KeyField[SlotCount];
    private readonly AppTextField[] _intervalFields = new AppTextField[SlotCount];
    private readonly AppTextField[] _countFields = new AppTextField[SlotCount];

    private AppTextField _hotkeyOnField = null!;
    private AppTextField _hotkeyOffField = null!;
    private AppTextField _skipKeyField = null!;
    private AppTextField _skipCountField = null!;
    private Label _lblSkipAuto = null!;

    private AppSwitch _swBeep = null!;
    private AppSwitch _swCloseToTray = null!;
    private AppSwitch _swTopMost = null!;

    private AppButton _btnSave = null!;
    private AppButton _btnLoad = null!;
    private AppButton _btnStart = null!;
    private AppButton _btnStop = null!;
    private AppButton _btnReset = null!;

    private Label _lblState = null!;
    private Label _lblProgress = null!;
    private Label _lblSummary = null!;

    private readonly List<(Control Control, string Key)> _staticTexts = new();
    private readonly List<(Control Control, string Key)> _tooltips = new();

    private string _statusKey = "status.stopped";
    private object[] _statusArgs = Array.Empty<object>();
    private Color _statusColor = Theme.TextPrimary;

    private int _statusQueued;

    /// <summary>已经处理过停止回调的那一轮编号；防止同一轮被 StopClicking 和 Stopped 处理两次。</summary>
    private int _handledStopRun;

    /// <summary>正在录键的行下标；-1 表示没在录。</summary>
    private int _captureIndex = -1;

    /// <summary>开始录键时该行原来的内容，取消时恢复回去。</summary>
    private string _captureBackup = string.Empty;

    private bool _closing;
    private bool _exiting;
    private bool _balloonShown;

    private readonly int _processId = Environment.ProcessId;
    private IntPtr _targetWindow;
    private string _targetTitle = string.Empty;
    private System.Windows.Forms.Timer? _targetTimer;

    public MainForm(EventWaitHandle? showEvent)
    {
        _showEvent = showEvent;

        _store = new SettingsStore(SettingsStore.DefaultIniPath(AppContext.BaseDirectory));

        _settings = _store.Load();

        // 首次运行先落盘默认配置，这样累计进度从一开始就能保存
        if (!_store.Exists)
        {
            _store.SaveConfig(_settings, out _);
        }

        _engine = new ClickEngine(new KeySender())
        {
            PrepareSendTarget = RestoreTargetFocus,
        };
        _engine.ProgressChanged += HandleEngineProgressChanged;
        _engine.Stopped += HandleEngineStopped;

        _keyCapture = new KeyCapture();
        _keyCapture.Captured += HandleKeyCaptured;
        _keyCapture.Cancelled += HandleKeyCaptureCancelled;

        BuildUi();
        ApplySettings(_settings);
        SetupTray();
        SetRunningState(false);
        SetupTargetTracking();
        RenderProgress(_settings.Slots);
        RefreshSummary();
        TopMost = _settings.TopMost;

        if (_settings.Notes.Count > 0)
        {
            SetStatus("note.read", Theme.Orange, UnwrapNote(_settings.Notes[0]));
        }
        else
        {
            SetIdleStatus(Theme.TextPrimary);
        }
    }

    // ---------------------------------------------------------------- 构建界面

    private void BuildUi()
    {
        SuspendLayout();

        Text = UiText.T("app.name");
        Icon = Assets.AppIcon;
        BackColor = Theme.Page;
        Font = Theme.Font(9f);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96f, 96f);
        ClientSize = new Size(DesignWidth, DesignHeight);
        DoubleBuffered = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer, true);

        _toolTip = new ToolTip
        {
            AutoPopDelay = 16000,
            InitialDelay = 420,
            ReshowDelay = 150,
            ShowAlways = true,
        };

        _root = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Page,
        };
        Controls.Add(_root);

        _header = new HeaderPanel
        {
            Location = new Point(0, 0),
            Size = new Size(DesignWidth, 92),
            Logo = Assets.Logo,
        };
        _root.Controls.Add(_header);


        BuildSlotsCard();
        BuildHotkeyCard();
        BuildButtons();
        BuildStatusCard();

        ResumeLayout(true);
    }

    private void BuildSlotsCard()
    {
        _cardSlots = new CardPanel
        {
            Location = new Point(PageMargin, Card1Y),
            Size = new Size(CardWidth, Card1H),
        };
        _root.Controls.Add(_cardSlots);

        AddLabel(_cardSlots, "card.slots.title", new Rectangle(24, 14, 320, 26), Theme.Font(12f, FontStyle.Bold), Theme.TextPrimary);
        AddLabel(_cardSlots, "card.slots.hint", new Rectangle(360, 20, CardWidth - 360 - 24, 20), Theme.Font(8.5f), Theme.TextTertiary, ContentAlignment.MiddleRight);

        for (int column = 0; column < 2; column++)
        {
            int x = 24 + column * 452;
            AddLabel(_cardSlots, "col.enabled", new Rectangle(x, 50, 58, 18), Theme.Font(8.5f), Theme.TextTertiary);
            AddLabel(_cardSlots, "col.key", new Rectangle(x + 58, 50, 172, 18), Theme.Font(8.5f), Theme.TextTertiary);
            AddLabel(_cardSlots, "col.interval", new Rectangle(x + 238, 50, 100, 18), Theme.Font(8.5f), Theme.TextTertiary);
            AddLabel(_cardSlots, "col.count", new Rectangle(x + 346, 50, 60, 18), Theme.Font(8.5f), Theme.TextTertiary);
        }

        for (int i = 0; i < SlotCount; i++)
        {
            int index = i;
            int column = i / 5;
            int row = i % 5;
            int x = 24 + column * 452;
            int y = 76 + row * 46;

            var toggle = new AppSwitch
            {
                Location = new Point(x, y + 3),
                SurfaceColor = Theme.Card,
                Name = $"slot{i + 1}.enabled",
            };
            toggle.CheckedChanged += (_, _) => OnSlotEnabledChanged(index);
            _cardSlots.Controls.Add(toggle);
            _tooltips.Add((toggle, "tooltip.enabled"));

            var keyField = new KeyField
            {
                Location = new Point(x + 58, y),
                Size = new Size(170, 32),
                SurfaceColor = Theme.Card,
                Name = $"slot{i + 1}.keybox",
            };
            keyField.Editor.Name = $"slot{i + 1}.key";
            keyField.Editor.TextChanged += (_, _) => OnSlotFieldChanged();
            keyField.CaptureRequested += (_, _) => BeginKeyCapture(index);
            _cardSlots.Controls.Add(keyField);
            _tooltips.Add((keyField, "tooltip.key"));

            var intervalField = new AppTextField
            {
                Location = new Point(x + 238, y),
                Size = new Size(76, 32),
                SurfaceColor = Theme.Card,
                TextAlign = HorizontalAlignment.Center,
            };
            intervalField.Editor.Name = $"slot{i + 1}.interval";
            intervalField.Editor.TextChanged += (_, _) => OnSlotFieldChanged();
            _cardSlots.Controls.Add(intervalField);
            _tooltips.Add((intervalField, "tooltip.interval"));

            AddLabel(_cardSlots, "unit.second", new Rectangle(x + 318, y, 24, 32), Theme.Font(9f), Theme.TextTertiary, ContentAlignment.MiddleCenter);

            var countField = new AppTextField
            {
                Location = new Point(x + 346, y),
                Size = new Size(60, 32),
                SurfaceColor = Theme.Card,
                TextAlign = HorizontalAlignment.Center,
                Placeholder = UiText.T("field.placeholder.count"),
            };
            countField.Editor.Name = $"slot{i + 1}.max";
            countField.Editor.TextChanged += (_, _) => OnSlotFieldChanged();
            _cardSlots.Controls.Add(countField);
            _tooltips.Add((countField, "tooltip.count"));

            _sw[i] = toggle;
            _keyFields[i] = keyField;
            _intervalFields[i] = intervalField;
            _countFields[i] = countField;
        }
    }

    private void BuildHotkeyCard()
    {
        _cardHotkeys = new CardPanel
        {
            Location = new Point(PageMargin, Card2Y),
            Size = new Size(CardWidth, Card2H),
        };
        _root.Controls.Add(_cardHotkeys);

        AddLabel(_cardHotkeys, "card.hotkeys.title", new Rectangle(24, 14, 320, 26), Theme.Font(12f, FontStyle.Bold), Theme.TextPrimary);

        AddLabel(_cardHotkeys, "label.start", new Rectangle(24, 52, 40, 32), Theme.Font(9f), Theme.TextSecondary);
        _hotkeyOnField = AddField(_cardHotkeys, "hotkey.start", new Rectangle(64, 52, 96, 32), "tooltip.hotkeyStart");

        AddLabel(_cardHotkeys, "label.stop", new Rectangle(176, 52, 40, 32), Theme.Font(9f), Theme.TextSecondary);
        _hotkeyOffField = AddField(_cardHotkeys, "hotkey.stop", new Rectangle(216, 52, 96, 32), "tooltip.hotkeyStop");

        AddLabel(_cardHotkeys, "label.skipkey", new Rectangle(328, 52, 56, 32), Theme.Font(9f), Theme.TextSecondary);
        _skipKeyField = AddField(_cardHotkeys, "hotkey.skipkey", new Rectangle(384, 52, 96, 32), "tooltip.skipKey");

        AddLabel(_cardHotkeys, "label.skipcount", new Rectangle(496, 52, 56, 32), Theme.Font(9f), Theme.TextSecondary);
        _skipCountField = AddField(_cardHotkeys, "hotkey.skipcount", new Rectangle(552, 52, 64, 32), "tooltip.skipCount");
        _skipCountField.Editor.TextChanged += (_, _) =>
        {
            UpdateAutoSkipLabel();
            RefreshSummary();
        };

        _lblSkipAuto = AddLabel(_cardHotkeys, null, new Rectangle(628, 52, CardWidth - 628 - 24, 32), Theme.Font(8.5f), Theme.TextTertiary);

        // 第二行：三个开关
        _swBeep = AddSwitch(_cardHotkeys, "switch.beep", 24, 96, 74, 70, out Label lblBeep, "tooltip.beep");
        _swBeep.CheckedChanged += (_, _) =>
        {
            _settings.Beep = _swBeep.Checked;
            SaveOptionsQuietly();
        };
        lblBeep.Click += (_, _) => _swBeep.Checked = !_swBeep.Checked;

        _swCloseToTray = AddSwitch(_cardHotkeys, "switch.closeToTray", 180, 96, 230, 170, out Label lblTray, "tooltip.closeToTray");
        _swCloseToTray.CheckedChanged += (_, _) =>
        {
            _settings.CloseToTray = _swCloseToTray.Checked;
            SaveOptionsQuietly();
        };
        lblTray.Click += (_, _) => _swCloseToTray.Checked = !_swCloseToTray.Checked;

        _swTopMost = AddSwitch(_cardHotkeys, "switch.topMost", 430, 96, 480, 110, out Label lblTop, "tooltip.topMost");
        _swTopMost.CheckedChanged += (_, _) =>
        {
            _settings.TopMost = _swTopMost.Checked;
            TopMost = _settings.TopMost;
            SaveOptionsQuietly();
        };
        lblTop.Click += (_, _) => _swTopMost.Checked = !_swTopMost.Checked;
    }

    private AppSwitch AddSwitch(Control parent, string labelKey, int x, int y, int labelX, int labelWidth, out Label label, string tooltipKey)
    {
        var toggle = new AppSwitch
        {
            Location = new Point(x, y),
            SurfaceColor = Theme.Card,
            Name = labelKey,
        };
        parent.Controls.Add(toggle);
        _tooltips.Add((toggle, tooltipKey));

        label = AddLabel(parent, labelKey, new Rectangle(labelX, y + 3, labelWidth, 22), Theme.Font(9f), Theme.TextSecondary);
        label.Cursor = Cursors.Hand;

        return toggle;
    }

    private AppTextField AddField(Control parent, string automationId, Rectangle bounds, string tooltipKey)
    {
        var field = new AppTextField
        {
            Location = bounds.Location,
            Size = bounds.Size,
            SurfaceColor = Theme.Card,
        };
        field.Editor.Name = automationId;
        parent.Controls.Add(field);
        _tooltips.Add((field, tooltipKey));
        return field;
    }

    private void BuildButtons()
    {
        _btnSave = AddButton("button.save", PageMargin, ButtonsY, 110, "tooltip.save", AppButtonKind.Secondary);
        _btnLoad = AddButton("button.load", PageMargin + 122, ButtonsY, 110, "tooltip.load", AppButtonKind.Secondary);
        _btnReset = AddButton("button.reset", PageMargin + 244, ButtonsY, 110, "tooltip.reset", AppButtonKind.Secondary);

        _btnStop = AddButton("button.stop", DesignWidth - PageMargin - 132 - 12 - 120, ButtonsY, 120, "tooltip.stop", AppButtonKind.Danger);
        _btnStart = AddButton("button.start", DesignWidth - PageMargin - 132, ButtonsY, 132, "tooltip.start", AppButtonKind.Primary);

        _btnSave.Click += (_, _) => SaveConfig();
        _btnLoad.Click += (_, _) => LoadConfig();
        _btnStart.Click += (_, _) => StartClicking();
        _btnStop.Click += (_, _) => StopClicking();
        _btnReset.Click += (_, _) => ResetProgress();
    }

    private AppButton AddButton(string key, int x, int y, int width, string tooltipKey, AppButtonKind kind)
    {
        var button = new AppButton
        {
            Kind = kind,
            Location = new Point(x, y),
            Size = new Size(width, ButtonH),
            Name = "button." + key.Replace("button.", string.Empty),
        };

        button.Text = UiText.T(key);
        _root.Controls.Add(button);
        _staticTexts.Add((button, key));
        _tooltips.Add((button, tooltipKey));
        _toolTip.SetToolTip(button, UiText.T(tooltipKey));
        return button;
    }

    private void BuildStatusCard()
    {
        _cardStatus = new CardPanel
        {
            Location = new Point(PageMargin, StatusY),
            Size = new Size(CardWidth, StatusH),
        };
        _root.Controls.Add(_cardStatus);

        _lblState = AddLabel(_cardStatus, null, new Rectangle(24, 12, CardWidth - 48, 26), Theme.Font(10.5f, FontStyle.Bold), Theme.TextPrimary);
        _lblState.Name = "status.state";

        _lblProgress = AddLabel(_cardStatus, null, new Rectangle(24, 40, CardWidth - 48, 22), Theme.Font(9f), Theme.TextSecondary);
        _lblProgress.Name = "status.progress";

        _lblSummary = AddLabel(_cardStatus, null, new Rectangle(24, 64, CardWidth - 48, 22), Theme.Font(9f), Theme.Accent);
        _lblSummary.Name = "status.summary";

        AddLabel(_cardStatus, "hint.resume", new Rectangle(24, 90, CardWidth - 48, 20), Theme.Font(8.5f), Theme.TextTertiary);
    }

    private Label AddLabel(Control parent, string? key, Rectangle bounds, Font font, Color color, ContentAlignment alignment = ContentAlignment.MiddleLeft, Color? background = null)
    {
        bool onCard = parent is CardPanel;
        var label = new Label
        {
            AutoSize = false,
            AutoEllipsis = true,
            UseMnemonic = false,
            Bounds = bounds,
            Font = font,
            ForeColor = color,
            BackColor = background ?? (onCard ? Theme.Card : Theme.Page),
            TextAlign = alignment,
        };

        if (key != null)
        {
            label.Text = UiText.T(key);
            _staticTexts.Add((label, key));
        }

        parent.Controls.Add(label);
        return label;
    }

    // ---------------------------------------------------------------- 托盘

    private void SetupTray()
    {
        _tray = new NotifyIcon
        {
            Icon = Assets.AppIcon ?? SystemIcons.Application,
            Text = UiText.T("tray.tooltip"),
            Visible = true,
            ContextMenuStrip = new ContextMenuStrip
            {
                Font = Theme.Font(9f),
                ShowImageMargin = false,
            },
        };

        _tray.DoubleClick += (_, _) => ShowFromTray();
        BuildTrayMenu();
    }

    private void BuildTrayMenu()
    {
        if (_tray?.ContextMenuStrip is not { } menu) return;

        menu.Items.Clear();
        menu.Items.Add(new ToolStripMenuItem(UiText.T("tray.show"), null, (_, _) => ShowFromTray()));
        menu.Items.Add(new ToolStripMenuItem(UiText.T("tray.start"), null, (_, _) => StartClicking()));
        menu.Items.Add(new ToolStripMenuItem(UiText.T("tray.stop"), null, (_, _) => StopClicking()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem(UiText.T("tray.exit"), null, (_, _) => ExitApplication()));
    }

    private void UpdateTrayTexts()
    {
        if (_tray == null) return;
        _tray.Text = _engine.IsRunning ? UiText.T("tray.tooltipRunning") : UiText.T("tray.tooltip");
        BuildTrayMenu();
    }

    private void HideToTray()
    {
        Hide();

        if (!_balloonShown && _tray != null)
        {
            _balloonShown = true;
            try
            {
                _tray.ShowBalloonTip(4000, UiText.T("tray.balloon.title"), UiText.T("tray.balloon.text", HotkeyOnText, HotkeyOffText), ToolTipIcon.Info);
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
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        ShowInTaskbar = true;

        // 从后台唤到前台：先临时置顶一下，绕开系统前台窗口限制
        bool previousTopMost = TopMost;
        TopMost = true;
        TopMost = _settings.TopMost;
        Activate();
        BringToFront();
        if (!_settings.TopMost) TopMost = previousTopMost;

        RefreshSummary();
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
                    BeginInvoke(new Action(ShowFromTray));
                }
                catch
                {
                    return;
                }
            }
        })
        {
            IsBackground = true,
            Name = "NineFlow.ShowListener",
        };

        thread.Start();
    }

    // ---------------------------------------------------------------- 生命周期

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);

        _hotkeys = new HotkeyRegistry(Handle);
        RegisterHotkeys(reportErrors: false);
        StartShowListener();
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        _hotkeys?.Dispose();
        _hotkeys = null;
        base.OnHandleDestroyed(e);
    }

    protected override void WndProc(ref Message m)
    {
        if (_hotkeys != null && _hotkeys.TryHandleMessage(m.Msg, m.WParam)) return;
        base.WndProc(ref m);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        // 小屏幕兜底：窗口比工作区还大时收缩，内容区可滚动，不会出现被裁掉/被遮挡
        Rectangle workArea = Screen.FromControl(this).WorkingArea;
        int maxWidth = Math.Max(600, workArea.Width - 40);
        int maxHeight = Math.Max(400, workArea.Height - 80);
        if (ClientSize.Width > maxWidth || ClientSize.Height > maxHeight)
        {
            ClientSize = new Size(Math.Min(ClientSize.Width, maxWidth), Math.Min(ClientSize.Height, maxHeight));
            _root.AutoScroll = true;
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        if (e.Cancel) return;

        if (e.CloseReason == CloseReason.UserClosing && !_exiting && _settings.CloseToTray)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }

        _closing = true;

        _targetTimer?.Stop();
        _targetTimer?.Dispose();
        _targetTimer = null;

        EngineStopResult? result = _engine.Stop();
        PersistProgress(result?.Slots ?? _settings.Slots);
        SaveOptionsQuietly();

        _hotkeys?.Dispose();
        _hotkeys = null;
        _keyCapture.Dispose();
        _engine.Dispose();

        if (_tray != null)
        {
            _tray.Visible = false;
            _tray.Dispose();
            _tray = null;
        }
    }

    private void ExitApplication()
    {
        _exiting = true;
        if (_engine.IsRunning) _engine.Stop();
        PersistProgress(_settings.Slots);
        Close();
    }

    // ---------------------------------------------------------------- 设置读写

    private string HotkeyOnText => NonEmptyOr(_hotkeyOnField.Value, AppDefaults.HotkeyOn);

    private string HotkeyOffText => NonEmptyOr(_hotkeyOffField.Value, AppDefaults.HotkeyOff);

    private static string NonEmptyOr(string? text, string fallback) =>
        string.IsNullOrWhiteSpace(text) ? fallback : text.Trim();

    private void ApplySettings(AppSettings settings)
    {
        List<SlotState> defaults = AppDefaults.CreateSlots();

        for (int i = 0; i < SlotCount; i++)
        {
            SlotState slot = i < settings.Slots.Count ? settings.Slots[i] : defaults[i];

            _sw[i].Checked = slot.Enabled;
            _keyFields[i].Value = slot.Key ?? string.Empty;
            _intervalFields[i].Value = FormatNumber(slot.Interval);
            _countFields[i].Value = slot.MaxCount.ToString(CultureInfo.InvariantCulture);
            UpdateRowVisuals(i);
        }

        _hotkeyOnField.Value = settings.HotkeyOn;
        _hotkeyOffField.Value = settings.HotkeyOff;
        _skipKeyField.Value = settings.SkipKey;
        _skipCountField.Value = settings.SkipCount ?? string.Empty;

        _swBeep.Checked = settings.Beep;
        _swCloseToTray.Checked = settings.CloseToTray;
        _swTopMost.Checked = settings.TopMost;

        UpdateAutoSkipLabel();
        RefreshSummary();
    }

    private List<SlotState> ReadSlotsFromUi(List<string> errors, bool validate)
    {
        var slots = new List<SlotState>(SlotCount);

        for (int i = 0; i < SlotCount; i++)
        {
            int index = i + 1;
            SlotState previous = i < _settings.Slots.Count ? _settings.Slots[i] : new SlotState { Index = index };

            var slot = new SlotState
            {
                Index = index,
                Enabled = _sw[i].Checked,
                Key = (_keyFields[i].Value ?? string.Empty).Trim(),
                Done = previous.Done,
                RunCount = previous.RunCount,
            };

            string intervalText = (_intervalFields[i].Value ?? string.Empty).Trim();
            if (intervalText.Length == 0)
            {
                slot.Interval = 1.5;
                if (validate && slot.Enabled) errors.Add(UiText.T("row.intervalEmpty", index));
            }
            else if (TryParseNumber(intervalText, out double interval))
            {
                slot.Interval = SlotState.ClampInterval(interval);
            }
            else
            {
                slot.Interval = 1.5;
                if (validate && slot.Enabled) errors.Add(UiText.T("row.intervalInvalid", index, intervalText));
            }

            string countText = (_countFields[i].Value ?? string.Empty).Trim();
            if (countText.Length == 0)
            {
                slot.MaxCount = 0;
            }
            else if (int.TryParse(countText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int maxCount))
            {
                slot.MaxCount = SlotState.ClampCount(maxCount);
            }
            else
            {
                slot.MaxCount = 0;
                if (validate && slot.Enabled) errors.Add(UiText.T("row.countInvalid", index, countText));
            }

            slots.Add(slot);
        }

        return slots;
    }

    private List<SlotState> ReadSlotsFromUi() => ReadSlotsFromUi(new List<string>(), validate: false);

    private void SaveOptionsQuietly() => _store.SaveOptions(_settings, out _);

    // ---------------------------------------------------------------- 按钮动作

    private void SaveConfig()
    {
        var errors = new List<string>();
        List<SlotState> slots = ReadSlotsFromUi(errors, validate: true);
        if (ReportValidationErrors(errors)) return;

        _settings.Slots = slots;
        _settings.HotkeyOn = _hotkeyOnField.Value.Trim();
        _settings.HotkeyOff = _hotkeyOffField.Value.Trim();
        _settings.SkipKey = _skipKeyField.Value.Trim();
        _settings.SkipCount = _skipCountField.Value.Trim();
        _settings.Beep = _swBeep.Checked;
        _settings.CloseToTray = _swCloseToTray.Checked;
        _settings.TopMost = _swTopMost.Checked;

        if (!_store.SaveConfig(_settings, out string error))
        {
            SetStatus("status.saveFailed", Theme.Red, error);
            return;
        }

        RegisterHotkeys(reportErrors: false);
        SetStatus("status.saved", Theme.Green, _store.Path);
        RenderProgress(_settings.Slots);
        RefreshSummary();
        UpdateAutoSkipLabel();
    }

    private void LoadConfig()
    {
        if (!_store.Exists)
        {
            SetStatus("status.noConfig", Theme.Orange, Array.Empty<object>());
            Beep(beepOn: true);
            return;
        }

        _settings = _store.Load();
        ApplySettings(_settings);
        RegisterHotkeys(reportErrors: false);
        RenderProgress(_settings.Slots);
        TopMost = _settings.TopMost;

        if (_settings.Notes.Count > 0) SetStatus("note.read", Theme.Orange, UnwrapNote(_settings.Notes[0]));
        else SetStatus("status.loaded", Theme.Green, Array.Empty<object>());
    }

    private void StartClicking()
    {
        if (_closing) return;
        if (_engine.IsRunning) return;

        // 录键时钩子会吞掉所有键盘输入，连点发出去的键也会被它吃掉，先收掉
        CancelKeyCapture(restoreValue: true);

        // 开始前按当前文本重新注册热键
        List<string> hotkeyProblems = RegisterHotkeys(reportErrors: false);

        var errors = new List<string>();
        List<SlotState> slots = ReadSlotsFromUi(errors, validate: true);
        if (ReportValidationErrors(errors)) return;

        List<SlotState> active = slots.Where(slot => slot.IsActive).ToList();
        if (active.Count == 0)
        {
            Beep(beepOn: true);
            SetStatus("status.noActive", Theme.Orange, Array.Empty<object>());
            return;
        }

        // 先本地校验按键写法，报错信息才能跟随界面语言
        foreach (SlotState slot in active)
        {
            if (!SendSpec.TryParse(slot.Key, out _, out string keyError))
            {
                Beep(beepOn: true);
                SetStatus("status.keyInvalid", Theme.Red, slot.Index, slot.Key, keyError);
                return;
            }
        }

        string skipKey = _skipKeyField.Value.Trim();
        if (skipKey.Length > 0 && !SendSpec.TryParse(skipKey, out _, out string skipError))
        {
            Beep(beepOn: true);
            SetStatus("status.skipInvalid", Theme.Red, skipKey, skipError);
            return;
        }

        string skipCountText = _skipCountField.Value.Trim();
        int skipCount;
        if (skipCountText.Length == 0)
        {
            skipCount = ClickSession.AutoSkipCount(slots);
        }
        else if (int.TryParse(skipCountText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedSkip))
        {
            skipCount = Math.Max(0, parsedSkip);
        }
        else
        {
            Beep(beepOn: true);
            SetStatus("status.badSkipCount", Theme.Orange, skipCountText);
            return;
        }

        // 确认有可用的目标窗口：按键总是发给前台窗口，如果九序自己在前台就没有意义
        IntPtr sendTarget = WindowsDesktop.ResolveSendTarget(_processId, Handle, _targetWindow);
        if (sendTarget == IntPtr.Zero || WindowsDesktop.IsMinimized(sendTarget))
        {
            Beep(beepOn: true);
            SetStatus("status.noTarget", Theme.Orange, HotkeyOnText);
            return;
        }

        if (!_engine.Start(slots, skipKey, skipCount, out string startError))
        {
            Beep(beepOn: true);
            SetStatus("status.error", Theme.Red, startError);
            return;
        }

        SetRunningState(true);
        PlayStartSound();
        RenderProgress(_engine.Snapshot());

        if (hotkeyProblems.Count > 0)
        {
            SetStatus("status.runningHint", Theme.Green, HotkeyOffText, string.Join("；", hotkeyProblems));
        }
        else
        {
            SetStatus("status.running", Theme.Green, HotkeyOffText);
        }

        RefreshSummary();
    }

    private void StopClicking()
    {
        if (_closing) return;

        bool wasRunning = _engine.IsRunning;
        EngineStopResult? result = _engine.Stop();

        // 引擎没能在 3 秒内退出：按键还在发，绝不能把界面切成“已停止”
        if (wasRunning && result == null)
        {
            SetStatus("status.stopping", Theme.Orange, HotkeyOffText);
            UpdateTrayTexts();
            return;
        }

        SetRunningState(false);

        if (result == null)
        {
            SetIdleStatus(Theme.TextPrimary);
            return;
        }

        // 本轮自然结束时 Stopped 回调可能已经先处理过了，这里不重复播提示音 / 重复改状态
        if (result.RunId <= _handledStopRun) return;
        _handledStopRun = result.RunId;

        string? saveError = PersistProgress(result.Slots);
        RenderProgress(result.Slots);
        RefreshSummary();
        PlayStopSound();

        if (result.Reason == StopReason.Completed)
        {
            SetStatus("status.completed", Theme.Green, HotkeyOnText);
        }
        else
        {
            SetStatus("status.stoppedNext", Theme.TextPrimary, HotkeyOnText);
        }

        // 进度没写进磁盘比“已停止”更要紧，放在最后，免得被上面的状态覆盖
        if (saveError != null) SetStatus("status.progressSaveFailed", Theme.Red, saveError);

        UpdateTrayTexts();
    }

    private void ResetProgress()
    {
        if (_closing) return;

        // 运行中按钮本来就是禁用的，正常走不到这里；将来若放开，也绝不能边跑边清零。
        // （旧实现在这里调 Application.DoEvents() 去“处理排队中的回调”，会把停止事件重入派发进来）
        if (_engine.IsRunning)
        {
            SetStatus("status.resetWhileRunning", Theme.Orange, HotkeyOffText);
            return;
        }

        foreach (SlotState slot in _settings.Slots)
        {
            slot.Done = 0;
            slot.RunCount = 0;
        }

        _skipCountField.Value = string.Empty;

        string? saveError = PersistProgress(_settings.Slots);
        RenderProgress(_settings.Slots);
        SetRunningState(false);
        SetStatus("status.reset", Theme.Green, Array.Empty<object>());
        if (saveError != null) SetStatus("status.progressSaveFailed", Theme.Red, saveError);
        RefreshSummary();
        UpdateAutoSkipLabel();
    }

    // ---------------------------------------------------------------- 热键

    private List<string> RegisterHotkeys(bool reportErrors)
    {
        var problems = new List<string>();
        if (_hotkeys == null) return problems;

        RegisterHotkey(HotkeyStartId, _hotkeyOnField.Value, UiText.T("label.start"), StartClicking, problems);
        RegisterHotkey(HotkeyStopId, _hotkeyOffField.Value, UiText.T("label.stop"), StopClicking, problems);

        if (problems.Count > 0 && reportErrors)
        {
            SetStatus("status.error", Theme.Orange, string.Join("；", problems));
        }

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

    // ---------------------------------------------------------------- 按键捕获

    private void BeginKeyCapture(int index)
    {
        if (_closing || _engine.IsRunning) return;

        // 同一时刻只录一个键：先把上一次（如果有）收干净
        CancelKeyCapture(restoreValue: true);

        if (!_keyCapture.Start())
        {
            SetStatus("status.captureFailed", Theme.Orange, UiText.T("capture.failed"));
            return;
        }

        _captureIndex = index;
        _captureBackup = _keyFields[index].Value;

        KeyField field = _keyFields[index];
        field.Capturing = true;
        field.Placeholder = UiText.T("capture.hint");
        field.Value = string.Empty;

        SetStatus("status.capturing", Theme.Orange);
    }

    /// <summary>收起录键的界面状态；restoreValue 为 true 时把原来的按键内容放回去。</summary>
    private void EndKeyCapture(bool restoreValue)
    {
        if (_captureIndex < 0) return;

        KeyField field = _keyFields[_captureIndex];
        field.Capturing = false;
        field.Placeholder = UiText.T("row.placeholder");
        if (restoreValue) field.Value = _captureBackup;

        _captureIndex = -1;
        _captureBackup = string.Empty;
    }

    /// <summary>主动放弃录键（开始连点、关闭窗口、换一行录）。</summary>
    private void CancelKeyCapture(bool restoreValue)
    {
        _keyCapture.Stop();
        EndKeyCapture(restoreValue);
    }

    private void HandleKeyCaptured(string text)
    {
        if (_closing || IsDisposed) return;

        if (InvokeRequired)
        {
            TryBeginInvoke(() => HandleKeyCaptured(text));
            return;
        }

        int index = _captureIndex;
        EndKeyCapture(restoreValue: false);

        if (index < 0 || index >= SlotCount) return;

        _keyFields[index].Value = text;
        _keyFields[index].Editor.SelectionStart = _keyFields[index].Editor.TextLength;
        SetStatus("status.keyCaptured", Theme.Green, text);
    }

    private void HandleKeyCaptureCancelled(string? reason)
    {
        if (_closing || IsDisposed) return;

        if (InvokeRequired)
        {
            TryBeginInvoke(() => HandleKeyCaptureCancelled(reason));
            return;
        }

        EndKeyCapture(restoreValue: true);

        if (reason != null) SetStatus("status.captureFailed", Theme.Orange, reason);
        else SetIdleStatus(Theme.TextPrimary);
    }

    /// <summary>捕获事件可能来自超时定时器的线程池线程，需要切回界面线程。</summary>
    private void TryBeginInvoke(Action action)
    {
        try
        {
            BeginInvoke(action);
        }
        catch (InvalidOperationException)
        {
            // 窗口已销毁：丢掉即可
        }
    }

    // ---------------------------------------------------------------- 引擎回调

    private void HandleEngineProgressChanged()
    {
        if (_closing) return;
        if (Interlocked.Exchange(ref _statusQueued, 1) == 1) return;

        try
        {
            BeginInvoke(new Action(() =>
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
            BeginInvoke(new Action(() => HandleEngineStoppedOnUi(result)));
        }
        catch (InvalidOperationException)
        {
            // 窗口已销毁
        }
    }

    private void HandleEngineStoppedOnUi(EngineStopResult result)
    {
        if (_closing) return;
        if (result.Reason == StopReason.User) return; // 主动停止已在 StopClicking 里同步处理
        if (result.RunId <= _handledStopRun) return;  // 这一轮已经由 StopClicking 处理过了
        _handledStopRun = result.RunId;

        SetRunningState(false);
        PersistProgress(result.Slots);
        RenderProgress(result.Slots);
        RefreshSummary();
        UpdateAutoSkipLabel();
        PlayStopSound();

        switch (result.Reason)
        {
            case StopReason.Completed:
                SetStatus("status.completed", Theme.Green, HotkeyOnText);
                break;

            case StopReason.Error:
                SetStatus("status.error", Theme.Red, result.Message ?? string.Empty);
                MessageBox.Show(
                    this,
                    result.Message ?? string.Empty,
                    UiText.T("dialog.title"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                break;
        }

        UpdateTrayTexts();
    }

    // ---------------------------------------------------------------- 界面状态

    private void SetRunningState(bool running)
    {
        _header.Running = running;

        _btnStart.Enabled = !running;
        _btnStop.Enabled = running;
        _btnSave.Enabled = !running;
        _btnLoad.Enabled = !running;
        _btnReset.Enabled = !running;

        for (int i = 0; i < SlotCount; i++)
        {
            _sw[i].Enabled = !running;
            _keyFields[i].Enabled = !running;
            _intervalFields[i].Enabled = !running;
            _countFields[i].Enabled = !running;
        }

        // 运行中禁用 ToolTip，避免提示窗口出现在前台抢走按键
        _toolTip.Active = !running;

        _hotkeyOnField.Enabled = !running;
        _hotkeyOffField.Enabled = !running;
        _skipKeyField.Enabled = !running;
        _skipCountField.Enabled = !running;

        UpdateTrayTexts();
    }

    private void OnSlotEnabledChanged(int index)
    {
        UpdateRowVisuals(index);
        RefreshSummary();
        UpdateAutoSkipLabel();
        _cardSlots.Invalidate();
    }

    private void OnSlotFieldChanged()
    {
        RefreshSummary();
    }

    /// <summary>未勾选的行整体变浅，直观表示“这一行不会执行”。</summary>
    private void UpdateRowVisuals(int index)
    {
        bool on = _sw[index].Checked;
        Color surface = on ? Theme.Card : ColorTranslator.FromHtml("#FAFAFC");
        Color valueColor = on ? Theme.TextPrimary : Theme.TextTertiary;

        _keyFields[index].SurfaceColor = surface;
        _intervalFields[index].SurfaceColor = surface;
        _countFields[index].SurfaceColor = surface;

        _keyFields[index].ValueColor = valueColor;
        _intervalFields[index].ValueColor = valueColor;
        _countFields[index].ValueColor = valueColor;
    }

    private void SetStatus(string key, Color color, params object[] args)
    {
        _statusKey = key;
        _statusArgs = args ?? Array.Empty<object>();
        _statusColor = color;
        RenderStatus();
    }

    private void SetIdleStatus(Color color)
    {
        SetStatus("status.stopped", color, HotkeyOnText, HotkeyOffText);
    }

    private void RenderStatus()
    {
        string text;
        try
        {
            text = UiText.T(_statusKey, _statusArgs);
        }
        catch (FormatException)
        {
            text = _statusKey;
        }

        _lblState.Text = text;
        _lblState.ForeColor = _statusColor;
    }

    private void RenderProgress(IReadOnlyList<SlotState> slots)
    {
        var parts = new List<string>();

        foreach (SlotState slot in slots)
        {
            if (!slot.Enabled || string.IsNullOrWhiteSpace(slot.Key)) continue;

            parts.Add(slot.MaxCount > 0
                ? UiText.T("progress.itemMax", slot.Key, slot.RunCount, slot.MaxCount, slot.Done, slot.Done + 1)
                : UiText.T("progress.itemInf", slot.Key, slot.RunCount, slot.Done));
        }

        _lblProgress.Text = parts.Count == 0 ? UiText.T("progress.empty") : string.Join("    ", parts);
    }

    /// <summary>“本次执行”摘要：把“哪些行会被执行、各发多少次、要跳过几项”讲清楚。</summary>
    private void RefreshSummary()
    {
        if (_lblSummary == null) return;

        List<SlotState> slots = ReadSlotsFromUi();
        List<SlotState> active = slots.Where(slot => slot.IsActive).ToList();

        if (active.Count == 0)
        {
            var warnings = slots.Where(s => s.Enabled && string.IsNullOrWhiteSpace(s.Key)).Select(s => s.Index).ToList();
            string none = UiText.T("summary.none");
            if (warnings.Count > 0) none += " · " + UiText.T("summary.warnEmptyKey", string.Join(",", warnings));
            _lblSummary.Text = none;
            return;
        }

        const string separator = "、";
        string items = string.Join(separator, active.Select(slot =>
            UiText.T("summary.item", slot.Key, slot.MaxCount > 0 ? slot.MaxCount.ToString(CultureInfo.InvariantCulture) : "∞")));

        string skipText;
        string manualSkip = _skipCountField.Value.Trim();
        if (manualSkip.Length == 0)
        {
            skipText = UiText.T("summary.skipAuto", ClickSession.AutoSkipCount(slots));
        }
        else
        {
            skipText = UiText.T("summary.skipManual", manualSkip);
        }

        string text = UiText.T("summary.prefix", items) + " · " + UiText.T("summary.rows", active.Count) + " · " + skipText;

        List<int> emptyChecked = slots.Where(s => s.Enabled && string.IsNullOrWhiteSpace(s.Key)).Select(s => s.Index).ToList();
        if (emptyChecked.Count > 0)
        {
            text += " · " + UiText.T("summary.warnEmptyKey", string.Join(",", emptyChecked));
        }

        text += " · " + (_targetWindow != IntPtr.Zero && WindowsDesktop.IsWindow(_targetWindow)
            ? UiText.T("summary.target", ShortenTitle(_targetTitle))
            : UiText.T("summary.targetNone"));

        _lblSummary.Text = text;
    }

    /// <summary>真正发送前把前台窗口切回目标窗口（在引擎线程里调用）。</summary>
    private void RestoreTargetFocus()
    {
        IntPtr foreground = WindowsDesktop.GetForegroundWindow();
        if (!WindowsDesktop.IsOwnProcessWindow(foreground, _processId) && foreground != IntPtr.Zero)
        {
            return; // 已经是别的程序在前台，按键直接发过去即可
        }

        IntPtr target = _targetWindow;
        if (!WindowsDesktop.IsWindow(target) || WindowsDesktop.IsMinimized(target))
        {
            target = WindowsDesktop.ResolveSendTarget(_processId, Handle, IntPtr.Zero);
        }

        if (target == IntPtr.Zero || target == Handle) return;

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

        _targetTimer = new System.Windows.Forms.Timer { Interval = 400 };
        _targetTimer.Tick += (_, _) =>
        {
            if (_closing) return;
            IntPtr current = WindowsDesktop.GetForegroundWindow();
            if (current != IntPtr.Zero) RememberTarget(current);
        };
        _targetTimer.Start();
    }

    /// <summary>记住最近一个属于其它程序的前台窗口，作为按键的目标窗口。</summary>
    private void RememberTarget(IntPtr window)
    {
        if (window == IntPtr.Zero || window == Handle) return;
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

    /// <summary>把配置读取失败的记录 "read|原因" 还原成原因文本。</summary>
    private static string UnwrapNote(string note)
    {
        int separator = note.IndexOf('|');
        return separator >= 0 ? note[(separator + 1)..] : note;
    }

    private void UpdateAutoSkipLabel()
    {
        if (_lblSkipAuto == null) return;

        string manual = _skipCountField.Value.Trim();
        _lblSkipAuto.Text = manual.Length > 0
            ? UiText.T("skip.manual", manual)
            : UiText.T("skip.auto", ClickSession.AutoSkipCount(ReadSlotsFromUi()));
    }

    /// <summary>把本轮进度并入设置并写入磁盘。返回保存失败的说明；成功时返回 null。</summary>
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
        SetStatus("status.error", Theme.Red, errors[0]);
        MessageBox.Show(
            this,
            string.Join(Environment.NewLine, errors),
            UiText.T("dialog.settingsIssue"),
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
        return true;
    }

    // ---------------------------------------------------------------- 小工具

    private static string FormatNumber(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);

    private static bool TryParseNumber(string text, out double value)
    {
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) return true;
        return double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
    }
}

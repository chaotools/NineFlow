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
public sealed partial class MainForm : Form
{
    private const int DesignWidth = 1120;
    private const int DesignHeight = 720;
    private const int PageMargin = 24;
    private const int CardGap = 16;

    // 自定义顶栏已移除，标题与状态放在系统标题栏。保留这个常量作为布局起点偏移。
    private const int HeaderH = 0;
    private const int RunGap = 4;
    private const int RunY = HeaderH + RunGap;
    private const int RunH = 136;
    private const int MiddleGap = 8;
    private const int RightCardW = 336;
    private const int MinClientW = 1040;
    private const int MinClientH = 620;
    private const int ButtonH = 40;

    private const int SlotRowHeight = 38;
    private const int SlotRowsTop = 74;

    private const int HotkeyStartId = 0x4E46;
    private const int HotkeyStopId = 0x4E47;
    private const int HotkeyResetId = 0x4E48;

    private readonly SettingsStore _store;
    private readonly ClickEngine _engine;
    private readonly KeyCapture _keyCapture;
    private readonly EventWaitHandle? _showEvent;

    private AppSettings _settings;
    private HotkeyRegistry? _hotkeys;
    private NotifyIcon? _tray;
    private ToolTip _toolTip = null!;

    private Panel _root = null!;
    private CardPanel _cardRun = null!;
    private CardPanel _cardSlots = null!;
    private CardPanel _cardHotkeys = null!;
    private CardPanel _cardSummary = null!;
    private Panel _slotScroll = null!;
    private Label _lblSlotsHint = null!;

    /// <summary>顶部四个指标卡，窗口缩放时重新分布宽度。</summary>
    private readonly List<Panel> _metricBoxes = new();

    /// <summary>当前界面上实际存在的行；数量可增删，最少 2 行。</summary>
    private readonly List<SlotRow> _rows = new();

    /// <summary>重建行时暂时屏蔽 TextChanged 引发的重算。</summary>
    private bool _rebuildingRows;

    private AppTextField _hotkeyOnField = null!;
    private AppTextField _hotkeyOffField = null!;
    private AppTextField _hotkeyResetField = null!;
    private AppTextField _skipKeyField = null!;
    private AppTextField _skipCountField = null!;
    private Label _lblSkipAuto = null!;

    // 次要操作与四个开关收进「设置」菜单，仍保留原有自动化 ID
    private ToolStripMenuItem _miSave = null!;
    private ToolStripMenuItem _miLoad = null!;
    private ToolStripMenuItem _miUpdate = null!;
    private ToolStripMenuItem _miBeep = null!;
    private ToolStripMenuItem _miCloseToTray = null!;
    private ToolStripMenuItem _miTopMost = null!;
    private ToolStripMenuItem _miAutoUpdate = null!;

    private AppButton _btnMenu = null!;
    private ContextMenuStrip _actionMenu = null!;
    private AppButton _btnStart = null!;
    private AppButton _btnStop = null!;
    private AppButton _btnReset = null!;
    private AppButton _btnAddSlot = null!;

    private Label _lblTarget = null!;
    private Label _lblMetricRound = null!;
    private Label _lblMetricTotal = null!;
    private Label _lblMetricNext = null!;
    private Label _lblMetricSkip = null!;

    /// <summary>正在检查或下载更新，避免重复触发。</summary>
    private bool _updateBusy;

    /// <summary>载入配置时只同步菜单勾选，不写回配置也不弹状态反馈。</summary>
    private bool _suppressOptionFeedback;

    private Label _lblState = null!;
    private Label _lblProgress = null!;
    private Label _lblSummary = null!;

    /// <summary>跨线程读取（引擎回调线程 / ShowListener 线程），必须 volatile。</summary>
    private volatile bool _closing;
    private bool _exiting;

    private string _statusKey = "status.stopped";
    private object[] _statusArgs = Array.Empty<object>();
    private Color _statusColor = Theme.TextPrimary;

    private int _statusQueued;

    /// <summary>把「用户点停止」和「引擎自然结束」合并成一份状态迁移，并保证同一轮只处理一次。</summary>
    private readonly RunCoordinator _runCoordinator;

    /// <summary>正在录键的行下标；-1 表示没在录。</summary>
    private int _captureIndex = -1;

    /// <summary>开始录键时该行原来的内容，取消时恢复回去。</summary>
    private string _captureBackup = string.Empty;

    /// <summary>控件 → tooltip 文案键；BuildUi 结束后统一 SetToolTip。</summary>
    private readonly List<(Control Control, string Key)> _tooltips = new();

    private bool _balloonShown;

    /// <summary>热键注册失败的气泡是否已经弹过（每次运行只弹一次，避免反复骚扰）。</summary>
    private bool _hotkeyProblemShown;

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

        _runCoordinator = new RunCoordinator(PersistProgress);

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
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96f, 96f);
        ClientSize = new Size(DesignWidth, DesignHeight);
        MinimumSize = new Size(MinClientW + (Width - ClientSize.Width), MinClientH + (Height - ClientSize.Height));
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

        BuildRunCard();
        BuildSlotsCard();
        BuildHotkeyCard();
        BuildSummaryCard();
        BuildMenu();

        // 统一挂 tooltip：此前 _tooltips 只收集不消费，除按钮外所有控件的提示都不生效
        foreach ((Control control, string key) in _tooltips)
        {
            _toolTip.SetToolTip(control, UiText.T(key));
        }

        ApplyResponsiveLayout();
        ResumeLayout(true);
    }

    private void BuildSlotsCard()
    {
        _cardSlots = new CardPanel
        {
            Location = new Point(PageMargin, HeaderH + RunGap + RunH + MiddleGap),
            Size = new Size(DesignWidth - PageMargin * 2 - CardGap - RightCardW, 400),
        };
        _root.Controls.Add(_cardSlots);

        AddLabel(_cardSlots, "card.slots.title", new Rectangle(18, 12, 220, 26), Theme.Font(12f, FontStyle.Bold), Theme.TextPrimary);
        _lblSlotsHint = AddLabel(_cardSlots, "card.slots.hint",
            new Rectangle(240, 14, _cardSlots.Width - 240 - 18, 22),
            Theme.Font(8.5f), Theme.TextTertiary, ContentAlignment.MiddleRight);

        AddLabel(_cardSlots, "col.enabled", new Rectangle(20, 48, 32, 20), Theme.Font(8.5f), Theme.TextTertiary);
        AddLabel(_cardSlots, "col.key", new Rectangle(54, 48, 176, 20), Theme.Font(8.5f), Theme.TextTertiary);
        AddLabel(_cardSlots, "col.interval", new Rectangle(236, 48, 80, 20), Theme.Font(8.5f), Theme.TextTertiary);
        AddLabel(_cardSlots, "col.count", new Rectangle(336, 48, 52, 20), Theme.Font(8.5f), Theme.TextTertiary);
        AddLabel(_cardSlots, "col.progress", new Rectangle(394, 48, 100, 20), Theme.Font(8.5f), Theme.TextTertiary);

        _slotScroll = new Panel
        {
            Location = new Point(18, SlotRowsTop),
            Size = new Size(_cardSlots.Width - 36, 330),
            BackColor = Theme.Card,
            AutoScroll = true,
        };
        _cardSlots.Controls.Add(_slotScroll);

        _btnAddSlot = new AppButton
        {
            Kind = AppButtonKind.Secondary,
            Location = new Point(18, 414),
            Size = new Size(120, 30),
            Text = UiText.T("button.addSlot"),
            Name = "button.addSlot",
        };
        _btnAddSlot.Click += (_, _) => AddSlotRow();
        _cardSlots.Controls.Add(_btnAddSlot);
        _tooltips.Add((_btnAddSlot, "tooltip.addSlot"));
    }

    /// <summary>界面上的一行。控件保持在列表里，删行时整行一起销毁并重新编号。</summary>
    private sealed class SlotRow
    {
        public SlotRow(Panel panel, AppSwitch toggle, KeyField key, AppTextField interval,
            AppTextField count, Label progress, AppButton remove)
        {
            Panel = panel;
            Toggle = toggle;
            Key = key;
            Interval = interval;
            Count = count;
            Progress = progress;
            Remove = remove;
        }

        public Panel Panel { get; }
        public AppSwitch Toggle { get; }
        public KeyField Key { get; }
        public AppTextField Interval { get; }
        public AppTextField Count { get; }
        public Label Progress { get; }
        public AppButton Remove { get; }
    }

    private SlotRow CreateSlotRow(int index, SlotState slot)
    {
        var panel = new Panel
        {
            Location = new Point(0, index * SlotRowHeight),
            Size = new Size(Math.Max(560, _slotScroll.ClientSize.Width), SlotRowHeight),
            BackColor = Theme.Card,
        };

        var toggle = new AppSwitch
        {
            Location = new Point(2, 6),
            SurfaceColor = Theme.Card,
            Name = $"slot{index + 1}.enabled",
            Checked = slot.Enabled,
        };

        var key = new KeyField
        {
            Location = new Point(52, 3),
            Size = new Size(176, 32),
            SurfaceColor = Theme.Card,
            Name = $"slot{index + 1}.keybox",
            Value = slot.Key ?? string.Empty,
            Placeholder = UiText.T("row.placeholder"),
        };
        key.Editor.Name = $"slot{index + 1}.key";

        var interval = new AppTextField
        {
            Location = new Point(236, 3),
            Size = new Size(72, 32),
            SurfaceColor = Theme.Card,
            TextAlign = HorizontalAlignment.Center,
            Value = FormatNumber(slot.Interval),
        };
        interval.Editor.Name = $"slot{index + 1}.interval";

        var unit = AddLabel(panel, "unit.second", new Rectangle(310, 3, 24, 32),
            Theme.Font(9f), Theme.TextTertiary, ContentAlignment.MiddleCenter, background: Theme.Card);

        var count = new AppTextField
        {
            Location = new Point(336, 3),
            Size = new Size(52, 32),
            SurfaceColor = Theme.Card,
            TextAlign = HorizontalAlignment.Center,
            Value = slot.MaxCount.ToString(CultureInfo.InvariantCulture),
            Placeholder = UiText.T("field.placeholder.count"),
        };
        count.Editor.Name = $"slot{index + 1}.max";

        var progress = AddLabel(panel, null, new Rectangle(394, 3, 100, 32),
            Theme.Font(9f), Theme.TextSecondary, ContentAlignment.MiddleLeft, background: Theme.Card);
        progress.Name = $"slot{index + 1}.progress";

        SlotRow? row = null;
        var remove = new AppButton
        {
            Kind = AppButtonKind.Secondary,
            Location = new Point(500, 5),
            Size = new Size(30, 28),
            Text = "−",
            Name = $"button.removeSlot{index + 1}",
        };
        remove.Click += (_, _) => { if (row != null) RemoveSlotRow(row); };

        row = new SlotRow(panel, toggle, key, interval, count, progress, remove);
        foreach (Control control in new Control[] { toggle, key, interval, count, progress, remove, unit })
        {
            control.Tag = row;
        }
        panel.Controls.Add(toggle);
        panel.Controls.Add(key);
        panel.Controls.Add(interval);
        panel.Controls.Add(unit);
        panel.Controls.Add(count);
        panel.Controls.Add(progress);
        panel.Controls.Add(remove);

        toggle.CheckedChanged += OnSlotControlChanged;
        key.Editor.TextChanged += OnSlotControlChanged;
        interval.Editor.TextChanged += OnSlotControlChanged;
        count.Editor.TextChanged += OnSlotControlChanged;
        key.CaptureRequested += (_, _) => BeginKeyCapture(index);

        // 行会反复重建，不能塞进 BuildUi 末尾那次性处理的 _tooltips 列表
        if (_toolTip != null)
        {
            _toolTip.SetToolTip(toggle, UiText.T("tooltip.enabled"));
            _toolTip.SetToolTip(key, UiText.T("tooltip.key"));
            _toolTip.SetToolTip(interval, UiText.T("tooltip.interval"));
            _toolTip.SetToolTip(count, UiText.T("tooltip.count"));
            _toolTip.SetToolTip(remove, UiText.T("tooltip.removeSlot"));
        }

        return row;
    }

    /// <summary>按给定配置重建所有行；增删行后行号与自动化 ID 会重新连续编号。</summary>
    private void RebuildSlotRows(IReadOnlyList<SlotState> slots)
    {
        CancelKeyCapture(restoreValue: true);
        _rebuildingRows = true;

        foreach (SlotRow row in _rows)
        {
            if (_toolTip != null)
            {
                _toolTip.SetToolTip(row.Toggle, null);
                _toolTip.SetToolTip(row.Key, null);
                _toolTip.SetToolTip(row.Interval, null);
                _toolTip.SetToolTip(row.Count, null);
                _toolTip.SetToolTip(row.Remove, null);
            }
            _slotScroll.Controls.Remove(row.Panel);
            row.Panel.Dispose();
        }
        _rows.Clear();

        for (int i = 0; i < slots.Count; i++)
        {
            SlotRow row = CreateSlotRow(i, slots[i]);
            _rows.Add(row);
            _slotScroll.Controls.Add(row.Panel);
            UpdateRowVisuals(i);
        }

        _rebuildingRows = false;
        UpdateAddRemoveButtons();
        RefreshSummary();
        UpdateAutoSkipLabel();
    }

    private void AddSlotRow()
    {
        if (_closing || _engine.IsRunning || _rows.Count >= AppDefaults.MaxSlots) return;

        SyncSettingsFromUi();
        _settings.Slots.Add(AppDefaults.CreateBlankSlot(_settings.Slots.Count + 1));
        RebuildSlotRows(_settings.Slots);
        SetStatus("status.slotAdded", Theme.Green, _rows.Count);
    }

    private void RemoveSlotRow(SlotRow row)
    {
        if (_closing || _engine.IsRunning || _rows.Count <= AppDefaults.MinSlots) return;

        int index = _rows.IndexOf(row);
        if (index < 0) return;

        SyncSettingsFromUi();
        SlotState slot = _settings.Slots[index];
        if (slot.Done > 0 &&
            MessageBox.Show(this,
                UiText.T("dialog.removeSlot.text", slot.Done),
                UiText.T("dialog.removeSlot.title"),
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }

        _settings.Slots.RemoveAt(index);
        RebuildSlotRows(_settings.Slots);
        SetStatus("status.slotRemoved", Theme.Green, _rows.Count);
    }

    private SlotRow? FindSlotRow(Control? control)
    {
        while (control != null && control != _slotScroll)
        {
            if (control.Tag is SlotRow row) return row;
            control = control.Parent;
        }

        return null;
    }

    private void UpdateAddRemoveButtons()
    {
        bool running = _engine.IsRunning;
        _btnAddSlot.Enabled = !running && _rows.Count < AppDefaults.MaxSlots;
        foreach (SlotRow row in _rows)
        {
            row.Remove.Enabled = !running && _rows.Count > AppDefaults.MinSlots;
        }
    }

    private void BuildHotkeyCard()
    {
        _cardHotkeys = new CardPanel
        {
            Location = new Point(DesignWidth - PageMargin - RightCardW, HeaderH + RunGap + RunH + MiddleGap),
            Size = new Size(RightCardW, 196),
        };
        _root.Controls.Add(_cardHotkeys);

        AddLabel(_cardHotkeys, "card.hotkeys.title", new Rectangle(18, 10, 200, 26), Theme.Font(12f, FontStyle.Bold), Theme.TextPrimary);

        AddLabel(_cardHotkeys, "label.start", new Rectangle(18, 44, 36, 30), Theme.Font(9f), Theme.TextSecondary);
        _hotkeyOnField = AddField(_cardHotkeys, "hotkey.start", new Rectangle(56, 44, 80, 30), "tooltip.hotkeyStart");

        AddLabel(_cardHotkeys, "label.stop", new Rectangle(146, 44, 36, 30), Theme.Font(9f), Theme.TextSecondary);
        _hotkeyOffField = AddField(_cardHotkeys, "hotkey.stop", new Rectangle(184, 44, 80, 30), "tooltip.hotkeyStop");

        AddLabel(_cardHotkeys, "label.reset", new Rectangle(18, 86, 36, 30), Theme.Font(9f), Theme.TextSecondary);
        _hotkeyResetField = AddField(_cardHotkeys, "hotkey.reset", new Rectangle(56, 86, 80, 30), "tooltip.hotkeyReset");

        AddLabel(_cardHotkeys, "label.skipkey", new Rectangle(146, 86, 52, 30), Theme.Font(9f), Theme.TextSecondary);
        _skipKeyField = AddField(_cardHotkeys, "hotkey.skipkey", new Rectangle(202, 86, 100, 30), "tooltip.skipKey");

        AddLabel(_cardHotkeys, "label.skipcount", new Rectangle(18, 128, 56, 30), Theme.Font(9f), Theme.TextSecondary);
        _skipCountField = AddField(_cardHotkeys, "hotkey.skipcount", new Rectangle(76, 128, 80, 30), "tooltip.skipCount");
        _skipCountField.Editor.TextChanged += (_, _) =>
        {
            UpdateAutoSkipLabel();
            RefreshSummary();
        };

        _lblSkipAuto = AddLabel(_cardHotkeys, null, new Rectangle(166, 128, RightCardW - 184, 30), Theme.Font(8.5f), Theme.TextTertiary);
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

    private void BuildRunCard()
    {
        _cardRun = new CardPanel
        {
            Location = new Point(PageMargin, RunY),
            Size = new Size(DesignWidth - PageMargin * 2, RunH),
        };
        _root.Controls.Add(_cardRun);

        _btnStart = AddButton(_cardRun, "button.start", 24, 9, 150, 38, "tooltip.start", AppButtonKind.Primary);
        _btnStop = AddButton(_cardRun, "button.stop", 184, 9, 120, 38, "tooltip.stop", AppButtonKind.Danger);
        _btnReset = AddButton(_cardRun, "button.reset", 314, 9, 110, 38, "tooltip.reset", AppButtonKind.Secondary);
        _btnReset.Click += (_, _) => ResetProgress();

        _btnMenu = AddButton(_cardRun, "button.more", 880, 9, 90, 38, "tooltip.more", AppButtonKind.Secondary);
        _btnMenu.Text = "设置 ▾";
        _btnStart.Click += (_, _) => StartClicking();
        _btnStop.Click += (_, _) => StopClicking();
        _btnMenu.Click += (_, _) =>
        {
            CancelKeyCapture(restoreValue: true);
            _actionMenu.Show(_btnMenu, new Point(0, _btnMenu.Height + 2));
        };

        _lblState = AddLabel(_root, null, new Rectangle(PageMargin, DesignHeight - PageMargin - 24, DesignWidth - PageMargin * 2, 24), Theme.Font(10f, FontStyle.Bold), Theme.TextPrimary);
        _lblState.Name = "status.state";

        _lblTarget = AddLabel(_cardRun, null, new Rectangle(750, 9, 178, 38), Theme.Font(9f), Theme.TextSecondary, ContentAlignment.MiddleRight);
        _lblTarget.Name = "status.target";

        _lblMetricRound = AddMetric("metric.round", "metric.round", 24);
        _lblMetricTotal = AddMetric("metric.total", "metric.total", 256);
        _lblMetricNext = AddMetric("metric.next", "metric.next", 488);
        _lblMetricSkip = AddMetric("metric.skip", "metric.skip", 720);

        _lblProgress = AddLabel(_cardRun, null, new Rectangle(24, 106, DesignWidth - PageMargin * 2 - 48, 22), Theme.Font(8.5f), Theme.TextSecondary);
        _lblProgress.Name = "status.progress";
    }

    private Label AddMetric(string labelKey, string name, int x)
    {
        var box = new Panel
        {
            Location = new Point(x, 53),
            Size = new Size(220, 48),
            BackColor = Theme.SecondaryFill,
        };
        _cardRun.Controls.Add(box);
        _metricBoxes.Add(box);

        AddLabel(box, labelKey, new Rectangle(12, 5, 196, 16), Theme.Font(8.5f), Theme.TextSecondary,
            background: Theme.SecondaryFill);

        var value = AddLabel(box, null, new Rectangle(12, 20, 196, 24), Theme.Font(14f, FontStyle.Bold), Theme.TextPrimary,
            background: Theme.SecondaryFill);
        value.Name = name;
        return value;
    }

    private void BuildSummaryCard()
    {
        _cardSummary = new CardPanel
        {
            Location = new Point(DesignWidth - PageMargin - RightCardW, HeaderH + RunGap + RunH + MiddleGap + 204),
            Size = new Size(RightCardW, 140),
        };
        _root.Controls.Add(_cardSummary);

        AddLabel(_cardSummary, "card.summary.title", new Rectangle(18, 10, 200, 24), Theme.Font(12f, FontStyle.Bold), Theme.TextPrimary);

        _lblSummary = AddLabel(_cardSummary, null, new Rectangle(18, 38, RightCardW - 36, 86), Theme.Font(9f), Theme.Accent);
        _lblSummary.Name = "status.summary";
    }

    /// <summary>四个设置项放一级，各自的开关放二级；次要操作也在一级，保留原有自动化 ID。</summary>
    private void BuildMenu()
    {
        _actionMenu = new ContextMenuStrip
        {
            Font = Theme.Font(9.5f),
            ShowImageMargin = false,
            BackColor = Color.White,
        };

        _miSave = AddMenuItem("button.save", SaveConfig);
        _miLoad = AddMenuItem("button.load", LoadConfig);
        _miUpdate = AddMenuItem("button.update", async () => await CheckForUpdatesAsync(manual: true));
        _actionMenu.Items.Add(new ToolStripSeparator());

        _miBeep = AddCheckGroup("switch.beep", on => { _settings.Beep = on; SaveOptionsQuietly(); });
        _miCloseToTray = AddCheckGroup("switch.closeToTray", on => { _settings.CloseToTray = on; SaveOptionsQuietly(); });
        _miTopMost = AddCheckGroup("switch.topMost", on =>
        {
            _settings.TopMost = on;
            TopMost = _settings.TopMost;
            SaveOptionsQuietly();
        });
        _miAutoUpdate = AddCheckGroup("switch.autoUpdate", on => { _settings.AutoCheckUpdate = on; SaveOptionsQuietly(); });
    }

    private ToolStripMenuItem AddMenuItem(string id, Action action)
    {
        var item = new ToolStripMenuItem(UiText.T(id)) { Name = id };
        item.Click += (_, _) => action();
        _actionMenu.Items.Add(item);
        return item;
    }

    private ToolStripMenuItem AddCheckGroup(string id, Action<bool> onChanged)
    {
        string settingName = UiText.T(id);
        var group = new ToolStripMenuItem(settingName + " ▸") { Name = id + ".group" };
        _actionMenu.Items.Add(group);
        if (group.DropDown is ToolStripDropDownMenu dropDown)
        {
            dropDown.Font = Theme.Font(9.5f);
            dropDown.ShowImageMargin = false;
            dropDown.ShowCheckMargin = true;
            dropDown.BackColor = Color.White;
        }

        string label = UiText.T("menu.enable");
        var item = new ToolStripMenuItem(label) { Name = id, CheckOnClick = true };
        item.CheckedChanged += (_, _) =>
        {
            item.Text = (item.Checked ? "✓ " : "　 ") + label;
            if (_suppressOptionFeedback) return;

            onChanged(item.Checked);
            SetStatus(item.Checked ? "status.optionOn" : "status.optionOff",
                item.Checked ? Theme.Green : Theme.TextSecondary, settingName);
        };
        group.DropDown.Items.Add(item);
        return item;
    }

    private AppButton AddButton(Control parent, string key, int x, int y, int width, int height, string tooltipKey, AppButtonKind kind)
    {
        var button = new AppButton
        {
            Kind = kind,
            Location = new Point(x, y),
            Size = new Size(width, height),
            Name = "button." + key.Replace("button.", string.Empty),
        };

        button.Text = UiText.T(key);
        parent.Controls.Add(button);
        _tooltips.Add((button, tooltipKey));
        return button;
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
        // 录键的键盘钩子会吞掉所有输入：窗口一旦藏到托盘就看不见提示了，
        // 用户只能盲等 8 秒超时，期间整个系统的键盘都失灵。先收掉再隐藏。
        CancelKeyCapture(restoreValue: true);

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
        ShowHotkeyProblem(RegisterHotkeys(), "status.hotkeyProblem");
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

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        ApplyResponsiveLayout();
    }

    /// <summary>按当前客户区重新摆放各卡片；右侧栏保持固定宽度，左侧列表吸收多余宽度。</summary>
    private void ApplyResponsiveLayout()
    {
        if (_cardRun == null || _cardSlots == null || _cardHotkeys == null ||
            _cardSummary == null || _slotScroll == null) return;

        int width = Math.Max(MinClientW, ClientSize.Width);
        int height = Math.Max(MinClientH, ClientSize.Height);
        int contentW = width - PageMargin * 2;
        int leftW = contentW - CardGap - RightCardW;
        int rightX = PageMargin + leftW + CardGap;
        int middleY = RunY + RunH + MiddleGap;
        int statusY = height - PageMargin - 24;
        int middleH = Math.Max(240, statusY - MiddleGap - middleY);

        _cardRun.Bounds = new Rectangle(PageMargin, RunY, contentW, RunH);
        int menuX = contentW - 24 - 90;
        int targetW = 160;
        int targetX = menuX - 8 - targetW;
        _lblState.Bounds = new Rectangle(PageMargin, statusY, contentW, 24);
        _lblTarget.Bounds = new Rectangle(targetX, 9, targetW, 38);
        _btnMenu.Location = new Point(menuX, 9);

        const int metricGap = 12;
        int metricW = Math.Max(120, (contentW - 48 - metricGap * 3) / 4);
        for (int i = 0; i < _metricBoxes.Count; i++)
        {
            Panel box = _metricBoxes[i];
            box.Bounds = new Rectangle(24 + i * (metricW + metricGap), 53, metricW, 48);
            foreach (Control child in box.Controls) child.Width = Math.Max(40, box.Width - 24);
        }
        _lblProgress.Bounds = new Rectangle(24, 106, contentW - 48, 22);

        _cardSlots.Bounds = new Rectangle(PageMargin, middleY, leftW, middleH);
        _lblSlotsHint.Width = Math.Max(120, leftW - 258);
        _slotScroll.Bounds = new Rectangle(18, SlotRowsTop, leftW - 36,
            Math.Max(80, middleH - SlotRowsTop - 46));
        _btnAddSlot.Location = new Point(18, middleH - 38);

        foreach (SlotRow row in _rows)
        {
            row.Panel.Width = Math.Max(560, _slotScroll.ClientSize.Width);
            row.Remove.Left = row.Panel.Width - 38;
            row.Progress.Width = Math.Max(40, row.Remove.Left - row.Progress.Left - 8);
        }

        _cardHotkeys.Bounds = new Rectangle(rightX, middleY, RightCardW, 196);
        _cardSummary.Bounds = new Rectangle(rightX, middleY + 204, RightCardW, 140);
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

        // 退出前把界面上的改动一并落盘：只存进度和 General 的话，
        // 用户改了按键/间隔/热键没点“保存配置”就关窗，改动会全部丢失
        SyncSettingsFromUi();
        PersistProgress(result?.Slots ?? _settings.Slots);
        _store.SaveConfig(_settings, out _);

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
        // 停止引擎与保存配置统一交给 OnFormClosing：这里自己 Stop + Persist 会
        // 丢弃 Stop 返回的最终快照，进度保存全靠 OnFormClosing 的二次 Stop “碰巧正确”
        _exiting = true;
        Close();
    }

    // ---------------------------------------------------------------- 设置读写

    private string HotkeyOnText => NonEmptyOr(_hotkeyOnField.Value, AppDefaults.HotkeyOn);

    private string HotkeyOffText => NonEmptyOr(_hotkeyOffField.Value, AppDefaults.HotkeyOff);

    private string HotkeyResetText => NonEmptyOr(_hotkeyResetField.Value, AppDefaults.HotkeyReset);

    private static string NonEmptyOr(string? text, string fallback) =>
        string.IsNullOrWhiteSpace(text) ? fallback : text.Trim();

    private void ApplySettings(AppSettings settings)
    {
        // 极端情况下（手工改坏 ini）也要保证界面上至少有两行
        while (settings.Slots.Count < AppDefaults.MinSlots)
        {
            settings.Slots.Add(AppDefaults.CreateBlankSlot(settings.Slots.Count + 1));
        }

        RebuildSlotRows(settings.Slots);

        _hotkeyOnField.Value = settings.HotkeyOn;
        _hotkeyOffField.Value = settings.HotkeyOff;
        _hotkeyResetField.Value = settings.HotkeyReset;
        _skipKeyField.Value = settings.SkipKey;
        _skipCountField.Value = settings.SkipCount ?? string.Empty;

        _suppressOptionFeedback = true;
        _miBeep.Checked = settings.Beep;
        _miCloseToTray.Checked = settings.CloseToTray;
        _miTopMost.Checked = settings.TopMost;
        _miAutoUpdate.Checked = settings.AutoCheckUpdate;
        _suppressOptionFeedback = false;

        UpdateAutoSkipLabel();
        RefreshSummary();
    }

    private List<SlotState> ReadSlotsFromUi(List<string> errors, bool validate)
    {
        var inputs = new List<SlotInput>(_rows.Count);
        for (int i = 0; i < _rows.Count; i++)
        {
            SlotRow row = _rows[i];
            SlotState previous = i < _settings.Slots.Count ? _settings.Slots[i] : new SlotState { Index = i + 1 };

            inputs.Add(new SlotInput(
                i + 1,
                row.Toggle.Checked,
                row.Key.Value,
                row.Interval.Value,
                row.Count.Value,
                previous.Done,
                previous.RunCount));
        }

        // 解析与回退规则在 Core（SlotInputParser），界面只把控件值搬过去
        var problems = new List<UiProblem>();
        List<SlotState> slots = SlotInputParser.ParseAll(inputs, validate, problems);

        foreach (UiProblem problem in problems) errors.Add(problem.Text);
        return slots;
    }

    private List<SlotState> ReadSlotsFromUi() => ReadSlotsFromUi(new List<string>(), validate: false);

    /// <summary>
    /// 把界面上的当前编辑内容收进 _settings（不做校验）。
    /// 保存配置与退出兜底保存共用，保证两条路径写入的内容一致。
    /// </summary>
    private void SyncSettingsFromUi()
    {
        _settings.Slots = ReadSlotsFromUi();
        _settings.HotkeyOn = _hotkeyOnField.Value.Trim();
        _settings.HotkeyOff = _hotkeyOffField.Value.Trim();
        _settings.HotkeyReset = _hotkeyResetField.Value.Trim();
        _settings.SkipKey = _skipKeyField.Value.Trim();
        _settings.SkipCount = _skipCountField.Value.Trim();
        _settings.Beep = _miBeep.Checked;
        _settings.CloseToTray = _miCloseToTray.Checked;
        _settings.TopMost = _miTopMost.Checked;
        _settings.AutoCheckUpdate = _miAutoUpdate.Checked;
    }

    private void SaveOptionsQuietly() => _store.SaveOptions(_settings, out _);

    // ---------------------------------------------------------------- 按钮动作

    private void SaveConfig()
    {
        // 录键期间输入框是空的，直接保存会把这一行存成空按键；先收掉再读界面
        CancelKeyCapture(restoreValue: true);

        var errors = new List<string>();
        ReadSlotsFromUi(errors, validate: true);
        if (ReportValidationErrors(errors)) return;

        SyncSettingsFromUi();

        if (!_store.SaveConfig(_settings, out string error))
        {
            SetStatus("status.saveFailed", Theme.Red, error);
            return;
        }

        // 注册失败必须说出来：否则界面说「已保存」，用户以为热键生效了，其实没有
        List<string> hotkeyProblems = RegisterHotkeys();
        if (hotkeyProblems.Count > 0) ShowHotkeyProblem(hotkeyProblems, "status.savedHotkeyProblem");
        else SetStatus("status.saved", Theme.Green, _store.Path);

        RenderProgress(_settings.Slots);
        RefreshSummary();
        UpdateAutoSkipLabel();
    }

    private void LoadConfig()
    {
        CancelKeyCapture(restoreValue: true);

        if (!_store.Exists)
        {
            SetStatus("status.noConfig", Theme.Orange, Array.Empty<object>());
            Beep(beepOn: true);
            return;
        }

        _settings = _store.Load();
        ApplySettings(_settings);
        List<string> hotkeyProblems = RegisterHotkeys();
        RenderProgress(_settings.Slots);
        TopMost = _settings.TopMost;

        if (hotkeyProblems.Count > 0) ShowHotkeyProblem(hotkeyProblems, "status.loadedHotkeyProblem");
        else if (_settings.Notes.Count > 0) SetStatus("note.read", Theme.Orange, UnwrapNote(_settings.Notes[0]));
        else SetStatus("status.loaded", Theme.Green, Array.Empty<object>());
    }

    private void StartClicking()
    {
        if (_closing) return;
        if (_engine.IsRunning) return;

        // 录键时钩子会吞掉所有键盘输入，连点发出去的键也会被它吃掉，先收掉
        CancelKeyCapture(restoreValue: true);

        var errors = new List<string>();
        List<SlotState> slots = ReadSlotsFromUi(errors, validate: true);
        if (ReportValidationErrors(errors)) return;

        // 确认有可用的目标窗口：按键总是发给前台窗口，如果九序自己在前台就没有意义
        IntPtr sendTarget = WindowsDesktop.ResolveSendTarget(_processId, Handle, _targetWindow);
        bool targetUsable = sendTarget != IntPtr.Zero && !WindowsDesktop.IsMinimized(sendTarget);

        var request = new StartRequest(
            slots,
            StartHotkey: HotkeyOnText,
            StopHotkey: HotkeyOffText,
            SkipKey: _skipKeyField.Value,
            SkipCount: _skipCountField.Value,
            TargetUsable: targetUsable,
            ResetHotkey: HotkeyResetText);

        // 校验（按键写法、热键撞车、跳过键、跳过数、目标窗口）全在 Core 里，界面只负责渲染结果
        StartPlan? plan = StartPlanner.TryCreate(request, out UiProblem? problem);
        if (plan == null)
        {
            Beep(beepOn: true);
            ShowProblem(problem!);
            return;
        }

        // 校验全部通过后才按当前文本重注册热键：反过来的话，校验失败提前返回时
        // 旧热键已经被注销/替换，可能把原本能用的停止热键弄丢
        List<string> hotkeyProblems = RegisterHotkeys();

        if (!_engine.Start(plan, out string startError))
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

    /// <summary>把校验问题画到状态栏：待补齐的用橙色，确定不能跑的用红色。</summary>
    private void ShowProblem(UiProblem problem)
    {
        Color color = problem.Severity == UiProblemSeverity.Warning ? Theme.Orange : Theme.Red;
        SetStatus(problem.Key, color, problem.Args);
    }

    private void StopClicking()
    {
        if (_closing) return;

        // 没在运行就不要动界面：自然结束（发满次数 / 出错）由 Stopped 回调负责，
        // 这里再走一遍只会把「配置已保存」这类提示顶掉
        if (!_engine.IsRunning) return;

        EngineStopResult? result = _engine.Stop();

        // 引擎没能在 3 秒内退出：按键还在发，绝不能把界面切成“已停止”
        if (result == null)
        {
            SetStatus("status.stopping", Theme.Orange, HotkeyOffText);
            UpdateTrayTexts();
            return;
        }

        SetRunningState(false);

        // 本轮自然结束时 Stopped 回调可能已经先处理过了：判重由协调器统一负责
        RunOutcome? outcome = _runCoordinator.Complete(result, HotkeyOnText);
        if (outcome == null) return;

        ApplyRunOutcome(outcome);
    }

    private void ResetProgress()
    {
        if (_closing) return;

        CancelKeyCapture(restoreValue: true);

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

    private List<string> RegisterHotkeys()
    {
        var problems = new List<string>();
        if (_hotkeys == null) return problems;

        RegisterHotkey(HotkeyStartId, _hotkeyOnField.Value, UiText.T("label.start"), StartClicking, problems);
        RegisterHotkey(HotkeyStopId, _hotkeyOffField.Value, UiText.T("label.stop"), StopClicking, problems);
        RegisterHotkey(HotkeyResetId, _hotkeyResetField.Value, UiText.T("label.reset"), ResetProgress, problems);

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

    /// <summary>
    /// 热键没注册上时的提醒。气泡在 Win10/11 可能被「专注助手 / 通知设置」静默丢掉，
    /// 所以状态栏同时留一条红字——只靠气泡是不够的。
    /// </summary>
    private void ShowHotkeyProblem(IReadOnlyList<string> problems, string statusKey)
    {
        if (problems.Count == 0) return;

        string summary = string.Join("；", problems);
        SetStatus(statusKey, Theme.Red, summary);

        // 气泡每次运行只弹一次，免得反复点保存时一直骚扰
        if (_hotkeyProblemShown) return;
        _hotkeyProblemShown = true;

        try
        {
            _tray?.ShowBalloonTip(6000, UiText.T("hotkey.problemTitle"), summary, ToolTipIcon.Warning);
        }
        catch
        {
            // 托盘不可用时忽略：状态栏已经写明原因
        }
    }

    // ---------------------------------------------------------------- 按键捕获

    private void BeginKeyCapture(int index)
    {
        if (_closing || _engine.IsRunning || index < 0 || index >= _rows.Count) return;

        // 同一时刻只录一个键：先把上一次（如果有）收干净
        CancelKeyCapture(restoreValue: true);

        if (!_keyCapture.Start())
        {
            SetStatus("status.captureFailed", Theme.Orange, UiText.T("capture.failed"));
            return;
        }

        _captureIndex = index;
        _captureBackup = _rows[index].Key.Value;

        _miUpdate.Enabled = false;

        KeyField field = _rows[index].Key;
        field.Capturing = true;
        field.Placeholder = UiText.T("capture.hint");
        field.Value = string.Empty;

        SetStatus("status.capturing", Theme.Orange);
    }

    /// <summary>收起录键的界面状态；restoreValue 为 true 时把原来的按键内容放回去。</summary>
    private void EndKeyCapture(bool restoreValue)
    {
        if (_captureIndex < 0) return;

        if (_captureIndex >= _rows.Count)
        {
            _captureIndex = -1;
            _captureBackup = string.Empty;
            _miUpdate.Enabled = !_engine.IsRunning && !_updateBusy;
            return;
        }

        KeyField field = _rows[_captureIndex].Key;
        field.Capturing = false;
        field.Placeholder = UiText.T("row.placeholder");
        if (restoreValue) field.Value = _captureBackup;

        _captureIndex = -1;
        _captureBackup = string.Empty;
        _miUpdate.Enabled = !_engine.IsRunning && !_updateBusy;
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

        if (index < 0 || index >= _rows.Count) return;

        _rows[index].Key.Value = text;
        _rows[index].Key.Editor.SelectionStart = _rows[index].Key.Editor.TextLength;
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

        // 这一轮是否已经由 StopClicking 处理过，由协调器统一判重
        RunOutcome? outcome = _runCoordinator.Complete(result, HotkeyOnText);
        if (outcome == null) return;

        SetRunningState(false);
        ApplyRunOutcome(outcome);
    }

    /// <summary>把一轮结束的结果画到界面上（进度、摘要、提示音、状态、错误框）。</summary>
    private void ApplyRunOutcome(RunOutcome outcome)
    {
        RenderProgress(outcome.Slots);
        RefreshSummary();
        UpdateAutoSkipLabel();
        PlayStopSound();

        Color color = outcome.Severity switch
        {
            RunOutcomeSeverity.Success => Theme.Green,
            RunOutcomeSeverity.Failure => Theme.Red,
            _ => Theme.TextPrimary,
        };
        SetStatus(outcome.StatusKey, color, outcome.StatusArgs);

        if (outcome.ShowErrorDialog)
        {
            MessageBox.Show(
                this,
                outcome.ErrorMessage ?? string.Empty,
                UiText.T("dialog.title"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }

        UpdateTrayTexts();
    }

    // ---------------------------------------------------------------- 界面状态

    /// <summary>自定义顶栏已移除，运行状态放到系统标题栏。</summary>
    private void UpdateWindowTitle()
    {
        Text = UiText.T("app.name") + " · " +
            UiText.T(_engine.IsRunning ? "pill.running" : "pill.stopped");
    }

    private void SetRunningState(bool running)
    {
        UpdateWindowTitle();

        _btnStart.Enabled = !running;
        _btnStop.Enabled = running;
        _miSave.Enabled = !running;
        _miLoad.Enabled = !running;
        _btnReset.Enabled = !running;
        _miUpdate.Enabled = !running && !_updateBusy;

        foreach (SlotRow row in _rows)
        {
            row.Toggle.Enabled = !running;
            row.Key.Enabled = !running;
            row.Interval.Enabled = !running;
            row.Count.Enabled = !running;
        }
        UpdateAddRemoveButtons();

        // 运行中禁用 ToolTip，避免提示窗口出现在前台抢走按键
        _toolTip.Active = !running;

        _hotkeyOnField.Enabled = !running;
        _hotkeyOffField.Enabled = !running;
        _skipKeyField.Enabled = !running;
        _skipCountField.Enabled = !running;

        UpdateTrayTexts();
    }

    private void OnSlotControlChanged(object? sender, EventArgs e)
    {
        if (_rebuildingRows || _rows.Count == 0) return;

        SlotRow? row = FindSlotRow(sender as Control);
        if (row == null) return;

        int index = _rows.IndexOf(row);
        if (index < 0) return;

        UpdateRowVisuals(index);
        RefreshSummary();
        UpdateAutoSkipLabel();
        _cardSlots.Invalidate();
    }

    /// <summary>未勾选的行整体变浅，直观表示“这一行不会执行”。</summary>
    private void UpdateRowVisuals(int index)
    {
        if (index < 0 || index >= _rows.Count) return;

        SlotRow row = _rows[index];
        bool on = row.Toggle.Checked;
        Color surface = on ? Theme.Card : ColorTranslator.FromHtml("#FAFAFC");
        Color valueColor = on ? Theme.TextPrimary : Theme.TextTertiary;

        row.Key.SurfaceColor = surface;
        row.Interval.SurfaceColor = surface;
        row.Count.SurfaceColor = surface;

        row.Key.ValueColor = valueColor;
        row.Interval.ValueColor = valueColor;
        row.Count.ValueColor = valueColor;
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
        SetStatus("status.stopped", color, HotkeyOnText, HotkeyOffText, HotkeyResetText);
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
        var active = new List<SlotState>();

        for (int i = 0; i < slots.Count; i++)
        {
            SlotState slot = slots[i];
            bool isActive = slot.Enabled && !string.IsNullOrWhiteSpace(slot.Key);
            if (isActive) active.Add(slot);

            if (i < _rows.Count)
            {
                _rows[i].Progress.Text = slot.MaxCount > 0
                    ? slot.RunCount + "/" + slot.MaxCount
                    : (slot.RunCount > 0 ? slot.RunCount.ToString(CultureInfo.InvariantCulture) : "∞");
            }

            if (!isActive) continue;

            parts.Add(slot.MaxCount > 0
                ? UiText.T("progress.itemMax", slot.Key, slot.RunCount, slot.MaxCount, slot.Done, slot.Done + 1)
                : UiText.T("progress.itemInf", slot.Key, slot.RunCount, slot.Done));
        }

        _lblProgress.Text = parts.Count == 0 ? UiText.T("progress.empty") : string.Join("    ", parts);

        // 指标按「组合轮次」显示：Down + Enter 各 9 次 = 9 组，不是 18 次按键
        ClickSession.ComboCounts combos = ClickSession.CountCombos(active);

        _lblMetricRound.Text = combos.Infinite
            ? combos.Run.ToString(CultureInfo.InvariantCulture) + " / ∞"
            : combos.Run.ToString(CultureInfo.InvariantCulture) + " / " + combos.Max.ToString(CultureInfo.InvariantCulture);
        _lblMetricTotal.Text = combos.Done.ToString(CultureInfo.InvariantCulture);
        _lblMetricNext.Text = (combos.Done + 1).ToString(CultureInfo.InvariantCulture);
        _lblMetricSkip.Text = ClickSession.AutoSkipCount(slots).ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>“本次执行”摘要：把“哪些行会被执行、各发多少次、要跳过几项”讲清楚。</summary>
    private void RefreshSummary()
    {
        if (_lblSummary == null) return;

        List<SlotState> slots = ReadSlotsFromUi();
        List<SlotState> active = slots.Where(slot => slot.IsActive).ToList();

        bool targetReady = _targetWindow != IntPtr.Zero && WindowsDesktop.IsWindow(_targetWindow);
        string targetText = targetReady
            ? UiText.T("summary.target", ShortenTitle(_targetTitle))
            : UiText.T("summary.targetNone");
        if (_lblTarget != null)
        {
            _lblTarget.Text = UiText.T("label.target") + "：" +
                (targetReady ? ShortenTitle(_targetTitle) : UiText.T("target.none"));
        }

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

        text += " · " + targetText;

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
}

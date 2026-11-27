using System.ComponentModel;

namespace NineFlow.Wpf;

/// <summary>
/// 一行按键的界面状态。WPF 用数据绑定驱动行模板，所以这里不持有任何控件引用；
/// 原先 CreateSlotRow 里 52/236/336/394/500 这些列坐标也就不用再算。
/// </summary>
public sealed class SlotRowVm : INotifyPropertyChanged
{
    private bool _enabled;
    private string _key = string.Empty;
    private string _intervalText = "1.5";
    private string _countText = "0";
    private string _progressText = string.Empty;
    private bool _editable = true;
    private bool _canRemove = true;
    private bool _capturing;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>1 基行号，会随增删行重排。</summary>
    public int Index { get; set; }

    public string EnabledAutomationId => $"slot{Index}.enabled";
    public string KeyAutomationId => $"slot{Index}.key";
    public string IntervalAutomationId => $"slot{Index}.interval";
    public string CountAutomationId => $"slot{Index}.count";
    public string ProgressAutomationId => $"slot{Index}.progress";
    public string DeleteAutomationId => $"slot{Index}.delete";

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value) return;
            _enabled = value;
            OnPropertyChanged(nameof(Enabled));
            OnPropertyChanged(nameof(KeyDisplay));
        }
    }

    public string Key
    {
        get => _key;
        set
        {
            if (_key == value) return;
            _key = value ?? string.Empty;
            OnPropertyChanged(nameof(Key));
            OnPropertyChanged(nameof(KeyDisplay));
            OnPropertyChanged(nameof(KeyEmpty));
        }
    }

    public string IntervalText
    {
        get => _intervalText;
        set
        {
            if (_intervalText == value) return;
            _intervalText = value ?? string.Empty;
            OnPropertyChanged(nameof(IntervalText));
        }
    }

    public string CountText
    {
        get => _countText;
        set
        {
            if (_countText == value) return;
            _countText = value ?? string.Empty;
            OnPropertyChanged(nameof(CountText));
        }
    }

    public string ProgressText
    {
        get => _progressText;
        set
        {
            if (_progressText == value) return;
            _progressText = value ?? string.Empty;
            OnPropertyChanged(nameof(ProgressText));
        }
    }

    /// <summary>运行中整行只读。</summary>
    public bool Editable
    {
        get => _editable;
        set
        {
            if (_editable == value) return;
            _editable = value;
            OnPropertyChanged(nameof(Editable));
        }
    }

    /// <summary>能否删除（运行中或只剩两行时为 false）。</summary>
    public bool CanRemove
    {
        get => _canRemove;
        set
        {
            if (_canRemove == value) return;
            _canRemove = value;
            OnPropertyChanged(nameof(CanRemove));
        }
    }

    /// <summary>正在录键：输入框变红并提示按键。</summary>
    public bool Capturing
    {
        get => _capturing;
        set
        {
            if (_capturing == value) return;
            _capturing = value;
            OnPropertyChanged(nameof(Capturing));
            OnPropertyChanged(nameof(KeyDisplay));
            OnPropertyChanged(nameof(KeyEmpty));
        }
    }

    /// <summary>输入框里显示的内容：有键显示键，没键显示占位提示。</summary>
    public string KeyDisplay =>
        _capturing ? "请按键…（Esc 取消）"
        : string.IsNullOrWhiteSpace(_key) ? "点击录入按键" : _key;

    public bool KeyEmpty => string.IsNullOrWhiteSpace(_key) && !_capturing;

    private void OnPropertyChanged(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

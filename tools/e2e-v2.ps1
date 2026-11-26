# 九序 / NineFlow —— 发布版 exe 端到端测试（v2：托盘、勾选过滤、热键、置顶）
param(
    [string]$ExePath = "C:\Users\0409\Documents\NineFlow\publish\NineFlow.exe",
    [string]$KeyCatcherExe = "C:\Users\0409\Documents\NineFlow\tests\KeyCatcher\bin\Release\net10.0-windows\KeyCatcher.exe",
    [string]$WorkDir = "C:\Users\0409\Documents\NineFlow\artifacts\e2e-v2"
)

$ErrorActionPreference = "Stop"

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName WindowsBase
Add-Type -AssemblyName System.Windows.Forms

Add-Type @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

public static class E2E
{
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr hWnd, int index);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int n);
    [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint a, uint b, bool f);
    [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder sb, int max);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr lParam);

    public static void Tap(byte vk)
    {
        keybd_event(vk, 0, 0, UIntPtr.Zero);
        System.Threading.Thread.Sleep(30);
        keybd_event(vk, 0, 2, UIntPtr.Zero);
    }

    public static bool IsTopMost(IntPtr hWnd) => (GetWindowLong(hWnd, -20) & 0x00000008) != 0;

    public static int ForegroundProcessId()
    {
        uint pid;
        GetWindowThreadProcessId(GetForegroundWindow(), out pid);
        return (int)pid;
    }

    /// 找出某个进程的可见主窗口（标题非空、面积最大）
    public static IntPtr FindMainWindow(int processId)
    {
        IntPtr best = IntPtr.Zero;
        int bestArea = 0;
        EnumWindows((h, l) => {
            uint pid;
            GetWindowThreadProcessId(h, out pid);
            if (pid != (uint)processId) return true;
            if (!IsWindowVisible(h)) return true;
            var sb = new StringBuilder(256);
            GetWindowText(h, sb, 256);
            if (sb.Length == 0) return true;
            RECT r;
            if (!GetWindowRect(h, out r)) return true;
            int area = (r.Right - r.Left) * (r.Bottom - r.Top);
            if (area > bestArea) { bestArea = area; best = h; }
            return true;
        }, IntPtr.Zero);
        return best;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);

    /// 强制把窗口切到前台（先 ALT 一下，再用 AttachThreadInput 兜底）
    public static bool ForceForeground(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return false;
        ShowWindow(hwnd, 9);
        keybd_event(0x12, 0, 0, UIntPtr.Zero);
        keybd_event(0x12, 0, 2, UIntPtr.Zero);
        SetForegroundWindow(hwnd);
        System.Threading.Thread.Sleep(200);
        if (GetForegroundWindow() == hwnd) return true;

        uint fgThread = GetWindowThreadProcessId(GetForegroundWindow(), out _);
        uint cur = GetCurrentThreadId();
        bool attached = AttachThreadInput(cur, fgThread, true);
        try
        {
            BringWindowToTop(hwnd);
            SetForegroundWindow(hwnd);
            System.Threading.Thread.Sleep(200);
        }
        finally
        {
            if (attached) AttachThreadInput(cur, fgThread, false);
        }
        return GetForegroundWindow() == hwnd;
    }
}
"@

$script:pass = 0
$script:fail = 0

function Check([string]$name, [bool]$condition, [string]$detail = "") {
    if ($condition) { $script:pass++; Write-Output "  ok   $name" }
    else { $script:fail++; Write-Output "  FAIL $name $detail" }
}

function Stop-All {
    Get-Process -Name KeyCatcher -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Get-Process -Name NineFlow -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Get-CimInstance Win32_Process |
        Where-Object { $_.Name -eq "dotnet.exe" -and $_.CommandLine -like "*NineFlow.dll*" -and $_.ProcessId -ne $PID } |
        ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
    Start-Sleep -Milliseconds 800
}

function Find-ById($window, [string]$automationId) {
    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty, $automationId)
    return $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
}

function Get-Text($window, [string]$automationId) {
    $element = Find-ById $window $automationId
    if (-not $element) { return "<missing>" }
    return $element.Current.Name
}

function Invoke-Button($window, [string]$automationId) {
    $element = Find-ById $window $automationId
    if (-not $element) { throw "找不到按钮 $automationId" }
    $element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}

function Open-SettingsMenu($window) {
    Invoke-Button $window "button.more"
    Start-Sleep -Milliseconds 350
}

function Open-SwitchMenu($window, [string]$switchId) {
    Open-SettingsMenu $window
    Invoke-Button $window ($switchId + ".group")
    Start-Sleep -Milliseconds 350
}

function Get-MainWindow([int]$timeoutSeconds = 15) {
    $deadline = (Get-Date).AddSeconds($timeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        $root = [System.Windows.Automation.AutomationElement]::RootElement
        $windows = $root.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)
        foreach ($w in $windows) {
            if ($w.Current.Name -ne "九序") { continue }
            if (Find-ById $w "status.state") { return $w }
        }
        Start-Sleep -Milliseconds 200
    }
    return $null
}

function Wait-Status($window, [string]$needle, [int]$timeoutSeconds = 8) {
    $deadline = (Get-Date).AddSeconds($timeoutSeconds)
    $last = ""
    while ((Get-Date) -lt $deadline) {
        if (-not (Find-ById $window "status.state")) { continue }
        $last = Get-Text $window "status.state"
        if ($last -like "*$needle*") { return $last }
        Start-Sleep -Milliseconds 100
    }
    return $last
}

function Read-IniValue([string]$path, [string]$section, [string]$key) {
    if (-not (Test-Path $path)) { return $null }
    $current = ""
    foreach ($line in Get-Content $path) {
        if ($line -match '^\s*\[(.+?)\]\s*$') { $current = $Matches[1]; continue }
        if ($current -ne $section) { continue }
        if ($line -match ('^\s*' + [regex]::Escape($key) + '\s*=(.*)$')) { return $Matches[1].Trim() }
    }
    return $null
}

# rows: 数组，每项 @{ Enabled=1; Key="j"; Interval=0.01; Max=3 }
function Write-TestIni([string]$path, $rows, [string]$hotkeyOn = "F7", [string]$hotkeyOff = "F8", [string]$skipCount = "", [string]$skipKey = "Down", [int]$closeToTray = 1) {
    $lines = @()
    for ($i = 1; $i -le [Math]::Max(2, $rows.Count); $i++) {
        $row = if ($i -le $rows.Count) { $rows[$i - 1] } else { @{ Enabled = 0; Key = ""; Interval = 1.5; Max = 0 } }
        $lines += "[Slot$i]"
        $lines += "Enabled=$($row.Enabled)"
        $lines += "Key=$($row.Key)"
        $lines += "Interval=$($row.Interval)"
        $lines += "MaxCount=$($row.Max)"
        $lines += "Done=0"
        $lines += ""
    }
    $lines += "[Hotkey]"
    $lines += "On=$hotkeyOn"
    $lines += "Off=$hotkeyOff"
    $lines += ""
    $lines += "[Skip]"
    $lines += "Key=$skipKey"
    $lines += "Count=$skipCount"
    $lines += ""
    $lines += "[General]"
    $lines += "CloseToTray=$closeToTray"
    $lines += "Beep=0"
    $lines += "TopMost=0"
    $lines += ""
    $lines += "[Progress]"
    $lines += "LastSkip=0"
    $lines += "SkipKey=$skipKey"
    [System.IO.File]::WriteAllText($path, ($lines -join "`r`n"), (New-Object System.Text.UTF8Encoding($false)))
}

if (-not (Test-Path $WorkDir)) { New-Item -ItemType Directory -Force -Path $WorkDir | Out-Null }
$publishDir = Split-Path -Parent $ExePath
$iniPath = Join-Path $publishDir "NineFlow.ini"

Write-Output "== 准备：清空进程与配置 =="
Stop-All
Remove-Item $iniPath -ErrorAction SilentlyContinue
Check "被测 exe 存在" (Test-Path $ExePath)
Check "按键捕获工具存在" (Test-Path $KeyCatcherExe)

# ------------------------------------------------------------------ A. 名称与界面
Write-Output ""
Write-Output "== A. 窗口名称与自动化元素 =="
Write-TestIni $iniPath @(@{ Enabled = 1; Key = "Down"; Interval = 0.02; Max = 9 }, @{ Enabled = 1; Key = "Enter"; Interval = 0.02; Max = 9 })
$proc = Start-Process -FilePath $ExePath -PassThru
$window = Get-MainWindow
Check "主窗口出现" ($window -ne $null)
if ($window) {
    Check "窗口标题含中文名与状态" ($window.Current.Name -like "九序 · *")
    Check "存在开始按钮" ((Find-ById $window "button.start") -ne $null)
    Check "存在停止按钮" ((Find-ById $window "button.stop") -ne $null)
    Check "存在重置按钮" ((Find-ById $window "button.reset") -ne $null)
    Open-SettingsMenu $window
    Check "菜单里有保存按钮" ((Find-ById $window "button.save") -ne $null)
    $slotSwitches = (1..20 | Where-Object { Find-ById $window "slot$_.enabled" } | Measure-Object).Count
    Check "至少存在 2 个行开关" ($slotSwitches -ge 2) "实际 $slotSwitches 个"
    Check "存在添加行按钮" ((Find-ById $window "button.addSlot") -ne $null)
    foreach ($id in @("switch.beep", "switch.closeToTray", "switch.topMost", "switch.autoUpdate")) {
        Check "菜单里存在一级项 $id" ((Find-ById $window ($id + ".group")) -ne $null)
    }
    Open-SwitchMenu $window "switch.beep"
    Check "提示音二级菜单里有开关" ((Find-ById $window "switch.beep") -ne $null)
    $summary = Get-Text $window "status.summary"
    Check "本次执行摘要显示 2 行" ($summary -like "*共 2 行*") "实际: $summary"
}
Stop-All

# ------------------------------------------------------------------ B. 只执行勾选的行
Write-Output ""
Write-Output "== B. 只执行勾选的行（真实按键验证）=="
Write-TestIni $iniPath @(
    @{ Enabled = 1; Key = "j"; Interval = 0.01; Max = 3 },
    @{ Enabled = 0; Key = "k"; Interval = 0.01; Max = 3 },
    @{ Enabled = 1; Key = ""; Interval = 0.01; Max = 3 }
)
$proc = Start-Process -FilePath $ExePath -PassThru
$window = Get-MainWindow
Check "B 窗口出现" ($window -ne $null)

$logPath = Join-Path $WorkDir "keys.log"
Remove-Item $logPath -ErrorAction SilentlyContinue
$catcher = Start-Process -FilePath $KeyCatcherExe -ArgumentList "`"$logPath`"" -PassThru
Start-Sleep -Seconds 2
Check "按键捕获窗口就绪" ((Test-Path $logPath) -and ((Get-Content $logPath -Raw) -like "*READY*"))

# 把捕获窗口真正切到前台，并等九序记录下这个目标窗口
$catcherHwnd = [E2E]::FindMainWindow($catcher.Id)
$forced = [E2E]::ForceForeground($catcherHwnd)
Start-Sleep -Milliseconds 1200
Check "按键捕获窗口在前台" ($forced -and ([E2E]::ForegroundProcessId() -eq $catcher.Id)) "前台进程 $([E2E]::ForegroundProcessId())，捕获进程 $($catcher.Id)"
# 切前台时按过的 ALT 也会被钩子记录，这里清空日志只保留后续按键
Set-Content -Path $logPath -Value "READY" -NoNewline -Encoding utf8

if ($window) {
    $summary = Get-Text $window "status.summary"
    Check "摘要只列出勾选且有按键的行" ($summary -like "*j ×3*" -and $summary -notlike "*k ×3*") "实际: $summary"
    Check "摘要提示有空按键的勾选行" ($summary -like "*第 3 行*") "实际: $summary"

    Invoke-Button $window "button.start"
    $status = Wait-Status $window "已完成"
    Check "本轮完成" ($status -like "*已完成*") "实际: $status"
    Start-Sleep -Milliseconds 600

    $captured = if (Test-Path $logPath) { Get-Content $logPath -Raw } else { "" }
    $jCount = ([regex]::Matches($captured, "vk=74;")).Count   # 'j' = 0x4A
    $kCount = ([regex]::Matches($captured, "vk=75;")).Count   # 'k' = 0x4B
    Check "勾选行发送了 3 次 j" ($jCount -eq 3) "实际 $jCount 次，内容: [$captured]"
    Check "未勾选行一次都没发送" ($kCount -eq 0) "实际 $kCount 次"
    Check "没有多余的按键" (([regex]::Matches($captured, "vk=")).Count -eq 3) "内容: [$captured]"

    Check "进度只统计勾选行" ((Read-IniValue $iniPath "Slot1" "Done") -eq "3")
    Check "未勾选行进度保持 0" ((Read-IniValue $iniPath "Slot2" "Done") -eq "0")
    Check "空按键行进度保持 0" ((Read-IniValue $iniPath "Slot3" "Done") -eq "0")
}

Get-Process -Name KeyCatcher -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Stop-All

# ------------------------------------------------------------------ C. 托盘常驻 + 后台热键
Write-Output ""
Write-Output "== C. 关闭窗口后仍在后台运行，热键继续可用 =="
Write-TestIni $iniPath @(@{ Enabled = 1; Key = "F24"; Interval = 0.01; Max = 2 })
$proc = Start-Process -FilePath $ExePath -PassThru
$window = Get-MainWindow
Check "C 窗口出现" ($window -ne $null)

if ($window) {
    $window.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
    Start-Sleep -Milliseconds 1200

    $proc.Refresh()
    Check "点关闭后进程没有退出" (-not $proc.HasExited)
    $stillVisible = Get-MainWindow -timeoutSeconds 2
    Check "窗口已隐藏到托盘" ($stillVisible -eq $null)

    # 窗口隐藏状态下按 F7，应该照常执行
    [E2E]::Tap(0x76)   # F7
    Start-Sleep -Milliseconds 1500
    $done = Read-IniValue $iniPath "Slot1" "Done"
    Check "隐藏到托盘后 F7 仍能连点（进度已保存）" ([int]$done -eq 2) "实际: $done"

    # 再启动一个实例：应该把已有窗口唤出来
    $second = Start-Process -FilePath $ExePath -PassThru
    $restored = Get-MainWindow -timeoutSeconds 8
    Check "再次双击图标能唤出已隐藏的窗口" ($restored -ne $null)
    Start-Sleep -Milliseconds 1500
    $second.Refresh()
    Check "第二个实例自动退出" ($second.HasExited)
    Check "原实例仍在运行" (-not $proc.HasExited)
}
Stop-All

# ------------------------------------------------------------------ D. 窗口置顶开关
Write-Output ""
Write-Output "== D. 窗口置顶开关 =="
Write-TestIni $iniPath @(@{ Enabled = 1; Key = "F24"; Interval = 0.01; Max = 1 })
$proc = Start-Process -FilePath $ExePath -PassThru
$window = Get-MainWindow
Check "D 窗口出现" ($window -ne $null)

if ($window) {
    $hwnd = [IntPtr]$window.Current.NativeWindowHandle
    Check "默认不置顶" (-not [E2E]::IsTopMost($hwnd))

    Open-SwitchMenu $window "switch.topMost"
    Invoke-Button $window "switch.topMost"
    Start-Sleep -Milliseconds 600
    Check "打开置顶后窗口为置顶状态" ([E2E]::IsTopMost($hwnd))

    Open-SwitchMenu $window "switch.topMost"
    Invoke-Button $window "switch.topMost"
    Start-Sleep -Milliseconds 600
    Check "关闭置顶后恢复普通状态" (-not [E2E]::IsTopMost($hwnd))
}
Stop-All

# ------------------------------------------------------------------ E. 热键 F7/F8
Write-Output ""
Write-Output "== E. F7 开始 / F8 停止 =="
Write-TestIni $iniPath @(@{ Enabled = 1; Key = "F24"; Interval = 0.05; Max = 0 })
$proc = Start-Process -FilePath $ExePath -PassThru
$window = Get-MainWindow
Check "E 窗口出现" ($window -ne $null)

if ($window) {
    [E2E]::Tap(0x76)   # F7
    $status = Wait-Status $window "运行中"
    Check "F7 开始运行" ($status -like "*运行中*") "实际: $status"

    Start-Sleep -Milliseconds 800
    [E2E]::Tap(0x77)   # F8
    $status = Wait-Status $window "已停止"
    Check "F8 停止运行" ($status -like "*已停止*") "实际: $status"
    Check "停止后进度已保存" ([int](Read-IniValue $iniPath "Slot1" "Done") -gt 0)
}
Stop-All

# ------------------------------------------------------------------ F. 关闭保存进度（关闭到托盘=关闭）
Write-Output ""
Write-Output "== F. 关闭到托盘开关关闭时，关闭窗口会保存进度并退出 =="
Write-TestIni $iniPath @(@{ Enabled = 1; Key = "F24"; Interval = 0.02; Max = 0 }) -closeToTray 0
$proc = Start-Process -FilePath $ExePath -PassThru
$window = Get-MainWindow
Check "F 窗口出现" ($window -ne $null)

if ($window) {
    Invoke-Button $window "button.start"
    Start-Sleep -Milliseconds 900
    $window.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
    Start-Sleep -Milliseconds 1500

    $proc.Refresh()
    Check "关闭后进程退出" ($proc.HasExited)
    Check "退出前保存了进度" ([int](Read-IniValue $iniPath "Slot1" "Done") -gt 0)
}
Stop-All
Remove-Item $iniPath -ErrorAction SilentlyContinue

Write-Output ""
Write-Output "结果：通过 $script:pass 项，失败 $script:fail 项"
if ($script:fail -gt 0) { exit 1 }
exit 0

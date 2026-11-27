param(
    [string]$ExePath = "",
    [string]$KeyCatcherExe = "",
    [string]$WorkDir = ""
)

$ErrorActionPreference = "Stop"
trap {
    Write-Output "ERROR: $($_.Exception.Message)"
    Write-Output $_.ScriptStackTrace
    exit 1
}
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [Console]::OutputEncoding
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "WPF e2e 必须与 requireAdministrator 的应用同权限运行；请使用管理员 PowerShell。"
}
$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($ExePath)) { $ExePath = Join-Path $repoRoot "publish\NineFlow.exe" }
if ([string]::IsNullOrWhiteSpace($KeyCatcherExe)) { $KeyCatcherExe = Join-Path $repoRoot "tests\KeyCatcher\bin\Release\net10.0-windows\KeyCatcher.exe" }
if ([string]::IsNullOrWhiteSpace($WorkDir)) { $WorkDir = Join-Path $repoRoot "artifacts\e2e-v2" }

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName WindowsBase
Add-Type -AssemblyName System.Windows.Forms

Add-Type @"
using System;
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
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int Left, Top, Right, Bottom; }

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
    Get-Process -Name "九序" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Get-CimInstance Win32_Process |
        Where-Object { $_.Name -eq "dotnet.exe" -and $_.CommandLine -like "*NineFlow.dll*" -and $_.ProcessId -ne $PID } |
        ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
    Start-Sleep -Milliseconds 800
}

function Find-ById($root, [string]$automationId) {
    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty, $automationId)
    return $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
}

function Find-GlobalById([string]$automationId) {
    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty, $automationId)
    return [System.Windows.Automation.AutomationElement]::RootElement.FindFirst(
        [System.Windows.Automation.TreeScope]::Subtree, $condition)
}

function Get-Text($root, [string]$automationId) {
    $element = Find-ById $root $automationId
    if (-not $element) { return "<missing>" }
    return $element.Current.Name
}

function Invoke-Element($element) {
    if (-not $element) { throw "自动化元素不存在" }
    $pattern = $null
    if ($element.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$pattern)) {
        $pattern.Invoke()
        return
    }
    if ($element.TryGetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern, [ref]$pattern)) {
        $pattern.Toggle()
        return
    }
    throw "元素不支持 Invoke/Toggle：$($element.Current.AutomationId) $($element.Current.Name)"
}

function Invoke-Button($window, [string]$automationId) {
    Invoke-Element (Find-ById $window $automationId)
}

function Wait-Until([scriptblock]$condition, [int]$timeoutSeconds = 8) {
    $deadline = (Get-Date).AddSeconds($timeoutSeconds)
    do {
        if (& $condition) { return $true }
        Start-Sleep -Milliseconds 100
    } while ((Get-Date) -lt $deadline)
    return $false
}

function Get-MainWindow([int]$timeoutSeconds = 15) {
    $ready = Wait-Until {
        $root = [System.Windows.Automation.AutomationElement]::RootElement
        $windows = $root.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)
        foreach ($w in $windows) {
            if ($w.Current.Name -like "九序 · *" -and (Find-ById $w "status.state")) { return $w }
        }
        return $null
    } $timeoutSeconds
    if (-not $ready) { return $null }
    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $windows = $root.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)
    foreach ($w in $windows) {
        if ($w.Current.Name -like "九序 · *" -and (Find-ById $w "status.state")) { return $w }
    }
    return $null
}

function Open-SettingsMenu($window) {
    Invoke-Button $window "button.more"
    $null = Wait-Until { Find-GlobalById "menu.save" } 4
}

function Close-SettingsMenu {
    [System.Windows.Forms.SendKeys]::SendWait("{ESC}")
    Start-Sleep -Milliseconds 250
}

function Wait-Status($window, [string]$needle, [int]$timeoutSeconds = 8) {
    $deadline = (Get-Date).AddSeconds($timeoutSeconds)
    $last = ""
    do {
        $last = Get-Text $window "status.state"
        if ($last -like "*$needle*") { return $last }
        Start-Sleep -Milliseconds 100
    } while ((Get-Date) -lt $deadline)
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

function Start-WithHotkey([string]$iniPath, [int]$minimumDone = 1) {
    [E2E]::Tap(0x76)
    $started = Wait-Until { [int](Read-IniValue $iniPath "Slot1" "Done") -ge $minimumDone } 3
    if (-not $started) {
        Start-Sleep -Milliseconds 300
        [E2E]::Tap(0x76)
        $started = Wait-Until { [int](Read-IniValue $iniPath "Slot1" "Done") -ge $minimumDone } 4
    }
    return $started
}

function Write-TestIni([string]$path, $rows, [string]$hotkeyOn = "F7", [string]$hotkeyOff = "F8", [string]$hotkeyReset = "F9", [string]$skipCount = "", [string]$skipKey = "Down", [int]$closeToTray = 1) {
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
    $lines += "Reset=$hotkeyReset"
    $lines += ""
    $lines += "[Skip]"
    $lines += "Key=$skipKey"
    $lines += "Count=$skipCount"
    $lines += ""
    $lines += "[General]"
    $lines += "CloseToTray=$closeToTray"
    $lines += "Beep=0"
    $lines += "TopMost=0"
    $lines += "AutoCheckUpdate=0"
    $lines += "Diagnostics=0"
    $lines += ""
    $lines += "[App]"
    $lines += "ShortcutCreated=1"
    $lines += ""
    [System.IO.File]::WriteAllText($path, ($lines -join "`r`n"), (New-Object System.Text.UTF8Encoding($false)))
}

function Start-NineFlow {
    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $ExePath
    $startInfo.WorkingDirectory = $publishDir
    $startInfo.UseShellExecute = $false
    $process = [System.Diagnostics.Process]::Start($startInfo)
    if (-not $process) { throw "无法启动 $ExePath" }
    return $process
}

function Start-KeyCatcher([string]$logPath) {
    Remove-Item $logPath -ErrorAction SilentlyContinue
    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $KeyCatcherExe
    $startInfo.Arguments = "`"$logPath`""
    $startInfo.WorkingDirectory = $WorkDir
    $startInfo.UseShellExecute = $false
    $process = [System.Diagnostics.Process]::Start($startInfo)
    if (-not $process) { throw "无法启动 KeyCatcher" }
    $ready = Wait-Until { (Test-Path $logPath) -and ((Get-Content $logPath -Raw) -like "*READY*") } 10
    return @{ Process = $process; LogPath = $logPath; Ready = $ready }
}

function Activate-Target($catcher) {
    $catcher.Process.Refresh()
    if ($catcher.Process.HasExited) { return $false }
    $hwnd = [E2E]::FindMainWindow($catcher.Process.Id)
    for ($attempt = 0; $attempt -lt 3; $attempt++) {
        $forced = [E2E]::ForceForeground($hwnd)
        Start-Sleep -Milliseconds 600
        if ($forced -and ([E2E]::ForegroundProcessId() -eq $catcher.Process.Id)) { return $true }
    }
    return $false
}

if (-not (Test-Path $WorkDir)) { New-Item -ItemType Directory -Force -Path $WorkDir | Out-Null }
$publishDir = Split-Path -Parent $ExePath
$iniPath = Join-Path $publishDir "NineFlow.ini"

Write-Output "== 准备：清空进程与配置 =="
Stop-All
Remove-Item $iniPath -ErrorAction SilentlyContinue
Check "被测 exe 存在" (Test-Path $ExePath)
Check "按键捕获工具存在" (Test-Path $KeyCatcherExe)
if (-not (Test-Path $ExePath)) { throw "找不到被测 exe：$ExePath" }
if (-not (Test-Path $KeyCatcherExe)) { throw "找不到 KeyCatcher：$KeyCatcherExe" }

try {
    Write-Output ""
    Write-Output "== A. 窗口名称与自动化元素 =="
    Write-TestIni $iniPath @(@{ Enabled = 1; Key = "Down"; Interval = 0.02; Max = 9 }, @{ Enabled = 1; Key = "Enter"; Interval = 0.02; Max = 9 })
    $proc = Start-NineFlow
    $window = Get-MainWindow
    Check "主窗口出现" ($window -ne $null)
    if ($window) {
        Check "窗口标题含中文名与状态" ($window.Current.Name -like "九序 · *")
        $start = Find-ById $window "button.start"
        Check "存在开始按钮" ($start -ne $null)
        Check "开始按钮初始文案正确" ($start.Current.Name -eq "开始连点")
        Check "存在重置按钮" ((Find-ById $window "button.reset") -ne $null)
        Check "存在第一行开关" ((Find-ById $window "slot1.enabled") -ne $null)
        Check "存在第二行开关" ((Find-ById $window "slot2.enabled") -ne $null)
        Check "存在第一行间隔输入" ((Find-ById $window "slot1.interval") -ne $null)
        Check "存在第一行次数输入" ((Find-ById $window "slot1.count") -ne $null)
        Check "存在添加行按钮" ((Find-ById $window "button.addSlot") -ne $null)
        Check "存在设置按钮" ((Find-ById $window "button.more") -ne $null)
        $summary = $start.Current.HelpText
        Check "执行摘要可被自动化读取" ($summary -like "*共 2 行*") "实际: $summary"

        Open-SettingsMenu $window
        foreach ($id in @("menu.save", "menu.load", "menu.update", "menu.beep", "menu.closeToTray", "menu.topMost", "menu.autoUpdate", "menu.diagnostics", "menu.openLog")) {
            Check "菜单项 $id 存在" ((Find-GlobalById $id) -ne $null)
        }
        Close-SettingsMenu
    }
    Stop-All

    Write-Output ""
    Write-Output "== B. 只执行勾选的行（真实按键验证）=="
    Write-TestIni $iniPath @(
        @{ Enabled = 1; Key = "j"; Interval = 0.01; Max = 3 },
        @{ Enabled = 0; Key = "k"; Interval = 0.01; Max = 3 },
        @{ Enabled = 1; Key = ""; Interval = 0.01; Max = 3 }
    )
    $proc = Start-NineFlow
    $window = Get-MainWindow
    Check "B 主窗口出现" ($window -ne $null)
    $hwnd = if ($window) { [IntPtr]$window.Current.NativeWindowHandle } else { [IntPtr]::Zero }
    $start = if ($window) { Find-ById $window "button.start" } else { $null }
    $summary = if ($start) { $start.Current.HelpText } else { "" }
    Check "摘要只列出勾选且有按键的行" ($summary -like "*j ×3*" -and $summary -notlike "*k ×3*") "实际: $summary"
    Check "摘要提示有空按键的勾选行" ($summary -like "*第 3 行*") "实际: $summary"

    $logPath = Join-Path $WorkDir "keys.log"
    $catcher = Start-KeyCatcher $logPath
    Check "按键捕获工具就绪" ($catcher.Ready)
    $targetReady = Activate-Target $catcher
    Check "按键捕获窗口成为目标窗口" $targetReady
    Set-Content -Path $logPath -Value "READY" -NoNewline -Encoding utf8
    if ($hwnd -ne [IntPtr]::Zero) { $window = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd) }
    Check "B 后台窗口句柄有效" ($window -ne $null)

    if ($window) {
        [E2E]::Tap(0x76)
        $status = Wait-Status $window "已完成"
        Check "本轮完成" ($status -like "*已完成*") "实际: $status"
        Start-Sleep -Milliseconds 600

        $captured = if (Test-Path $logPath) { Get-Content $logPath -Raw } else { "" }
        $jCount = ([regex]::Matches($captured, "vk=74;")).Count
        $kCount = ([regex]::Matches($captured, "vk=75;")).Count
        $f7Count = ([regex]::Matches($captured, "vk=118;")).Count
        Check "勾选行发送了 3 次 j" ($jCount -eq 3) "实际 $jCount 次，内容: [$captured]"
        Check "未勾选行一次都没发送" ($kCount -eq 0) "实际 $kCount 次"
        Check "除启动热键外没有多余的按键" ((([regex]::Matches($captured, "vk=")).Count - $f7Count) -eq 3) "内容: [$captured]"
        Check "进度只统计勾选行" ((Read-IniValue $iniPath "Slot1" "Done") -eq "3")
        Check "未勾选行进度保持 0" ((Read-IniValue $iniPath "Slot2" "Done") -eq "0")
        Check "空按键行进度保持 0" ((Read-IniValue $iniPath "Slot3" "Done") -eq "0")
    }
    Stop-All

    Write-Output ""
    Write-Output "== C. 托盘常驻、后台热键与单实例 =="
    Write-TestIni $iniPath @(@{ Enabled = 1; Key = "F24"; Interval = 0.01; Max = 2 })
    $proc = Start-NineFlow
    $window = Get-MainWindow
    Check "C 主窗口出现" ($window -ne $null)
    $hwnd = if ($window) { [IntPtr]$window.Current.NativeWindowHandle } else { [IntPtr]::Zero }
    $catcher = Start-KeyCatcher (Join-Path $WorkDir "target-c.log")
    Check "C 目标窗口就绪" ($catcher.Ready)
    Check "C 目标窗口已激活" (Activate-Target $catcher)
    if ($hwnd -ne [IntPtr]::Zero) { $window = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd) }
    Check "C 后台窗口句柄有效" ($window -ne $null)

    if ($window) {
        $window.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
        Start-Sleep -Milliseconds 1200
        $proc.Refresh()
        Check "点关闭后进程没有退出" (-not $proc.HasExited)
        Check "窗口已隐藏到托盘" ((Get-MainWindow -timeoutSeconds 2) -eq $null)

        [E2E]::Tap(0x76)
        $saved = Wait-Until { (Read-IniValue $iniPath "Slot1" "Done") -eq "2" } 6
        Check "隐藏到托盘后 F7 仍能连点" $saved "实际: $(Read-IniValue $iniPath 'Slot1' 'Done')"

        $second = Start-NineFlow
        $restored = Get-MainWindow -timeoutSeconds 8
        Check "再次启动能唤出已隐藏的窗口" ($restored -ne $null)
        Start-Sleep -Milliseconds 1200
        $second.Refresh()
        Check "第二个实例自动退出" ($second.HasExited)
        $proc.Refresh()
        Check "原实例仍在运行" (-not $proc.HasExited)
    }
    Stop-All

    Write-Output ""
    Write-Output "== D. 窗口置顶开关 =="
    Write-TestIni $iniPath @(@{ Enabled = 1; Key = "F24"; Interval = 0.01; Max = 1 })
    $proc = Start-NineFlow
    $window = Get-MainWindow
    Check "D 主窗口出现" ($window -ne $null)

    if ($window) {
        $hwnd = [IntPtr]$window.Current.NativeWindowHandle
        Check "默认不置顶" (-not [E2E]::IsTopMost($hwnd))
        Open-SettingsMenu $window
        Invoke-Element (Find-GlobalById "menu.topMost")
        $topMostOn = Wait-Until { [E2E]::IsTopMost($hwnd) } 4
        Check "打开置顶后窗口为置顶状态" $topMostOn

        Open-SettingsMenu $window
        Invoke-Element (Find-GlobalById "menu.topMost")
        $topMostOff = Wait-Until { -not [E2E]::IsTopMost($hwnd) } 4
        Check "关闭置顶后恢复普通状态" $topMostOff
    }
    Stop-All

    Write-Output ""
    Write-Output "== E. F7 开始 / F8 停止 =="
    Write-TestIni $iniPath @(@{ Enabled = 1; Key = "F24"; Interval = 0.05; Max = 0 })
    $proc = Start-NineFlow
    $window = Get-MainWindow
    Check "E 主窗口出现" ($window -ne $null)
    $hwnd = if ($window) { [IntPtr]$window.Current.NativeWindowHandle } else { [IntPtr]::Zero }
    $catcher = Start-KeyCatcher (Join-Path $WorkDir "target-e.log")
    Check "E 目标窗口就绪" ($catcher.Ready)
    Check "E 目标窗口已激活" (Activate-Target $catcher)
    if ($hwnd -ne [IntPtr]::Zero) { $window = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd) }
    Check "E 后台窗口句柄有效" ($window -ne $null)

    if ($window) {
        [E2E]::Tap(0x76)
        Start-Sleep -Milliseconds 300
        [E2E]::Tap(0x76)
        Start-Sleep -Milliseconds 800
        [E2E]::Tap(0x77)
        $saved = Wait-Until { [int](Read-IniValue $iniPath "Slot1" "Done") -gt 0 } 6
        $captured = if (Test-Path $catcher.LogPath) { Get-Content $catcher.LogPath -Raw } else { "" }
        $sentCount = ([regex]::Matches($captured, "vk=135;")).Count
        Check "F7 开始运行" ($sentCount -gt 0) "实际发送 F24 $sentCount 次"
        Check "F8 停止运行" ($saved -and $sentCount -gt 0) "实际进度: $(Read-IniValue $iniPath 'Slot1' 'Done')"
        Start-Sleep -Milliseconds 500
        $capturedAfter = if (Test-Path $catcher.LogPath) { Get-Content $catcher.LogPath -Raw } else { "" }
        $afterCount = ([regex]::Matches($capturedAfter, "vk=135;")).Count
        Check "停止后不再发送" ($afterCount -eq $sentCount) "停止前 $sentCount 次，停止后 $afterCount 次"
        Check "停止后进度已保存" $saved
    }
    Stop-All

    Write-Output ""
    Write-Output "== F. 关闭窗口时保存进度并退出 =="
    Write-TestIni $iniPath @(@{ Enabled = 1; Key = "F24"; Interval = 0.02; Max = 0 }) -closeToTray 0
    $proc = Start-NineFlow
    $window = Get-MainWindow
    Check "F 主窗口出现" ($window -ne $null)
    $hwnd = if ($window) { [IntPtr]$window.Current.NativeWindowHandle } else { [IntPtr]::Zero }
    $catcher = Start-KeyCatcher (Join-Path $WorkDir "target-f.log")
    Check "F 目标窗口就绪" ($catcher.Ready)
    Check "F 目标窗口已激活" (Activate-Target $catcher)
    if ($hwnd -ne [IntPtr]::Zero) { $window = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd) }
    Check "F 后台窗口句柄有效" ($window -ne $null)

    if ($window) {
        [E2E]::Tap(0x76)
        Start-Sleep -Milliseconds 300
        [E2E]::Tap(0x76)
        Start-Sleep -Milliseconds 900
        $window.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
        $exited = Wait-Until { $proc.HasExited } 6
        Check "关闭后进程退出" $exited
        Check "退出前保存了进度" ([int](Read-IniValue $iniPath "Slot1" "Done") -gt 0)
    }
    Stop-All

    Write-Output ""
    Write-Output "== G. F9 重置进度 =="
    Write-TestIni $iniPath @(@{ Enabled = 1; Key = "F24"; Interval = 0.01; Max = 1 })
    $proc = Start-NineFlow
    $window = Get-MainWindow
    Check "G 主窗口出现" ($window -ne $null)
    $hwnd = if ($window) { [IntPtr]$window.Current.NativeWindowHandle } else { [IntPtr]::Zero }
    $catcher = Start-KeyCatcher (Join-Path $WorkDir "target-g.log")
    Check "G 目标窗口就绪" ($catcher.Ready)
    Check "G 目标窗口已激活" (Activate-Target $catcher)
    if ($hwnd -ne [IntPtr]::Zero) { $window = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd) }
    Check "G 后台窗口句柄有效" ($window -ne $null)

    if ($window) {
        $started = Start-WithHotkey $iniPath
        Check "G 本轮完成" ($started -and ((Read-IniValue $iniPath "Slot1" "Done") -eq "1")) "实际进度: $(Read-IniValue $iniPath 'Slot1' 'Done')"
        Check "G 完成前进度为 1" ((Read-IniValue $iniPath "Slot1" "Done") -eq "1")
        [E2E]::Tap(0x78)
        $reset = Wait-Until { (Read-IniValue $iniPath "Slot1" "Done") -eq "0" } 5
        Check "F9 重置进度" $reset "实际进度: $(Read-IniValue $iniPath 'Slot1' 'Done')"
        Check "G 重置后进度为 0" ((Read-IniValue $iniPath "Slot1" "Done") -eq "0")
    }
    Stop-All

    Write-Output ""
    Write-Output "== H. 添加与删除行 =="
    Write-TestIni $iniPath @(@{ Enabled = 1; Key = "F24"; Interval = 0.01; Max = 1 }, @{ Enabled = 0; Key = ""; Interval = 1.5; Max = 0 })
    $proc = Start-NineFlow
    $window = Get-MainWindow
    Check "H 主窗口出现" ($window -ne $null)
    if ($window) {
        Invoke-Button $window "button.addSlot"
        $added = Wait-Until { Find-ById $window "slot3.enabled" } 4
        Check "添加一行后出现第三行" $added
        Check "第三行删除按钮存在" ((Find-ById $window "slot3.delete") -ne $null)
        Invoke-Button $window "slot3.delete"
        $removed = Wait-Until { -not (Find-ById $window "slot3.enabled") } 4
        Check "删除第三行后控件消失" $removed
    }
}
finally {
    Stop-All
    Remove-Item $iniPath -ErrorAction SilentlyContinue
}

Write-Output ""
Write-Output "结果：通过 $script:pass 项，失败 $script:fail 项"
if ($script:fail -gt 0) { exit 1 }
exit 0

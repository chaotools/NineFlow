$ErrorActionPreference = "Continue"
$out = "C:\Users\0409\Documents\NineFlow\artifacts\repro-result.txt"
$shotDir = "C:\Users\0409\Documents\NineFlow\artifacts\repro"
Remove-Item $out -ErrorAction SilentlyContinue
Remove-Item $shotDir -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $shotDir | Out-Null

function Log([string]$t) { Add-Content -Path $out -Value $t -Encoding utf8 }

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, WindowsBase
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class RP {
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int n);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
}
"@

function Find-MainWindow([int]$t = 15) {
    $deadline = (Get-Date).AddSeconds($t)
    while ((Get-Date) -lt $deadline) {
        $root = [System.Windows.Automation.AutomationElement]::RootElement
        foreach ($w in $root.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)) {
            if ($w.Current.Name -ne "九序") { continue }
            $c = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, "status.state")
            if ($w.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $c)) { return $w }
        }
        Start-Sleep -Milliseconds 200
    }
    return $null
}

function Capture([string]$name) {
    $w = Find-MainWindow 8
    if (-not $w) { Log "capture $name : window missing"; return }
    $hwnd = [IntPtr]$w.Current.NativeWindowHandle
    $rect = New-Object RP+RECT
    [void][RP]::GetWindowRect($hwnd, [ref]$rect)
    $width = $rect.Right - $rect.Left; $height = $rect.Bottom - $rect.Top
    $bmp = New-Object System.Drawing.Bitmap $width, $height
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $hdc = $g.GetHdc()
    [void][RP]::PrintWindow($hwnd, $hdc, 2)
    $g.ReleaseHdc($hdc); $g.Dispose()
    $path = Join-Path $shotDir ($name + ".png")
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Log "capture $name -> $path"
}

Add-Type -AssemblyName System.Drawing

Get-Process | Where-Object { $_.ProcessName -in @("九序","NineFlow","KeyCatcher") } | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 800
Remove-Item "C:\Users\0409\Documents\NineFlow\publish\NineFlow.ini" -ErrorAction SilentlyContinue

$proc = Start-Process -FilePath "C:\Users\0409\Documents\NineFlow\publish\NineFlow.exe" -PassThru
Start-Sleep -Seconds 4
Capture "01-fresh"

# 场景1：最小化再还原
$w = Find-MainWindow 8
$hwnd = [IntPtr]$w.Current.NativeWindowHandle
[void][RP]::ShowWindow($hwnd, 6)   # minimize
Start-Sleep -Milliseconds 700
[void][RP]::ShowWindow($hwnd, 9)   # restore
Start-Sleep -Milliseconds 900
Capture "02-after-minimize-restore"

# 场景2：把窗口拖出屏幕一部分再拖回
[void][RP]::SetWindowPos($hwnd, [IntPtr]::Zero, -400, 200, 0, 0, 0x0001 -bor 0x0004)
Start-Sleep -Milliseconds 700
[void][RP]::SetWindowPos($hwnd, [IntPtr]::Zero, 300, 150, 0, 0, 0x0001 -bor 0x0004)
Start-Sleep -Milliseconds 900
Capture "03-after-move-offscreen"

# 场景3：其它窗口盖住再移开
$cover = Start-Process -FilePath "notepad.exe" -PassThru
Start-Sleep -Seconds 1
$cover.Refresh()
[void][RP]::SetWindowPos($cover.MainWindowHandle, [IntPtr]::Zero, 300, 150, 800, 600, 0x0010)
Start-Sleep -Milliseconds 900
Stop-Process -Id $cover.Id -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 900
Capture "04-after-cover-uncover"

# 场景4：关到托盘再唤回
$w = Find-MainWindow 8
$w.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
Start-Sleep -Milliseconds 1200
$second = Start-Process -FilePath "C:\Users\0409\Documents\NineFlow\publish\NineFlow.exe" -PassThru
Start-Sleep -Seconds 3
Capture "05-after-tray-restore"

# 场景5：切换置顶开关（触发整卡重绘）
$w = Find-MainWindow 8
$sw = $null
$root = [System.Windows.Automation.AutomationElement]::RootElement
foreach ($win in $root.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)) {
    if ($win.Current.Name -ne "九序") { continue }
    $cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, "switch.topMost")
    $sw = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
    if ($sw) { break }
}
if ($sw) {
    $pattern = $sw.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
    $pattern.Toggle(); Start-Sleep -Milliseconds 500
    $pattern.Toggle(); Start-Sleep -Milliseconds 500
}
Capture "06-after-toggle"

Get-Process | Where-Object { $_.ProcessName -in @("九序","NineFlow","KeyCatcher") } | Stop-Process -Force -ErrorAction SilentlyContinue
Log "done"

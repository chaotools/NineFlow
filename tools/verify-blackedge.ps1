$ErrorActionPreference = "Continue"
$root = "C:\Users\0409\Documents\NineFlow"
$out = "$root\artifacts\verify-blackedge.txt"
Remove-Item $out -ErrorAction SilentlyContinue
function Log([string]$t) { Add-Content -Path $out -Value $t -Encoding utf8 }

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, WindowsBase
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class VB {
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
}
"@

function Find-MainWindow([int]$t = 15) {
    $deadline = (Get-Date).AddSeconds($t)
    while ((Get-Date) -lt $deadline) {
        $root2 = [System.Windows.Automation.AutomationElement]::RootElement
        foreach ($w in $root2.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)) {
            if ($w.Current.Name -ne "九序") { continue }
            $c = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, "status.state")
            if ($w.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $c)) { return $w }
        }
        Start-Sleep -Milliseconds 200
    }
    return $null
}

Get-Process | Where-Object { $_.ProcessName -in @("九序","NineFlow","KeyCatcher") } | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 800
Remove-Item "$root\src\NineFlow\bin\Release\net8.0-windows\win-x64\NineFlow.ini" -ErrorAction SilentlyContinue

$dll = "$root\src\NineFlow\bin\Release\net8.0-windows\win-x64\NineFlow.dll"
$proc = Start-Process -FilePath "C:\Users\0409\dotnet\dotnet.exe" -ArgumentList "`"$dll`"" -PassThru
Start-Sleep -Seconds 5

$w = Find-MainWindow 15
if (-not $w) { Log "窗口未出现"; exit 1 }
$hwnd = [IntPtr]$w.Current.NativeWindowHandle

# 确保在最前
[void][VB]::keybd_event(0x12, 0, 0, [UIntPtr]::Zero); [void][VB]::keybd_event(0x12, 0, 2, [UIntPtr]::Zero)
[void][VB]::SetForegroundWindow($hwnd)
Start-Sleep -Milliseconds 700

# PrintWindow 截图
$rect = New-Object VB+RECT
[void][VB]::GetWindowRect($hwnd, [ref]$rect)
$width = $rect.Right - $rect.Left
$height = $rect.Bottom - $rect.Top
$bmp = New-Object System.Drawing.Bitmap $width, $height
$g = [System.Drawing.Graphics]::FromImage($bmp)
$hdc = $g.GetHdc()
[void][VB]::PrintWindow($hwnd, $hdc, 2)
$g.ReleaseHdc($hdc); $g.Dispose()
$pwPath = "$root\artifacts\edge-printwindow.png"
$bmp.Save($pwPath, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Log "PrintWindow -> $pwPath ($width x $height)"

# 屏幕截图
& "$root\tools\capture-screen.ps1" -OutputPath "$root\artifacts\edge-screen.png" | Out-Null
Log "screen -> $root\artifacts\edge-screen.png"

Get-Process | Where-Object { $_.ProcessName -in @("九序","NineFlow") } | Stop-Process -Force -ErrorAction SilentlyContinue
Get-CimInstance Win32_Process | Where-Object { $_.Name -eq "dotnet.exe" -and $_.CommandLine -like "*NineFlow.dll*" -and $_.ProcessId -ne $PID } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
Log "done"

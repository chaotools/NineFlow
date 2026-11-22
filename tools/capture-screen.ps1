param(
    [string]$OutputPath = "C:\Users\0409\Documents\NineFlow\artifacts\window.png",
    [int]$TimeoutSeconds = 10
)

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName WindowsBase

Add-Type @"
using System;
using System.Runtime.InteropServices;

public static class NineFlowScreen
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    public static extern bool SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    public static bool ForceForeground(IntPtr hwnd)
    {
        ShowWindow(hwnd, 9);
        keybd_event(0x12, 0, 0, UIntPtr.Zero);
        keybd_event(0x12, 0, 2, UIntPtr.Zero);
        SetForegroundWindow(hwnd);
        System.Threading.Thread.Sleep(250);
        if (GetForegroundWindow() == hwnd) return true;

        uint foregroundThread = GetWindowThreadProcessId(GetForegroundWindow(), out _);
        uint currentThread = GetCurrentThreadId();
        bool attached = AttachThreadInput(currentThread, foregroundThread, true);
        try
        {
            BringWindowToTop(hwnd);
            SetForegroundWindow(hwnd);
            System.Threading.Thread.Sleep(250);
        }
        finally
        {
            if (attached) AttachThreadInput(currentThread, foregroundThread, false);
        }

        return GetForegroundWindow() == hwnd;
    }
}
"@

function Find-MainWindow([int]$timeoutSeconds) {
    $deadline = (Get-Date).AddSeconds($timeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        $root = [System.Windows.Automation.AutomationElement]::RootElement
        $windows = $root.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)
        foreach ($w in $windows) {
            $name = $w.Current.Name
            if ($name -ne "九序" -and $name -ne "NineFlow") { continue }
            $cond = New-Object System.Windows.Automation.PropertyCondition(
                [System.Windows.Automation.AutomationElement]::AutomationIdProperty, "status.state")
            if ($w.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)) { return $w }
        }
        Start-Sleep -Milliseconds 250
    }
    return $null
}

$window = Find-MainWindow -timeoutSeconds $TimeoutSeconds
if (-not $window) {
    Write-Output "NO_WINDOW_FOUND"
    exit 1
}

$hwnd = [IntPtr]$window.Current.NativeWindowHandle
# 截图期间临时置顶，避免被其它窗口挡住
[void][NineFlowScreen]::SetWindowPos($hwnd, [IntPtr](-1), 0, 0, 0, 0, 0x0001 -bor 0x0002 -bor 0x0010)
$switched = [NineFlowScreen]::ForceForeground($hwnd)
# 最小化再还原，强制整窗重绘，避免抓到残留的旧像素
[void][NineFlowScreen]::ShowWindow($hwnd, 6)   # SW_MINIMIZE
Start-Sleep -Milliseconds 350
[void][NineFlowScreen]::ShowWindow($hwnd, 9)   # SW_RESTORE
[void][NineFlowScreen]::ForceForeground($hwnd)
Start-Sleep -Milliseconds 1200

$rect = New-Object NineFlowScreen+RECT
[void][NineFlowScreen]::GetWindowRect($hwnd, [ref]$rect)
$width = $rect.Right - $rect.Left
$height = $rect.Bottom - $rect.Top
Write-Output "WINDOW title='$($window.Current.Name)' size=${width}x${height} foreground=$switched"

$directory = Split-Path -Parent $OutputPath
if ($directory -and -not (Test-Path $directory)) { New-Item -ItemType Directory -Force -Path $directory | Out-Null }

$bitmap = New-Object System.Drawing.Bitmap $width, $height
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
try {
    $graphics.CopyFromScreen($rect.Left, $rect.Top, 0, 0, (New-Object System.Drawing.Size $width, $height))
    $bitmap.Save($OutputPath, [System.Drawing.Imaging.ImageFormat]::Png)
    Write-Output "SAVED $OutputPath"
}
finally {
    $graphics.Dispose()
    $bitmap.Dispose()
    [void][NineFlowScreen]::SetWindowPos($hwnd, [IntPtr](-2), 0, 0, 0, 0, 0x0001 -bor 0x0002 -bor 0x0010)
}

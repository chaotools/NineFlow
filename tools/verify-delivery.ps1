# 交付目录自检：确认 exe 能启动、自动生成配置、与工作目录无关、单实例生效。
param(
    [string]$DeliverDir = "C:\Users\0409\Desktop\按键",
    [string]$ExeName = "九序.exe"
)

$ErrorActionPreference = "Stop"
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [Console]::OutputEncoding
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "交付冒烟测试必须与 requireAdministrator 的应用同权限运行；请使用管理员 PowerShell。"
}
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, WindowsBase

$pass = 0
$fail = 0
function Check([string]$name, [bool]$condition, [string]$detail = "") {
    if ($condition) { $script:pass++; Write-Output "  ok   $name" }
    else { $script:fail++; Write-Output "  FAIL $name $detail" }
}

function Stop-AllNineFlow {
    # 中文名 exe 的进程名是 “九序”，英文名是 “NineFlow”，两种都要处理
    Get-Process | Where-Object { $_.ProcessName -in @("九序", "NineFlow") } | Stop-Process -Force -ErrorAction SilentlyContinue
    Get-Process | Where-Object { $_.ProcessName -like "*NineFlow*" -or $_.ProcessName -like "*九序*" } | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 900
}

function Find-MainWindow([int]$timeoutSeconds = 15) {
    $deadline = (Get-Date).AddSeconds($timeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        $root = [System.Windows.Automation.AutomationElement]::RootElement
        $windows = $root.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)
        foreach ($w in $windows) {
            if ($w.Current.Name -notlike "九序 · *") { continue }
            $cond = New-Object System.Windows.Automation.PropertyCondition(
                [System.Windows.Automation.AutomationElement]::AutomationIdProperty, "status.state")
            if ($w.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)) { return $w }
        }
        Start-Sleep -Milliseconds 250
    }
    return $null
}

$exe = Join-Path $DeliverDir $ExeName
$ini = Join-Path $DeliverDir "NineFlow.ini"

function Start-NineFlow([string]$WorkingDirectory = $DeliverDir) {
    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $exe
    $startInfo.WorkingDirectory = $WorkingDirectory
    $startInfo.UseShellExecute = $false
    $process = [System.Diagnostics.Process]::Start($startInfo)
    if (-not $process) { throw "无法启动 $exe" }
    return $process
}

Write-Output "== 前置：清空进程与配置 =="
Stop-AllNineFlow
Remove-Item $ini -ErrorAction SilentlyContinue
Check "交付 exe 存在" (Test-Path $exe)
Check "英文名 exe 存在" (Test-Path (Join-Path $DeliverDir "NineFlow.exe"))
Check "使用说明存在" (Test-Path (Join-Path $DeliverDir "使用说明.md"))
Check "启动前没有配置文件" (-not (Test-Path $ini))

Write-Output ""
Write-Output "== A. 从交付目录启动 =="
$proc = Start-NineFlow
$window = Find-MainWindow
Check "A 主窗口出现" ($window -ne $null)
Check "A 窗口标题含中文名与状态" ($window.Current.Name -like "九序 · *")
Start-Sleep -Milliseconds 800
Check "A 首次运行自动生成 NineFlow.ini" (Test-Path $ini)
Check "A 进程仍在运行" (-not $proc.HasExited)

Write-Output ""
Write-Output "== B. 单实例：再次双击唤出窗口 =="
$second = Start-NineFlow
Start-Sleep -Milliseconds 2500
$second.Refresh()
Check "B 第二个进程自动退出" ($second.HasExited)
Check "B 第一个实例仍在运行" (-not $proc.HasExited)

Write-Output ""
Write-Output "== C. 换工作目录启动，配置仍生成在 exe 同目录 =="
Stop-AllNineFlow
Remove-Item $ini -ErrorAction SilentlyContinue
$proc = Start-NineFlow -WorkingDirectory "C:\Windows"
$window = Find-MainWindow
Check "C 主窗口出现" ($window -ne $null)
Start-Sleep -Milliseconds 800
Check "C 配置生成在 exe 同目录" (Test-Path $ini)
Check "C 没有在 C:\Windows 生成配置" (-not (Test-Path "C:\Windows\NineFlow.ini"))
$iniText = if (Test-Path $ini) { Get-Content $ini -Raw } else { "" }
Check "C 配置内含默认 Down 键" ($iniText -match "Key=Down")
Check "C 配置内含默认 Enter 键" ($iniText -match "Key=Enter")
Check "C 配置内含托盘选项" ($iniText -match "CloseToTray=1")

Write-Output ""
Write-Output "== D. 文件信息 =="
$info = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($exe)
Check "D 产品名是九序" ($info.ProductName -eq "九序") "实际: $($info.ProductName)"
$repoRoot = Split-Path $PSScriptRoot -Parent
[xml]$projectXml = Get-Content (Join-Path $repoRoot "src\NineFlow.Wpf\NineFlow.Wpf.csproj")
$expectedVersion = (@($projectXml.Project.PropertyGroup.Version | Where-Object { $_ }) | Select-Object -First 1)
Check "D 版本号为 $expectedVersion" ($info.FileVersion -like "$expectedVersion.*") "实际: $($info.FileVersion)"

Write-Output ""
Write-Output "== E. 收尾 =="
Stop-AllNineFlow
Remove-Item $ini -ErrorAction SilentlyContinue
$leftover = (Get-Process | Where-Object { $_.ProcessName -in @("九序", "NineFlow") } | Measure-Object).Count
Check "E 没有残留进程" ($leftover -eq 0)
Check "E 交付目录保持干净（无 ini）" (-not (Test-Path $ini))

Write-Output ""
Write-Output "结果：通过 $pass 项，失败 $fail 项"
if ($fail -gt 0) { exit 1 }
exit 0

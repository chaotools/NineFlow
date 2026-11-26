# 九序 / NineFlow —— 构建脚本
#
# 用法：
#   .\build.ps1                # 还原 + 编译 + 跑测试 + 发布单文件 exe
#   .\build.ps1 -SkipTests     # 跳过测试
#   .\build.ps1 -NoPublish     # 只编译并测试，不发布
#
# 输出：<项目根>\publish\NineFlow.exe（自包含单文件，目标机器无需安装 .NET）

[CmdletBinding()]
param(
    [ValidateSet("Release", "Debug")]
    [string]$Configuration = "Release",

    [switch]$SkipTests,

    [switch]$NoPublish
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:DOTNET_NOLOGO = "1"

function Find-Dotnet {
    $candidates = @()

    $onPath = Get-Command dotnet -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($onPath) { $candidates += $onPath.Source }

    $candidates += (Join-Path $env:USERPROFILE "dotnet\dotnet.exe")
    $candidates += "C:\Program Files\dotnet\dotnet.exe"

    foreach ($candidate in $candidates) {
        if ($candidate -and (Test-Path $candidate)) { return $candidate }
    }

    throw "没有找到 dotnet。请安装 .NET 10 SDK：https://dotnet.microsoft.com/download/dotnet/10.0"
}

$dotnet = Find-Dotnet
Write-Host "使用 dotnet: $dotnet" -ForegroundColor Cyan
& $dotnet --version

Write-Host ""
Write-Host "== 1/4 还原 ==" -ForegroundColor Cyan
& $dotnet restore (Join-Path $root "src\NineFlow.Wpf\NineFlow.Wpf.csproj")
if ($LASTEXITCODE -ne 0) { throw "restore 失败" }

Write-Host ""
Write-Host "== 2/4 编译 ==" -ForegroundColor Cyan
& $dotnet build (Join-Path $root "src\NineFlow.Wpf\NineFlow.Wpf.csproj") -c $Configuration --no-restore
if ($LASTEXITCODE -ne 0) { throw "build 失败" }

if (-not $SkipTests) {
    Write-Host ""
    Write-Host "== 3/4 测试 ==" -ForegroundColor Cyan
    $testProject = Join-Path $root "tests\NineFlow.Tests\NineFlow.Tests.csproj"
    & $dotnet build $testProject -c $Configuration
    if ($LASTEXITCODE -ne 0) { throw "测试项目编译失败" }

    $testDll = Join-Path $root "tests\NineFlow.Tests\bin\$Configuration\net10.0-windows\NineFlow.Tests.dll"
    & $dotnet $testDll
    if ($LASTEXITCODE -ne 0) { throw "测试未通过" }
}

if ($NoPublish) {
    Write-Host ""
    Write-Host "已跳过发布（-NoPublish）。" -ForegroundColor Yellow
    return
}

Write-Host ""
Write-Host "== 4/4 发布单文件 exe ==" -ForegroundColor Cyan
$publishDir = Join-Path $root "publish"
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }

& $dotnet publish (Join-Path $root "src\NineFlow.Wpf\NineFlow.Wpf.csproj") -c $Configuration -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "publish 失败" }

$exe = Join-Path $publishDir "NineFlow.exe"
if (-not (Test-Path $exe)) { throw "没有生成 $exe" }

$sizeMb = [Math]::Round((Get-Item $exe).Length / 1MB, 1)
Write-Host ""
Write-Host "构建完成：$exe（$sizeMb MB，免安装单文件）" -ForegroundColor Green

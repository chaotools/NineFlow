# NineFlow.Wpf — WPF 界面层（方案 2）

只替换界面层：`NineFlow.Core`（引擎、热键、录键、INI、校验、更新）**一行未改**，
500 项逻辑测试（完整 532 项）照旧通过。旧 WinForms 工程已移除，仓库只保留 WPF 一个界面方向。

## 为什么是 WPF

原界面布局写在 `MainForm.cs` 的代码里：`DesignWidth = 1120`、`SlotRowsTop = 74`，
列位置 `52 / 236 / 336 / 394 / 500` 全是绝对坐标，再靠 `ApplyResponsiveLayout()`
在 `OnSizeChanged` 里手工重排——窗口一变就得重算一遍，很难加功能。

WPF 版把布局交给容器：根 Grid 6 行（顶栏 44 / 列头 26 / 行区 `*` / 添加行 36 / 页脚 42 / 状态行 22），
行模板与列头**共用同一套列宽**（38 / `*` / 74 / 54 / 42 / 26），列头另由 `SyncListGutter()`
随滚动条出现自动让位。窗口 `SizeToContent="Height"`：行数变化时高度自己长，不靠手工重排。

## 结构

| 文件 | 职责 |
| --- | --- |
| `App.xaml(.cs)` | 单实例（Mutex + 事件）、临时目录重定向、全局异常兜底 |
| `Theme.xaml` | 配色（与 `Theme.cs` 逐色一致）与全部控件模板（含开关、键帽、细滚动条、扁平菜单） |
| `MainWindow.xaml` | 纯 Grid 布局：「极简单面板」——一整块白面，顶栏 / 列头 / 行区 / 添加行 / 页脚 / 状态行 |
| `MainWindow.xaml.cs` | 编排：设置读写、热键、录键、引擎启停、指标与摘要、托盘、退出落盘 |
| `SlotRowVm.cs` | 行 ViewModel（`ItemsControl` + `DataTemplate`，不再持有控件引用） |
| `Updates.cs` | 检查更新 / 下载 / 替换重启（WPF 版） |
| `Assets.cs` | 内嵌 `NineFlow.app.ico` |

## 构建

```powershell
# 开发构建
dotnet build src/NineFlow.Wpf/NineFlow.Wpf.csproj -c Debug

# 自包含单文件（不依赖本机 .NET 运行时）
dotnet publish src/NineFlow.Wpf/NineFlow.Wpf.csproj -c Release -o publish
```

产物：`publish/NineFlow.exe`（约 66.5 MB，双击即可运行）。

## 界面设计

- 布局：一张无卡片、无边框的白面，只用发丝线分隔；行高 54，开关 / 按键 / 间隔 / 次数 / 本轮五列。
  窗口宽度 880，高度随行数生长（320–820），超过 820 行区滚动，列头随滚动条自动让位。
- 开始与停止合并为一个按钮：未运行时是蓝色「开始连点」，运行中变成红色「停止连点」；
  顶栏左侧的状态点同步由灰转绿。
- 四个读数（本轮 / 累计 / 下轮 / 跳过）从大块指标降级为状态行右端一行小字；
  逐行实时明细与「本次执行」摘要不再占版面，改为悬停提示。
- 快捷键与续点压成页脚一行键帽（F7 启动 / F8 停止 / F9 重置 / Down 跳过）+「设置 ▾」。
- 设置菜单改为扁平菜单（`提示音` / `关闭窗口时隐藏到托盘` / `窗口置顶` / `启动时检查更新`），
  顶栏齿轮与页脚按钮共用同一份菜单，均从按钮下方弹出。
- 单实例使用 `Local\NineFlow.SingleInstance.v1`；重复启动会唤回已有窗口，不会同时发送按键。
- `app.manifest` 声明 `requireAdministrator` 与 PerMonitorV2：双击弹 UAC，高 DPI 下不糊。

## 接入状态

1. `build.ps1`、GitHub Actions（build / ui-tests / release / CodeQL）已切到本工程，产物仍为 `NineFlow.exe`。
2. 根 `README.md`、`使用说明.md` 与截图已按 WPF 界面更新。
3. `tools/e2e-v2.ps1` 已按 WPF 自动化树重写，当前 75 项全部通过；CI 在 PR / `main` 上自动运行，Release 发布前也会强制执行。

namespace NineFlow.Core;

/// <summary>
/// 界面文案。软件中文名为「九序」，英文名为「NineFlow」。
/// 界面只用中文名，文案集中在这里，方便统一修改。
/// </summary>
public static class UiText
{
    private static readonly Dictionary<string, string> Table = new()
    {
        ["app.name"] = "九序",
        ["app.subtitle"] = "多键连发 · 智能续点 · 转发助手",
        ["pill.running"] = "运行中",
        ["pill.stopped"] = "已停止",

        ["card.slots.title"] = "按键序列",
        ["card.slots.hint"] = "最少 2 行 · 最多 20 行 · 次数 0 = 无限连发",
        ["col.enabled"] = "有效",
        ["col.key"] = "组合按键",
        ["col.interval"] = "间隔",
        ["col.count"] = "次数",
        ["col.progress"] = "本轮",
        ["unit.second"] = "秒",

        ["card.run.title"] = "本次运行",
        ["card.summary.title"] = "本次执行",
        ["card.options.title"] = "选项",
        ["label.target"] = "目标窗口",
        ["target.none"] = "未选择",
        ["metric.round"] = "本轮进度",
        ["metric.total"] = "累计组合",
        ["metric.next"] = "下轮起点",
        ["metric.skip"] = "自动跳过",
        ["button.addSlot"] = "添加一行",
        ["button.removeSlot"] = "删除这一行",
        ["dialog.removeSlot.title"] = "删除按键行",
        ["dialog.removeSlot.text"] = "这一行已经累计发送 {0} 次，删除后进度会一起丢失。确定删除吗？",

        ["card.hotkeys.title"] = "快捷键与续点",
        ["label.start"] = "启动",
        ["label.stop"] = "停止",
        ["label.reset"] = "重置",
        ["label.skipkey"] = "跳过键",
        ["label.skipcount"] = "跳过数",
        ["skip.auto"] = "留空 = 自动（当前 {0} 项）",
        ["skip.manual"] = "手动跳过 {0} 项",

        ["row.placeholder"] = "点击录入按键",
        ["field.placeholder.count"] = "0",

        ["menu.captureKey"] = "按下要用的键…",
        ["capture.hint"] = "请按下要用的键…（Esc 取消）",
        ["capture.timeout"] = "等待按键超时，已取消",
        ["capture.failed"] = "无法开始录键（键盘钩子装不上，可能被安全软件拦了），请手动填写按键",

        ["button.save"] = "保存配置",
        ["button.load"] = "读取配置",
        ["button.start"] = "开始连点",
        ["button.stop"] = "停止连点",
        ["button.reset"] = "重置进度",
        ["button.update"] = "检查更新",

        ["switch.beep"] = "提示音",
        ["switch.closeToTray"] = "关闭窗口时隐藏到托盘",
        ["switch.topMost"] = "窗口置顶",
        ["switch.autoUpdate"] = "启动时检查更新",

        ["status.stopped"] = "状态：已停止。按 {0} 开始，{1} 停止，{2} 重置进度。",
        ["status.running"] = "状态：运行中。按 {0} 停止。",
        ["status.runningHint"] = "状态：运行中。按 {0} 停止。提示：{1}",
        ["status.stoppedNext"] = "状态：已停止。按 {0} 开始下一轮（自动续点）。",
        ["status.stopping"] = "状态：停止超时，按键可能仍在发送。请再按一次 {0}；必要时用任务管理器结束九序。",
        ["status.completed"] = "状态：本轮已完成（次数已发满）。按 {0} 开始下一轮（自动续点）。",
        ["status.error"] = "状态：已停止。{0}",
        ["status.saved"] = "状态：配置已保存到 {0}",
        ["status.savedHotkeyProblem"] = "状态：配置已保存，但快捷键没生效：{0}",
        ["status.loadedHotkeyProblem"] = "状态：配置已读取，但快捷键没生效：{0}",
        ["status.hotkeyProblem"] = "状态：快捷键没生效：{0}",
        ["status.blocked"] = "状态：{0}",
        ["status.saveFailed"] = "状态：配置保存失败：{0}",
        ["status.progressSaveFailed"] = "状态：进度保存失败：{0}",
        ["status.loaded"] = "状态：配置已读取。",
        ["status.noConfig"] = "状态：没有找到配置文件，请先保存配置。",
        ["status.reset"] = "状态：进度已清零，下一轮将从第 1 项重新开始。",
        ["status.slotAdded"] = "状态：已添加一行，当前共 {0} 行。",
        ["status.slotRemoved"] = "状态：已删除一行，当前共 {0} 行。",
        ["status.shortcutCreated"] = "状态：已在桌面创建快捷方式，以后点它启动即可。",
        ["status.resetWhileRunning"] = "状态：正在连点，不能重置进度。请先按 {0} 停止。",
        ["status.capturing"] = "状态：等待按键…直接按下要用的键，按 Esc 取消。",
        ["status.keyCaptured"] = "状态：已录入 {0}。也可以按 ^a / Ctrl+A 这类写法自己改。",
        ["status.captureFailed"] = "状态：{0}",
        ["status.noActive"] = "状态：没有启用的按键。请至少勾选一行并填写组合按键。",
        ["status.noTarget"] = "状态：还没有选择目标窗口。请先点一下要接收按键的窗口（例如微信），再按 {0} 开始。",
        ["status.badSkipCount"] = "状态：跳过数“{0}”不是整数；留空表示自动取累计次数。",
        ["status.keyInvalid"] = "状态：第 {0} 行按键“{1}”无效：{2}",
        ["status.skipInvalid"] = "状态：跳过键“{0}”无效：{1}",

        ["status.checkingUpdate"] = "状态：正在检查更新…",
        ["status.upToDate"] = "状态：已是最新版（{0}）。",
        ["status.updateAvailable"] = "状态：发现新版本 {0}（当前 {1}），等待确认。",
        ["status.updateAvailableRunning"] = "状态：发现新版本 {0}（当前 {1}）。正在连点，停止后即可更新。",
        ["status.updateCheckFailed"] = "状态：检查更新失败：{0}",
        ["status.downloading"] = "状态：正在下载 {0}… {1}%",
        ["status.updateReady"] = "状态：更新已下载，正在重启完成安装…",
        ["status.updateFailed"] = "状态：更新失败：{0}",
        ["status.previousUpdateFailed"] = "状态：上次更新没有完成，可到 GitHub Releases 手动下载最新版覆盖。",

        ["update.size"] = "{0} MB",
        ["update.sizeUnknown"] = "大小未知",

        ["progress.empty"] = "暂无启用的按键。",
        ["progress.itemMax"] = "{0}  本 {1}/{2} · 累 {3} · 下 {4}",
        ["progress.itemInf"] = "{0}  本 {1} · 累 {2}",

        ["summary.none"] = "本次执行：未勾选任何行",
        ["summary.prefix"] = "本次执行：{0}",
        ["summary.rows"] = "共 {0} 行",
        ["summary.skipAuto"] = "自动跳过 {0} 项",
        ["summary.skipManual"] = "跳过 {0} 项",
        ["summary.warnEmptyKey"] = "第 {0} 行已勾选但没有填写按键",
        ["summary.item"] = "{0} ×{1}",
        ["summary.target"] = "目标窗口：{0}",
        ["summary.targetNone"] = "目标窗口：未选择（先点一下要接收按键的窗口）",

        ["hint.resume"] = "续点：重开页面后先按「跳过键」移过「跳过数」项；跳过数留空 = 自动取累计次数，也可手填。",

        ["tray.show"] = "显示主界面",
        ["tray.start"] = "开始连点",
        ["tray.stop"] = "停止连点",
        ["tray.exit"] = "退出",
        ["tray.tooltip"] = "九序",
        ["tray.tooltipRunning"] = "九序 · 运行中",
        ["tray.balloon.title"] = "九序仍在后台运行",
        ["tray.balloon.text"] = "窗口已隐藏到托盘，按 {0} 开始、{1} 停止；右键托盘图标可以退出。",

        ["dialog.title"] = "九序",
        ["dialog.settingsIssue"] = "设置有问题",
        ["dialog.running.text"] = "九序已经在运行，已为你唤出窗口。",
        ["dialog.update.title"] = "发现新版本",
        ["dialog.update.text"] = "当前版本 {0}，最新版本 {1}（{2}）。\n\n是否立即下载并更新？\n更新只替换程序本体，NineFlow.ini 里的配置和累计进度会保留。更新完成后程序会自动重启。",
        ["dialog.updateFailed.title"] = "更新失败",
        ["dialog.updateFailed.text"] = "无法完成更新：\n{0}\n\n也可以到 GitHub Releases 页面手动下载最新版。",

        ["hotkey.empty"] = "{0}快捷键为空",
        ["hotkey.invalid"] = "{0}快捷键“{1}”无效：{2}",
        ["hotkey.taken"] = "快捷键 {0} 已被其他程序占用",
        ["hotkey.failed"] = "注册快捷键 {0} 失败（Windows 错误 {1}）",
        ["hotkey.problemTitle"] = "快捷键没生效",

        ["row.intervalEmpty"] = "第 {0} 行：间隔时间不能为空（单位：秒）。",
        ["row.intervalInvalid"] = "第 {0} 行：间隔时间“{1}”不是数字（单位：秒）。",
        ["row.countInvalid"] = "第 {0} 行：次数“{1}”不是整数（0 = 无限）。",
        ["row.conflictStop"] = "第 {0} 行的按键“{1}”等于停止热键 {2}：这个键会被系统先截走，连点发到它时会把任务自己停掉。请换一个发送键，或先把停止热键改掉。",
        ["row.conflictStart"] = "第 {0} 行的按键“{1}”等于启动热键 {2}：这个键会被系统先截走，目标窗口收不到。请换一个发送键，或先把启动热键改掉。",
        ["skip.conflictStop"] = "跳过键“{0}”等于停止热键 {1}：跳过阶段会触发停止。请换一个跳过键。",
        ["skip.conflictStart"] = "跳过键“{0}”等于启动热键 {1}：跳过阶段会变成「开始」请求，等于没跳过。请换一个跳过键。",
        ["row.conflictReset"] = "第 {0} 行的按键“{1}”等于重置进度热键 {2}：这个键会被系统先截走，目标窗口收不到，还可能中途清零进度。请换一个发送键，或先把重置热键改掉。",
        ["skip.conflictReset"] = "跳过键“{0}”等于重置进度热键 {1}：跳过阶段会把进度清零。请换一个跳过键。",

        ["tooltip.enabled"] = "打开后这一行才会发送按键。",
        ["tooltip.key"] = "点这个输入框直接进入录键状态，按下要用的键即可；识别的是物理按键，字母统一显示大写键名（R / V），组合键写成 ^+S。按 Esc 取消。",
        ["tooltip.interval"] = "每个按键之间的间隔（秒），支持小数，最小 0.01。",
        ["tooltip.count"] = "本轮发送次数：0 = 无限连发；填 9 = 每轮发够 9 次后停止，下一轮自动续点。",
        ["tooltip.addSlot"] = "添加一行按键设置，最多 20 行。",
        ["tooltip.removeSlot"] = "删除这一行。最少保留 2 行；已有累计进度的行会先确认。",
        ["tooltip.hotkeyStart"] = "开始连点的全局快捷键，例如 F7、Ctrl+F7。",
        ["tooltip.hotkeyStop"] = "停止连点的全局快捷键，例如 F8、Ctrl+F8。",
        ["tooltip.hotkeyReset"] = "重置进度的全局快捷键，例如 F9；按下后累计次数清零。",
        ["tooltip.skipKey"] = "续点时先自动按下这个键，把光标移过已经处理过的项目。",
        ["tooltip.skipCount"] = "续点要跳过的项数。留空 = 自动取累计已处理次数，也可手填微调。",
        ["tooltip.save"] = "保存当前设置与累计进度。",
        ["tooltip.load"] = "读取已保存的设置。",
        ["tooltip.start"] = "开始连点（与启动快捷键相同）。",
        ["tooltip.stop"] = "停止连点（与停止快捷键相同）。",
        ["tooltip.reset"] = "把累计次数与跳过数清零，下一轮从第 1 项重新开始。",
        ["tooltip.beep"] = "开始 / 停止时播放提示音。",
        ["tooltip.closeToTray"] = "点关闭按钮时隐藏到托盘继续运行，而不是退出程序。",
        ["tooltip.topMost"] = "让九序窗口始终显示在最前面。",
        ["tooltip.update"] = "到 GitHub 检查有没有新版本；发现新版可一键下载，确认后自动替换并重启，配置与进度会保留。",
        ["tooltip.more"] = "保存 / 读取配置、检查更新和四个设置项都收在这里。",
        ["menu.enable"] = "开启",
        ["status.optionOn"] = "已开启：{0}",
        ["status.optionOff"] = "已关闭：{0}",
        ["tooltip.autoUpdate"] = "启动 3 秒后自动检查一次；只有发现新版本才会提示，检查失败不会打扰。",


        ["note.read"] = "读取配置文件失败：{0}",
    };

    public static IEnumerable<string> AllKeys => Table.Keys;

    public static bool HasKey(string key) => Table.ContainsKey(key);

    public static string T(string key, params object[] args)
    {
        if (!Table.TryGetValue(key, out string? text)) return key;
        return args.Length == 0 ? text : string.Format(System.Globalization.CultureInfo.CurrentCulture, text, args);
    }
}

# Phase 3：番茄钟核心状态机

本阶段实现真实番茄钟及其设置入口。完成后停止，等待确认再进入 Phase 4 自由专注计时。

## 完成内容与规则

- 默认专注 25 分钟、短休息 5 分钟、长休息 15 分钟、每轮 4 次专注；循环为专注/短休息交替，最后一次专注后长休息。
- 支持开始、暂停、继续、跳过当前阶段、重新开始当前阶段。重新开始先结算旧尝试，再建立新阶段 ID，等待点击开始。
- 每个预定专注位置占一个轮次名额，跳过也向前推进，但不算完成番茄；只有自然到时的专注计入“本次完成番茄”。
- 自动进入默认开启，自动开始休息/专注默认关闭。关闭自动进入时，结束后保留 AwaitingNext，点击“进入”后进入 Ready，再点击开始。三个开关的全部八种组合有测试。
- 设置页允许专注 1–180 分钟、短休息 1–60 分钟、长休息 1–120 分钟、每轮 1–12 次；无效输入禁用应用按钮。
- 每轮使用不可变配置快照。该轮尚未操作且处于 Ready 时可以立即应用；开始或跳过以后修改的参数从下一轮生效，重新开始不会偷偷换用新参数。
- 页面切换不影响引擎。睡眠/休眠不计入专注，系统恢复后保持暂停，用户手动继续。关闭窗口仍退出程序，进程重启恢复留到持久化阶段。

## 分层与计时准确性

`PomodoroEngine` 是可注入 TimeProvider 的纯业务状态机，不依赖 WPF、Windows、线程或数据库。每次命令先按当前时间结算，命令携带预期 StageId，因此临界到时的旧点击不会作用到下一个阶段。

剩余时长来自单调时间锚点与剩余持续时间，并保留 UTC 目标结束时间。每秒回调只是刷新机会，不做“减 1”。系统时间前后调整不会改变有效时长；UTC 明显偏移时重定位目标 UTC。计时器晚回调时只结算当前阶段，实际完成时长封顶为计划时长，下一阶段从观察到结束时开始，不凭空补出多个番茄。

Windows 适配使用 `QueryUnbiasedInterruptTimePrecise`，100ns 单位且排除睡眠。使用 Windows API set `api-ms-win-core-realtime-l1-1-1.dll`：本机 kernel32 未导出该精确入口，已通过真实启动测试发现并修正。平台能力依据见 [微软 API 文档](https://learn.microsoft.com/en-us/windows/win32/api/realtimeapiset/nf-realtimeapiset-queryunbiasedinterrupttimeprecise) 与 [官方 API 位置元数据](https://github.com/MicrosoftDocs/sdk-api/blob/docs/sdk-api-src/content/realtimeapiset/nf-realtimeapiset-queryunbiasedinterrupttimeprecise.md)。

`PomodoroController` 用锁串行化引擎与一个可重新调度的 ITimer。运行中至多每秒刷新，最后不足一秒按剩余时长调度；Ready、Paused、AwaitingNext 停止调度。事件在锁外发布，快照有递增 Version；HomeViewModel 合并待处理的 Dispatcher 更新并丢弃旧版本。

DesktopSession 管理控制器、页面订阅与系统电源订阅生命周期。MainWindow 没有计时业务逻辑。TimerDisplay 只展示文本，并提供只读 UI Automation ValuePattern 及阶段状态。

## 状态转换

| 当前状态 | 事件 | 结果 |
| --- | --- | --- |
| Ready | Start | Running，建立时间锚点及目标 UTC |
| Running | Pause | Paused，保留剩余时长 |
| Paused | Resume | Running，重建时间锚点 |
| Running | 到时 | 结算 Completed；按自动选项停留 AwaitingNext、进入 Ready 或 Running |
| Ready / Running / Paused | Skip | 结算 Skipped；按自动选项进入下一阶段或等待 |
| Running / Paused | Restart | 结算 Restarted；新尝试 Ready，完整时长 |
| AwaitingNext | EnterNext | 下一阶段 Ready |
| Running | 系统睡眠 | 未到时则 Paused；已到时只结算，不自动开始下一阶段 |

## 记录扩展点与当前边界

每次结算返回 PomodoroStageResult，包含 StageId、CycleId、阶段序号、尝试次数、类型、完成/跳过/重启原因、计划/实际时长、首次开始 UTC 和结束 UTC。未开始就跳过的阶段开始时间为空、实际时长为零。休息结果带明确类型，未来不能算入专注统计。

这些结果目前通过事件发出并保留最近一条，没有保存完整历史，也没有建立 FocusSession 数据库记录。下阶段将接入统一专注记账，暂停间隔及跨日分摊需要活动区间，不能简单以结束时间减首次开始时间计算。时钟调整也可能影响 UTC 首尾关系，实际时长以单调时间为准。未来持久化以 StageId 幂等入账，休息剔除，避免与自由计时重复累计。

“本次完成番茄”只表示本次程序运行；每日统计仍显示占位符。设置、配置、活动阶段和计数退出后丢失。没有后台托盘、通知或声音，本阶段没有实施 Phase 4 及以后的功能。

## 文件清单

新增：

- `src/Floe.Domain/Pomodoro/PomodoroSettings.cs`：配置与合法性约束。
- `src/Floe.Domain/Pomodoro/PomodoroState.cs`：阶段、状态、命令和结果枚举。
- `src/Floe.Domain/Pomodoro/PomodoroSnapshot.cs`：快照与阶段结果。
- `src/Floe.Domain/Pomodoro/PomodoroEngine.cs`：纯业务状态机。
- `src/Floe.Application/Timing/PomodoroController.cs`：调度、并发与生命周期。
- `src/Floe.Infrastructure/Time/WindowsAwakeTimeProvider.cs`：Windows 单调清醒时钟。
- `src/Floe.Desktop/Platform/SystemSleepSubscription.cs`：系统睡眠/恢复事件适配。
- `src/Floe.Desktop/Composition/DesktopSession.cs`：实例所有权与释放。
- `src/Floe.Desktop/Features/Settings/PomodoroSettingsViewModel.cs`：参数草稿与验证。
- `src/Floe.Desktop/Features/Settings/PomodoroSettingsPanel.xaml` 及 `.xaml.cs`：参数表单。
- `src/Floe.Desktop/Themes/Templates/Inputs.xaml`：主题输入框。
- `tests/Floe.Domain.Tests/`：项目、状态机测试及依赖锁文件。
- `tests/Floe.Application.Tests/`：项目、控制器测试及依赖锁文件。
- `tests/TestSupport/ManualTimeProvider.cs`：独立控制墙上时间、单调时间和调度触发。
- `docs/phase-3.md`：本说明。

修改：

- `src/Floe.Desktop/App.xaml`、`App.xaml.cs`、`Composition/CompositionRoot.cs`：资源与业务实例组装。
- `src/Floe.Desktop/Features/Home/HomePage.xaml`、`HomePageViewModel.cs`：真实计时命令、状态与计数。
- `src/Floe.Desktop/Features/Settings/SettingsPage.xaml`、`SettingsPageViewModel.cs`：嵌入参数面板。
- `src/Floe.Desktop/Controls/TimerDisplay.cs`、`Themes/Templates/Surfaces.xaml`：辅助功能及自动化识别。
- `src/Floe.Desktop/Themes/Templates/Buttons.xaml`：修正主按钮文字使用按钮自身前景色，保持对比度。
- `src/Floe.Desktop/Shell/MainWindow.xaml`：滚动区域自动化标识。
- `Floe.sln`、`Directory.Packages.props`、相关 `packages.lock.json`：测试项目与测试依赖。运行时没有新增第三方依赖。
- `scripts/Test-Shell.ps1`：真实参数、计时、自然完成及 UI 回归检查。
- `README.md`：当前能力、限制、运行与验收方式。

## 运行与测试

```powershell
cd E:\Codex\floe
.\scripts\dotnet.ps1 build .\Floe.sln -c Release --no-restore
.\scripts\dotnet.ps1 run --project .\src\Floe.Desktop\Floe.Desktop.csproj -c Release --no-build
.\scripts\dotnet.ps1 test .\Floe.sln -c Release --no-build --logger trx --results-directory .\artifacts\tests
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-Shell.ps1 -IncludeCompletion
```

新环境先按 README 还原依赖。不带 IncludeCompletion 时做快速 UI 验收；带参数时另实际等待一分钟。脚本只控制自己启动的程序。

已完成 31 项测试（Domain 26、Application 5），覆盖暂停累计、系统时间前后跳变、完整四番茄循环、自动选项组合、长期延迟、跳过、重启、临界时刻旧命令、睡眠策略、配置切换及并发/释放。睡眠策略使用可控时间验证，未主动让用户电脑睡眠。

Windows 10 实机、真实系统睡眠/恢复、系统时钟人工调整、跨 DPI 多显示器和长时间稳定性仍待后续平台验收。

Windows 11 Build 26200、150% 缩放下已验证：

- Release / Debug 构建均 0 警告、0 错误。
- 真实 WPF 启动、四页导航和参数校验通过。
- 实际一分钟自然完成、自动进入长休息、完成数量恰好加一通过。
- 开始、跨页面持续计时、暂停冻结、继续、重新开始、跳过及新一轮通过。
- 浅/深色实际截图表面亮度断言通过，实色回退、减少动态、恢复默认通过。
- 方向键、Tab 和空格实际键盘操作通过。
- 最小尺寸滚动、缩放、最大化/还原及关闭退出码 0 通过。
- 截图位于 `artifacts/smoke`：`Pomodoro-settings.png`、`Pomodoro-paused.png`、`Pomodoro-completed.png`、`HomePage-dark.png` 等。完整计时验收后仅调整按钮文字模板，并用快速窗口验收复查。

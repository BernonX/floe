# Phase 4：自由专注计时与统一记账

范围：独立自由计时、两种计时的统一内存记账、今日专注与跨日处理。完成后等待确认再进入 Phase 5 Todo。

## 用户可用功能

- 首页增加“番茄钟 / 自由计时”选择，同一位置展示对应计时界面，避免首页同时堆两个大计时器。
- 自由计时显示本次 HH:mm:ss，支持开始、暂停、继续和结束本次。小时使用累计小时数，超过 24 小时不会回零。
- 暂停保存本次累计；结束本次准备新会话，已经投入的时间保留在今日合计中。
- 今日专注显示真实合计和当前统计日期，番茄钟与自由计时共同累计。尚未完成的番茄也实时计入，跳过/重新开始不会抹去真实投入，休息不计入。
- 同一时刻只运行一种模式。另一个模式显示暂停提示并禁用开始按钮；互斥规则也在应用层检查，未来托盘和 Widget 不能绕过。
- 切换显示模式、切换页面本身不操作计时。两种模式可以各自保留暂停进度，使用者决定何时继续。
- 系统睡眠后保持暂停，恢复后刷新今日日期；自由计时复用 Windows 单调清醒时钟。

## 架构调整

原 PomodoroController 替换为 `FocusController`。由一个协调器、一把锁和一个 ITimer 管理 PomodoroEngine、FreeFocusEngine 与 FocusLedger，避免为两个模式建立互不知情的计时服务。

两个纯业务引擎计算实际活动时间并输出增量贡献。协调器先完成记账，再发布整体 FocusSnapshot。UI 可以合并或丢弃旧版本快照，业务记录不会因此丢失。

引擎输出 `FocusContribution(SessionId, Type, Sequence, StartedAtUtc, Duration)`。番茄钟以阶段尝试 ID 作为会话 ID，自由计时在结束本次后换新 ID；贡献序号在单个会话中严格递增。账本忽略已处理序号，并拒绝缺号或已结束后的新贡献，保证当前串行交付流程中的幂等性。它不是云端乱序同步协议。

`FocusSession` 包含来源、首个活动区间时间、结束时间、实际时长、结束原因和活动区间；`DailyFocusRecord` 是按日期分摊的派生汇总，分别保留番茄/自由时长及合计。完整完成标记只用于自然到时的番茄，自由计时以 Finished 标记主动结束。零时长操作不产生虚假的专注记录。

每秒更新的相邻区间合并存储，内存增长主要随会话、暂停与时钟调整次数变化；每日汇总增量更新，不逐秒扫描全部历史。结束事件只修改结束状态，不能再次增加时长。

本阶段不加入 Todo 字段和数据库映射。Phase 6 将建立任务关联，Phase 7 再实现持久化、恢复与数据库约束，保持用户规定的阶段边界。

## 日期与时钟规则

- 统计时区取启动时系统时区，并在本次运行中固定；运行中更改系统时区不重分历史日期，重启后采用新时区。未来持久化设置会提供明确时区策略。
- 每段活动时间沿 UTC 时间线按本地日期边界拆分，午夜前后分别进入对应日期。夏令时日不假设固定 24 小时，已测试 23/25 小时日期。
- 今日日期在午夜更新。本次自由会话不因跨日结束，昨日已累计值也不清零。
- 运行中至多每秒刷新，最后的短剩余量按截止时间调度。全部暂停/就绪时，仅安排下一个午夜唤醒；睡眠时停止调度。系统 TimeChanged 事件刷新日期并重排唤醒。
- 时间长度以单调时间为准，系统时间调整不会凭空增加或扣减累计时长。明显墙上时间调整后，新贡献按修正后的 UTC 定位，已入账历史不迁移；修改系统时钟可能使 UTC 区间重叠或产生间隙，实际时长与贡献序号仍为准。
- 首次计时到回调之间的长延迟按实际活动长度计算；番茄最长只记当前阶段的计划时长，不回放不存在的多个循环。

## 新增文件

| 文件 | 职责 |
| --- | --- |
| `src/Floe.Domain/Focus/FocusRecords.cs` | 专注来源、贡献、活动区间、会话及每日汇总 |
| `src/Floe.Domain/Focus/FocusLedger.cs` | 幂等内存记账、区间合并、本地日期分摊 |
| `src/Floe.Domain/Focus/FreeFocusEngine.cs` | 秒表状态机及活动区间贡献 |
| `src/Floe.Application/Timing/FocusController.cs` | 两种模式互斥、统一调度、先记账后发布快照 |
| `tests/Floe.Domain.Tests/FreeFocusEngineTests.cs` | 自由计时、暂停、重启会话、时钟调整、睡眠 |
| `tests/Floe.Domain.Tests/FocusLedgerTests.cs` | 日期分摊、幂等、区间压缩、夏令时 |
| `tests/Floe.Application.Tests/FocusAccountingTests.cs` | 统一入账、并发互斥、午夜刷新与睡眠跨日 |
| `docs/phase-4.md` | 本阶段交付说明 |

## 修改 / 替换文件

- `src/Floe.Domain/Pomodoro/PomodoroEngine.cs`、`PomodoroSnapshot.cs`：按活动时间输出专注贡献，休息不输出。
- 删除 `src/Floe.Application/Timing/PomodoroController.cs`，由 FocusController 接管，未保留重复的调度实现。
- `src/Floe.Desktop/Features/Home/HomePage.xaml`、`HomePageViewModel.cs`：模式选择、自由计时、今日合计、互斥提示及命令状态。
- `src/Floe.Desktop/Features/Settings/PomodoroSettingsViewModel.cs`：参数应用接入统一协调器。
- `src/Floe.Desktop/Composition/CompositionRoot.cs`、`DesktopSession.cs`：组装并释放共享协调器。
- `src/Floe.Desktop/Platform/SystemSleepSubscription.cs`：订阅并清理 TimeChanged 事件，支持午夜重排。
- 测试文件 `PomodoroControllerTests.cs` 重命名为 `FocusControllerTests.cs`，保留原番茄回归用例，空闲调度断言更新为仅等待午夜。
- `scripts/Test-Shell.ps1`：新增自由计时、互斥、每日合计保留，以及完整番茄只入账一次的 UI 验收。
- `README.md`：更新能力、操作及当前内存边界。没有新增 NuGet 依赖。

## 运行与测试

```powershell
cd E:\Codex\floe
.\scripts\dotnet.ps1 build .\Floe.sln -c Release --no-restore
.\scripts\dotnet.ps1 run --project .\src\Floe.Desktop\Floe.Desktop.csproj -c Release --no-build
.\scripts\dotnet.ps1 test .\Floe.sln -c Release --no-build --logger trx --results-directory .\artifacts\tests\phase4
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-Shell.ps1 -IncludeCompletion
```

首次准备环境先按 README 还原依赖。手动验收可先开始自由计时几秒，暂停并切页，继续后结束；本次归零而今日合计保留。再启动番茄钟，检查另一模式无法同时开始。跨日用可控时间测试，不需要修改用户系统时间。

已通过 51 项自动化测试（Domain 35、Application 16）。Release / Debug 构建均无警告、无错误。

Windows 11 Build 26200、150% 缩放下，实际窗口验收通过：

- 自由计时的开始、暂停冻结、继续累加、切页后保留模式及进度、结束后今日合计保留。
- 两个方向的模式互斥，包含阻止跳过阶段触发另一模式自动运行。
- 完整一分钟番茄自然完成、自动休息、完成数加一；今日专注也恰好增加一分钟，休息不增加。
- 浅/深色实际表面颜色断言、实色回退、恢复默认、键盘方向键/Tab/空格。
- 小窗口滚动、缩放、最大化/还原、关闭退出码 0。
- 自由计时截图位于 `artifacts/smoke/FreeFocus-running.png`、`FreeFocus-paused.png`、`FreeFocus-today.png`，深色及最小尺寸另有 `FreeFocus-dark.png`、`FreeFocus-minimum.png`、`FreeFocus-minimum-scrolled.png`。完整计时验收后精简了自由计时卡片底部重复说明，并以快速窗口回归复核布局。

## 当前限制

所有会话、每日记录、参数和状态仍只存在于内存；退出或崩溃会丢失。今日专注只覆盖本次打开期间的投入，持久化与恢复将在 Phase 7 实现。

Windows 10 实机、真实系统睡眠恢复、系统时钟调整、跨 DPI 多显示器和长时间稳定性仍待后续实机验收。午夜、睡眠、时钟跳变与夏令时规则通过可控时钟测试；没有主动修改用户时钟或让电脑睡眠。

Todo、任务绑定、SQLite、统计趋势页、Widget 与托盘均未在本阶段实施。

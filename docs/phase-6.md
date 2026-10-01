# Phase 6：Todo 与专注记录绑定

范围：任务选择、会话归属、两种计时统一参与任务进度、今日/本周/累计展示。完成后停止，确认后进入 Phase 7 SQLite。

## 功能与交互规则

- 首页右上角选择“这次在做什么”，支持不绑定任务。候选为未完成、未删除的任务。
- 开始专注时保存任务 ID 和开始时标题；暂停、继续和系统睡眠保持同一会话归属。已开始的专注记录不能换任务，避免将之前投入错误归给另一个任务。
- 番茄钟重新开始会结束旧尝试并准备新尝试；旧尝试实际投入保留。自由计时结束本次后准备新会话。没有尚未结束的专注记录时可重新选择任务；休息阶段可为下一次专注选任务。暂停的另一模式仍保留其会话，因此也会锁定选择。
- 自动进入下一次专注时，在状态机开始新阶段的同一次协调操作中捕获任务，第一段时间到账前就已固定。
- 完整完成的番茄专注增加任务番茄数量；跳过、重新开始和自由计时只累计实际时间。预计数量是目标，不是累计上限。休息、暂停、睡眠均不计入。
- 未绑定记录仍参与全局每日专注，不分配到任何任务。
- 改名不改写历史开始时标题；首页显示任务当前标题。完成、删除不强制打断已开始的专注，仍归原 ID；未来默认选择清空。撤销删除恢复同一任务及原进度。
- 任务列表实时显示已完成/预计番茄、累计专注，以及今日和本周专注。统计日期使用启动时固定的时区；本周从周一开始，跨午夜按实际活动区间分摊。

## 架构与数据关系

`TodoTask.Id` → 多条 `FocusSession.TaskId`，TaskId 可空。`FocusSession` 增加 `TaskTitleAtStart`，原开始/结束时间、实际时长、类型、结束原因和活动区间保持统一。计时引擎不依赖 Todo，由 Application 协调器将捕获的关联附加到正时长贡献上。

`FocusController` 仍使用一个锁和一个计时器，串行结算、命令、选择任务与捕获新绑定。阶段更新可能已经暴露下一阶段：先用旧绑定结算贡献和结束记录，再捕获新阶段，避免自动切换时将最后一段时间归错任务。选择与启动的并发结果由锁顺序确定；选择若晚于启动则拒绝。

`FocusLedger` 是时长与番茄进度的唯一来源，按会话 ID 和连续序号幂等入账。后续贡献不能修改任务 ID 或开始时标题；会话结束只处理一次。新增按任务缓存累计时间/完成番茄和按任务日期缓存时间，查询今日/本周只访问对应日期，无需每秒扫描全部历史。日记录与任务记录在同一次贡献处理中更新。

`TodoTask.WithFocusProgress` 生成只读进度投影，不改变任务 ID、完成状态或更新时间。TodoController 管理的原始生命周期实体不单独递增计时字段；页面把统一账本结果投影到展示实体。这使 Phase 7 可以从会话及活动区间重建统计，不需要协调两套独立计数。每日汇总和任务缓存都不是第二份原始时间来源。

任务页只刷新本次有新投入或完成番茄的任务；日期变化才刷新所有可见任务的日期统计。专注更新不重建列表，不触发排序/筛选。跨线程事件在 Dispatcher 上消费；即使事件乱序，也按受影响任务读取当前账本，不会因丢弃旧事件而遗漏另一任务。任务选择候选仅在任务或绑定变化时重建，不随每秒计时重建。退出时释放所有新增订阅，没有新增依赖、轮询或常驻计时器。

## 文件清单

新增：

| 文件 | 作用 |
| --- | --- |
| `src/Floe.Desktop/Themes/Templates/ComboBoxes.xaml` | 主题化任务下拉框，原生键盘/无障碍行为 |
| `tests/Floe.Domain.Tests/TaskFocusLedgerTests.cs` | 任务分账、幂等、跨日/跨周、进度投影测试 |
| `tests/Floe.Application.Tests/TaskBindingTests.cs` | 绑定冻结、自动阶段、生命周期和并发测试 |
| `docs/phase-6.md` | 本阶段交付说明 |

修改：

- `src/Floe.Domain/Focus/FocusRecords.cs`：贡献与会话增加任务关联，新增任务统计结果。
- `src/Floe.Domain/Focus/FocusLedger.cs`：绑定一致性、任务累计及日期缓存、完成番茄幂等统计。
- `src/Floe.Domain/Tasks/TodoTask.cs`：从账本投影任务进度。
- `src/Floe.Application/Timing/FocusController.cs`：统一任务选择、两种会话捕获、任务生命周期事件及统计查询。
- `src/Floe.Desktop/Features/Home/HomePageViewModel.cs`、`HomePage.xaml`：任务候选、锁定选择、当前任务及绑定提示。
- `src/Floe.Desktop/Features/Tasks/TaskItemViewModel.cs`、`TasksPageViewModel.cs`、`TasksPage.xaml`：实时任务进度与日期统计。
- `src/Floe.Desktop/Composition/CompositionRoot.cs`、`App.xaml`：注入共享服务、加载下拉框样式。
- `scripts/Test-Shell.ps1`：实际任务选择、锁定、完成统计、删除/撤销及下一任务自由计时验收。
- `README.md`：当前能力、验收流程和阶段入口。

## 运行与测试

```powershell
cd E:\Codex\floe
.\scripts\dotnet.ps1 build .\Floe.sln -c Release --no-restore
.\scripts\dotnet.ps1 run --project .\src\Floe.Desktop\Floe.Desktop.csproj -c Release --no-build
.\scripts\dotnet.ps1 test .\Floe.sln -c Release --no-build --logger trx --results-directory .\artifacts\tests\phase6
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-Shell.ps1 -IncludeCompletion
```

首次环境准备和还原见 README。手动验收：添加任务，在首页选中它，完整完成一次一分钟番茄，再进行一次自由计时；核对任务累计与属于该任务的每日时间一致。暂停时选择应锁定；重新开始或结束本次后可换任务。删除正在专注的任务，再撤销删除，核对进度保留。

85 项业务测试通过（Domain 53、Application 32），含此前全部回归。新增测试覆盖任务归属不可改写、暂停排除、零时长不建记录、自动阶段旧/新任务、跨午夜/周一、改名/完成/删除/撤销、未绑定全局累计，以及并发选择/开始和到时重复命令。测试结果位于 `artifacts/tests/phase6`。

Release / Debug 构建均为 0 警告、0 错误。实际 Windows 桌面验收覆盖任务选择与键盘 Home / Alt+Down / End / Enter、运行和暂停时锁定、真实一分钟完成、任务与每日累计一致、部分投入不计完整番茄、删除/撤销保留进度、换任务后自由计时，以及已有 Todo、主题、减少效果、导航键盘、最小窗口滚动、最大化/还原和退出检查。

截图保存在 `artifacts/smoke`：`HomePage-task-bound.png` 为任务选择，`TasksPage-focus-progress.png` 为真实完成后的累计进度，`HomePage-dark.png`、`TasksPage-dark.png` 为深色主题，另有两种计时和任务列表的最小窗口与滚动截图。已检查浅色、深色和最小窗口布局；最小高度下使用现有滚动区域访问全部内容。

## 当前边界

所有记录仍在内存，退出或崩溃会丢失；统计只包含本次打开期间。统计页面、数据库和恢复留到后续阶段，未跨阶段开发。任务删除为软删除，不清理已有关联；阶段结束与 UI 切换不生成第二条相同时间记录。运行中的任务不支持中途切换，若需切换先结束当前记录或重新开始。

Windows 10 实机、多显示器/不同 DPI、真实系统睡眠和大规模历史性能仍需后续实测；当前桌面验收环境为 Windows 11 Build 26200、150% 缩放。

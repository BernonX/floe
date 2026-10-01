# Phase 7：SQLite 本地持久化与恢复

范围：持久化现有任务、专注/番茄记录、每日汇总、配置与外观，提供原子检查点和重启恢复。完成后停止，确认后进入 Phase 8 统计页面。

## 交付行为

- 默认数据库 `%LOCALAPPDATA%\Floe\floe.db`，设置页显示实际路径。`FLOE_DATA_DIRECTORY` 可覆盖目录，窗口验收用工作区内独立目录。
- Todo 新增、编辑、完成、恢复、删除、撤销先完成事务再发布 UI 快照；保存失败不会发布未写入的任务状态。
- 统一保存番茄与自由专注的会话、实际活动区间、序号、任务 ID 和开始时标题。历史重建今日/本周/累计进度，重开不会重复累计。
- 保存番茄阶段结果，包括专注/短休息/长休息、完成/跳过/重新开始、轮次 ID、尝试次数和实际时间；休息结果不进入专注时间。
- 应用后的番茄参数以及主题、减少透明/动态效果跨重启保留。未点击应用的参数草稿不保存。进行中修改配置仍从下一轮生效，恢复时保留当前轮旧参数和下一轮新参数。
- 正常退出先结算实际投入并暂停，保存后关闭。重启恢复同一阶段/自由会话、序号、累计和任务绑定，保持暂停，等待手动继续。退出期间不计为专注；不回放错过的自动循环。
- 活动中约每 5 秒保存；开始/暂停/继续/跳过/重新开始/结束、任务选择或生命周期变化、阶段结束、跨日、睡眠和正常关闭立即保存。正常关闭保留亚秒尾段；突然终止恢复最近成功提交的检查点。
- 保存失败时保留尚未提交的内存变化，暂停计时并禁用继续，首页提供重试保存。重试成功后仍保持暂停，由用户手动继续。退出前再次保存，若仍失败，可取消退出或明确选择带着未保存进度退出。
- 无法读取、损坏、字段缺失、时长/序号/关联不一致或较新 schema 时停止打开并保留原数据，不自动用空库覆盖。

## 架构

Application 新增 `IAppStore`，描述加载和原子保存，不引用 SQLite 或 Windows。`TodoController` 保存生命周期；`FocusController` 串行结算、捕获检查点和提交受影响的会话/日期/阶段。Infrastructure 的 `SqliteAppStore` 实现 SQLite 连接、事务、顺序迁移和每目录实例锁。Desktop 在组合根加载后注入同一存储；MainWindow 没有数据库业务。

Domain 的两个计时引擎提供 Capture / Restore，不序列化进程内单调时钟锚点。恢复运行状态转为暂停，继续时创建当前进程的新锚点；历史序号与活动时长继续使用原会话。合并后的活动区间数量不等于贡献序号，因此独立保存 ContributionSequence，避免恢复后因序号丢失而漏记或重复。

一次专注事务包含变化的会话、活动区间、阶段结果、每日汇总、配置和两个计时引擎检查点。数据库约束任务外键，禁止改写会话身份/任务归属、倒退时长/序号或保存过期版本。幂等保存使用主键和绝对累计值，不再将同一时间累加第二次。软删除保留任务外键；撤销保持同一 ID。

统计仍由原始会话和活动区间重建，任务进度与每日汇总均为投影。启动时校验会话活动区间之和、检查点实际时长、序号和任务关联，再提交暂停后的恢复状态。每日表是可重建缓存，启动重新计算并校正；schema 版本 2，按 0→1 基础表、1→2 番茄阶段历史顺序迁移，每个迁移在事务中提交。

连接使用 WAL、foreign_keys 与 synchronous=FULL，进程内串行访问；每个数据目录只允许一个应用实例。SQL 数据全部参数化。当前采用同步本地短事务，活动时最多约每五秒提交一批，不调用假异步数据库 API；Microsoft 文档说明 [该提供程序的异步方法实际同步执行](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/async)。存储耗时和异常下的 UI 响应还需 Phase 12 进一步测量和优化。

新增运行依赖仅为 [Microsoft.Data.Sqlite 10.0.12](https://www.nuget.org/packages/Microsoft.Data.Sqlite/10.0.12) 及必要的 SQLitePCLRaw 原生依赖，没有增加 ORM 或依赖注入容器。

## 数据表

| 表 | 保存内容 | 关系/规则 |
| --- | --- | --- |
| `todo_tasks` | ID、标题、UTC 创建/更新时间、完成/删除时间、预计番茄 | 软删除保留 ID；不重复保存独立的累计专注计数 |
| `focus_sessions` | ID、类型、开始/结束、实际 ticks、结束原因、序号、任务与标题快照 | 可空 TaskId 引用 Todo；主键幂等 |
| `focus_intervals` | 会话 ID、顺序、UTC 起点、实际 ticks | 复合主键；用于排除暂停、跨日重建 |
| `pomodoro_stages` | 阶段/轮次 ID、索引、尝试、阶段/结果、计划/实际 ticks、起止 | 阶段 ID 唯一；休息不计入专注汇总 |
| `daily_focus_records` | 日期、统计时区、番茄/自由 ticks | 日期+时区复合主键；可重建缓存 |
| `pomodoro_settings` | 当前已应用配置 JSON | 单行；进行中的轮次另存其配置快照 |
| `app_settings` | 主题、减少透明、减少动态 JSON | 单行；未来设置可通过迁移或兼容默认值扩展 |
| `runtime_checkpoint` | 版本、两个引擎、当前/捕获任务、统计时区、保存 UTC JSON | 与专注记录同一事务提交；不计入离线时间 |
| `metadata` | 最近可撤销删除的任务 ID | 与任务修改同一事务 |

时长用整数 ticks 保留亚秒精度，UTC 时间用整数 UtcTicks；日期采用 yyyy-MM-dd，GUID 使用稳定字符串。结构化记录保留关系字段和统计索引，小型配置与恢复状态使用 JSON，避免每次增加恢复字段都扩大主数据表。

## 文件清单

新增：

- `src/Floe.Application/Storage/IAppStore.cs`：存储契约、加载数据、应用设置、检查点和存储异常。
- `src/Floe.Infrastructure/Storage/SqliteAppStore.cs`：真实 SQLite 读写、事务、迁移、约束与实例锁。
- `tests/Floe.Domain.Tests/CheckpointValidationTests.cs`：恢复状态与阶段/时长一致性校验。
- `tests/Floe.Application.Tests/TestAppStore.cs`、`PersistenceRecoveryTests.cs`：故障注入与检查点恢复测试。
- `tests/Floe.Infrastructure.Tests/Floe.Infrastructure.Tests.csproj`、`SqliteAppStoreTests.cs`、`packages.lock.json`：独立 SQLite 集成测试项目和锁定依赖。
- `docs/phase-7.md`：本阶段说明。

修改：

- `src/Floe.Domain/Pomodoro/PomodoroEngine.cs`、`PomodoroSnapshot.cs`、`PomodoroState.cs`：捕获/恢复及重启/存储失败暂停原因。
- `src/Floe.Domain/Focus/FreeFocusEngine.cs`、`FocusLedger.cs`、`FocusRecords.cs`：自由恢复、历史投影重建及持久化序号。
- `src/Floe.Domain/Tasks/TodoTask.cs`：恢复原任务身份与生命周期。
- `src/Floe.Application/Tasks/TodoController.cs`、`Timing/FocusController.cs`：保存、加载、脏记录批次、恢复校验、失败暂停/重试和退出结算。
- `src/Floe.Infrastructure/Floe.Infrastructure.csproj`、`Directory.Packages.props`、`Floe.sln`：SQLite 依赖和集成测试项目。
- `src/Floe.Infrastructure/packages.lock.json`、`src/Floe.Desktop/packages.lock.json`：锁定新增传递依赖。
- `src/Floe.Desktop/App.xaml.cs`、`Composition/CompositionRoot.cs`、`DesktopSession.cs`：数据目录、启动恢复、关闭保存和失败提示。
- `src/Floe.Desktop/Themes/ThemeService.cs`：持久化外观，保存失败保留旧外观。
- `src/Floe.Desktop/Features/Settings/PomodoroSettingsViewModel.cs`、`SettingsPageViewModel.cs`、`SettingsPage.xaml`：加载参数、保存状态、本地数据路径与外观回滚。
- `src/Floe.Desktop/Features/Home/HomePageViewModel.cs`、`HomePage.xaml`：重启提示、保存失败及重试入口。
- `scripts/Test-Shell.ps1`：隔离测试数据、正常重启、强制终止恢复、配置/主题/任务恢复及跨重启撤销。
- `README.md`：当前能力、运行、数据位置、备份边界和阶段入口。

## 运行与验收

```powershell
cd E:\Codex\floe
.\scripts\dotnet.ps1 restore .\Floe.sln --locked-mode --configfile .\NuGet.config
.\scripts\dotnet.ps1 build .\Floe.sln -c Release --no-restore
.\scripts\dotnet.ps1 run --project .\src\Floe.Desktop\Floe.Desktop.csproj -c Release --no-build
.\scripts\dotnet.ps1 test .\Floe.sln -c Release --no-build --logger trx --results-directory .\artifacts\tests\phase7
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-Shell.ps1 -IncludeCompletion
```

手动验收：添加任务并绑定专注，修改配置与主题，运行数秒后正常关闭并重开。核对任务/累计/主题/配置保留、计时暂停；继续后增量正确，未将退出期间计入。再测试删除后重开撤销，核对身份与进度。强制终止实验请使用独立目录，运行至少六秒以经过一次检查点。

已通过 105 项测试（Domain 55、Application 39、Infrastructure 11），包括此前全部回归。新增测试覆盖正常/异常退出、亚秒精度、合并区间序号、离线不计时、当前与待生效参数、零时长绑定、保存失败暂停/重试、任务事务失败不发布、重复保存、外键回滚、过期检查点/归属改写拒绝、schema 迁移、较新结构拒绝、实例锁、损坏文件保留、缺失 JSON 字段以及跨日/周一和统计时区恢复。结果在 `artifacts/tests/phase7`。

Release / Debug 构建无警告、无错误。实际 WPF 验收已通过原有 Todo、计时/绑定、真实一分钟完成、主题、键盘、窗口缩放与退出；新增正常重开、进行中参数恢复、主题与减少效果恢复、强制终止后的自由计时检查点恢复/继续、跨重启撤销删除。测试使用独立数据库，并在输出中提供路径。

恢复截图在 `artifacts/smoke`：`HomePage-recovered.png`、`FreeFocus-crash-recovered.png`、`SettingsPage-local-data.png`。验收环境为 Windows 11 Build 26200、150% 缩放；截图已用于检查恢复状态与本地数据说明布局。

## 当前边界与后续

- 未开发统计图表、Widget、托盘或通知；Phase 8 开始统计页面。
- 首次创建数据库保存统计时区，后续保持该时区以免历史日期移动；当前没有时区切换 UI。任务完成数使用相同统计时区。显示的生命周期时间仍按 Windows 当前本地时区展示。
- 正常关闭结算并提交；异常结束只恢复最近成功提交的检查点。在健康磁盘、正常调度下，活动尾段约最多五秒未提交；磁盘故障或调度停顿可能更长。不从旧 UTC 目标结束时间推算离线专注。
- 保存失败后未提交变化仍在内存，可重试；若此时强制结束，只能保留此前成功提交的部分。损坏文件保留，不自动修复或恢复备份。
- 暂无账号/同步、自动备份、导入/导出、加密和历史清理。备份请先正常退出，再复制整个数据目录；运行时 WAL 可能包含已提交数据，不能只复制主库。应用目录锁文件退出后会释放，不必删除。
- 当前启动会加载历史活动区间重建投影。大规模历史、磁盘延迟、Windows 10 实机、多 DPI/多显示器及真实睡眠/断电留到后续验证；本阶段不宣称完成发布级性能测量。

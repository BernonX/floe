# Phase 8：专注统计页面

范围：今日指标、最近 7/30 天趋势、期间合计、每日平均，以及按日期查看番茄钟与自由计时明细。完成后停止，确认后进入 Phase 9 桌面 Widget。

## 交付行为与统计口径

- 今日专注、今日完成番茄、今日完成任务使用同一份本地历史与当前账本。专注进行中实时刷新；暂停和休息不增加专注时长。
- 最近 7 天、30 天均包含今天，起点分别为今天减 6、29 天；连续列出所有日期，没有记录的日期补零。切换范围后同时更新图表、期间合计和每日平均。
- 每日平均 = 期间总专注 / 7 或 30，包括没有专注的日期。时长保留 ticks，显示到秒并向下取整，小时数可以超过 24。各分项独立取整，显示的番茄与自由时长之和可能比显示合计少 1 秒，底层合计仍保留完整精度。
- 时长由实际活动区间按已保存的统计时区分摊。跨午夜的一次专注分别归入两天；自由计时与番茄专注统一参与合计，不将每日缓存再次叠加到会话历史。
- 完成番茄只计自然完成的番茄专注会话，按记录的结束日期计一次。跳过、重新开始、自由计时和休息不增加数量；未绑定任务的完整番茄也计入。
- 统计页显示当天历史完成数量，跨重启保留。首页“本次完成番茄”仍表示本次启动期间数量，两者含义不同。
- 今日完成任务按当前任务的完成时间计算，排除软删除和恢复为待办的任务；撤销删除可恢复原完成状态及计数。这是当前任务状态的统计，不是勾选动作的累计次数。
- 使用现有玻璃卡片、主题和减少效果设置。WPF 原生柱状图最多 30 个元素，支持点击、悬停明细和左右方向键选择日期；选中日期的番茄/自由明细显示在图表上方。
- 柱高按当前范围的最长单日时长缩放。零值只显示灰色基线；极小的非零值保留最低 3 DIP 以便识别，精确数值以明细为准。30 天模式用范围起止日期作为轴标签，避免文字拥挤。
- 默认窗口及最小窗口支持整页滚动；未使用鼠标专属入口，图表日期通过 UI Automation 暴露日期、合计和两种计时明细。

## 架构与数据

`FocusLedger` 增加按日期缓存的完成番茄数量。首次结算自然完成时更新，重复结束不会再次增加；从已有 `FocusSession` 历史恢复时重建。它不依赖 Todo 的存在或生命周期，不写第二份不可校正的计数。

`FocusController.GetStatistics` 在现有协调锁内读取固定长度的日记录及当日完成数量，返回不可修改的窗口快照，避免 UI 每秒复制并扫描全部会话。`StatisticsService` 组合该快照与 Todo 完成数，任务计数仅在版本或统计日期改变时重算。统计查询不写入数据库，不添加迁移，schema 仍为 2。

`StatisticsPageViewModel` 负责范围、日期选择和展示数据。页面可见时订阅现有计时与任务事件，Dispatcher 合并待刷新通知；隐藏后取消订阅，再次进入时读取最新账本。暂停/空闲时没有新增轮询 Timer；不增加图表库或其他第三方依赖。图表使用原生 ListBox / UniformGrid，保留日期选择与键盘语义，更新时保留选中日期，日期越界时回到今天。

组合根注入共享服务和控制器，`DesktopSession` 负责释放统计页面订阅；MainWindow 没有新增统计业务。

## 文件清单

新增：

- `src/Floe.Application/Statistics/FocusStatistics.cs`：查询快照、期间总量/日均及统计服务。
- `tests/Floe.Application.Tests/StatisticsTests.cs`：7/30 天边界、零值日期、两种计时合计、UTC/北京时间午夜、任务生命周期、重启完成计数。
- `docs/phase-8.md`：本阶段交付说明。

修改：

- `src/Floe.Domain/Focus/FocusLedger.cs`：完成番茄日期投影及重建。
- `src/Floe.Application/Timing/FocusController.cs`：串行读取统计窗口。
- `src/Floe.Desktop/Features/Statistics/StatisticsPageViewModel.cs`：统计展示、范围与日期选择、可见期间的实时更新。
- `src/Floe.Desktop/Features/Statistics/StatisticsPage.xaml`：今日卡片、7/30 天选项、汇总、原生柱状图与明细。
- `src/Floe.Desktop/Features/Statistics/StatisticsPage.xaml.cs`：页面加载/卸载和 DataContext 更换时管理订阅生命周期。
- `src/Floe.Desktop/Composition/CompositionRoot.cs`、`DesktopSession.cs`：依赖注入与释放。
- `tests/Floe.Domain.Tests/FocusLedgerTests.cs`：结束去重、午夜归属、跳过/重开/自由计时不计番茄、恢复重建。
- `tests/Floe.Infrastructure.Tests/SqliteAppStoreTests.cs`：真实 SQLite 重开后的历史统计及日均验证。
- `scripts/Test-Shell.ps1`：实际窗口中验证空数据、7/30 根柱、日均、日期选择/键盘、今日数据一致、最小尺寸、重启保留和运行中的实时刷新。
- `README.md`：当前功能与验收入口。

## 运行与验收

```powershell
cd E:\Codex\floe
.\scripts\dotnet.ps1 build .\Floe.sln -c Release --no-restore
.\scripts\dotnet.ps1 run --project .\src\Floe.Desktop\Floe.Desktop.csproj -c Release --no-build
```

首次取得项目需要先执行 README 中的锁定依赖还原步骤。已有 Phase 7 数据无需手动转换。

手动验收：

1. 打开统计：无数据时合计、日均及三个今日指标均为零，日期轴仍连续。
2. 开始自由计时，进入统计观察今日与期间合计同步增长；暂停后保持不变。回到首页对照今日专注。
3. 完整完成一个番茄，统计页增加一项今日完成；跳过或重新开始只保留实际投入，不增加完成数。
4. 切换 7/30 天，确认日期包含今天且空白日期保留，平均采用对应完整天数。
5. 点击不同柱或聚焦后按左右方向键，核对图表上方的日期、合计和番茄/自由时长；30 天可通过选择逐日查看。
6. 完成任务、恢复待办、删除及撤销删除，核对今日完成任务数。
7. 正常退出并重开：历史时长与今日完成番茄保留，计时恢复为暂停；首页本次完成数回零。
8. 切换深浅主题并缩至最小窗口，检查图表、日期和每日明细，滚动查看完整页面。

```powershell
.\scripts\dotnet.ps1 test .\Floe.sln -c Release --no-build --logger trx --results-directory .\artifacts\tests\phase8
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-Shell.ps1
# 可选：实际等待一分钟，检查自然完成及其跨重启统计
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-Shell.ps1 -IncludeCompletion
```

窗口脚本使用 `artifacts/smoke/data-<GUID>` 独立数据库，不向个人库注入演示数据。截图位于 `artifacts/smoke`，包括 StatisticsPage-empty、focus、recovered、minimum 和 live-dark；跨日与多日历史用可控时间测试验证，无需修改系统日期。

## 验证与限制

- Release 和 Debug 构建均为 0 警告、0 错误。
- 115 项自动化测试通过：Domain 59、Application 45、真实 SQLite Infrastructure 11；TRX 保存在 `artifacts/tests/phase8`。
- Windows 11、150% DPI 实际窗口验收通过，包括真实一分钟自然完成、暂停/继续、7/30 天切换、日均、键盘日期选择、最小尺寸滚动、主题、正常重开及强制终止恢复。最终布局另行执行快速回归。
- 真实一分钟完整验收数据库：`artifacts/smoke/data-9fff2735797a46ef96216aa81fa8f472`；最终布局完整快速回归数据库：`artifacts/smoke/data-1cfd44de9e4b432ab7d9153519952a20`，两次均正常退出。最终截图包括 `StatisticsPage-focus-30.png`、`StatisticsPage-minimum-scrolled.png`、`StatisticsPage-live-dark-detail.png`。
- 图表显示当前最近 7/30 个日历日，不提供历史日期选择、导出、任务维度筛选或完整数据分析后台。
- 大规模历史启动重建、长期 CPU/内存测量、Windows 10 实机、高对比实机及多显示器验证仍留待后续阶段；当前不宣称已完成发布级性能验收。
- 本阶段未实现桌面 Widget、托盘或通知。等待确认后进入 Phase 9。

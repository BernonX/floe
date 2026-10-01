# Phase 5：Todo

范围：任务生命周期、任务列表、编辑体验、首页今日完成任务数。完成后停止，确认后进入 Phase 6 任务与 FocusSession 绑定。

## 完成内容

- 新增任务：标题必填、去除首尾空白、单行最多 120 字；预计番茄数量 0–99，0 表示不设预计。无效输入禁用保存。
- 编辑标题与预计数量；保存保留 ID、创建时间和完成状态，取消不写入草稿。切页保留未保存草稿与筛选。
- 任务复选框支持完成和恢复待办。完成时记录 CompletedAtUtc，重复完成保留原时间；恢复清空完成时间，再次完成使用新的时间。
- 删除采用 DeletedAtUtc 标记，从所有可见列表隐藏；撤销最近一次删除恢复同一条记录、原完成状态及预计数量。连续删除只能撤销最后一条，不是多层撤销历史。
- 待办/已完成/全部筛选，全部列表中待办排在已完成前，同类按创建时间倒序。
- 行内显示预计/已完成番茄、累计专注以及创建或完成时间。尚未实现任务绑定，已完成番茄与累计专注为真实初始值 0。
- 首页“今日完成任务”接入任务状态，使用与专注日期相同的启动时统计时区。
- 编辑区 Enter 保存、Escape 取消、Ctrl+N 新建；编辑后焦点回到标题输入。标题过长在列表中换行，删除反馈长标题省略。

## 架构与数据规则

Domain 的 `TodoTask` 是不可变实体，集中定义标题/数量限制与生命周期。包括 ID、标题、创建/更新时间、完成/删除时间、预计番茄、已完成番茄、累计专注字段；完成状态和删除状态从对应时间推导，减少矛盾组合。

Application 的 `TodoController` 持有内存集合，用锁串行修改并在提交后发出版本化只读快照。无效操作不会修改状态；重复完成、无变化编辑、重复删除不产生新版本。删除标记保留 ID，为后续专注历史关联和持久化提供边界。

Desktop 的 `TasksPageViewModel` 管理草稿、验证提示、筛选和编辑状态，`TaskItemViewModel` 展示单条记录。业务修改统一经过 TodoController。页面 code-behind 仅管理输入焦点和订阅生命周期，MainWindow 没有任务业务。

已有行视图模型尽量复用；只有内容变化的行通知绑定。列表配置为有限高度、Pixel 滚动及 Recycling 虚拟化，不为全部记录同时创建卡片。列表较长时在列表内滚动；小窗口下外层滚动提供编辑区与列表的访问。大规模任务的 CPU/内存指标尚未做性能测量。

Todo 不增加轮询或常驻计时器；首页缓存今日完成数，仅在任务变化或专注统计日期变化时重新计算。今日完成数表示“当前仍为已完成、未删除、完成日期为今日”的任务数量。恢复待办或删除会减少数量，撤销删除可恢复数量；它不是累计完成动作次数。

页面消费跨线程变化时切回 Dispatcher，并丢弃旧版本快照。DesktopSession 退出时释放任务页及首页订阅。当前没有新增第三方依赖。

## 新增文件

| 文件 | 作用 |
| --- | --- |
| `src/Floe.Domain/Tasks/TodoTask.cs` | 任务实体、校验和生命周期 |
| `src/Floe.Application/Tasks/TodoController.cs` | 内存集合、只读快照、删除撤销、按日期计数 |
| `src/Floe.Desktop/Features/Tasks/TaskItemViewModel.cs` | 单行展示和完成、编辑、删除入口 |
| `src/Floe.Desktop/Themes/Templates/Tasks.xaml` | 复选框、列表容器、已完成标题样式 |
| `tests/Floe.Domain.Tests/TodoTaskTests.cs` | 标题和数量边界、ID、完成时间与删除规则 |
| `tests/Floe.Application.Tests/TodoControllerTests.cs` | CRUD、幂等、撤销、时区、快照、并发 |
| `docs/phase-5.md` | 本阶段交付记录 |

## 修改文件

- `src/Floe.Desktop/Features/Tasks/TasksPage.xaml`、`TasksPage.xaml.cs`、`TasksPageViewModel.cs`：实际任务页面、快捷键和焦点处理。
- `src/Floe.Desktop/Features/Home/HomePage.xaml`、`HomePageViewModel.cs`：今日完成任务展示与缓存更新。
- `src/Floe.Desktop/Composition/CompositionRoot.cs`、`DesktopSession.cs`：共享时钟、组装 TodoController 及页面生命周期。
- `src/Floe.Desktop/App.xaml`、`Themes/Templates/Inputs.xaml`：加载任务样式，添加标题输入样式。
- `scripts/Test-Shell.ps1`：增加真实 Todo 全流程、快捷键、身份、筛选、滚动和截图验收。
- `.editorconfig`：桌面验收脚本指定 UTF-8 BOM，兼容 Windows PowerShell 5.1 中文测试数据。
- `README.md`：当前能力、操作、限制和阶段入口。

## 运行与测试

```powershell
cd E:\Codex\floe
.\scripts\dotnet.ps1 build .\Floe.sln -c Release --no-restore
.\scripts\dotnet.ps1 run --project .\src\Floe.Desktop\Floe.Desktop.csproj -c Release --no-build
.\scripts\dotnet.ps1 test .\Floe.sln -c Release --no-build --logger trx --results-directory .\artifacts\tests\phase5
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-Shell.ps1
```

首次环境准备及依赖还原见 README。手动验收：添加一条预计 5 个番茄的任务，编辑为 3 个，完成后切到已完成列表，再恢复、删除并撤销。核对名称、数量与状态。最后缩小窗口并尝试列表滚动及三个编辑快捷键。

已通过 69 项业务测试（Domain 46、Application 23），包含此前计时与记账回归。Release / Debug 构建均为 0 警告、0 错误。

实际 WPF 窗口验收覆盖：标题/数量校验，新增/编辑/取消，完成/恢复，删除/撤销，同一任务 ID 保持不变，筛选，列表滚动，首页今日完成数，以及此前的计时、主题、键盘和窗口管理。验收中发现并修正了延迟刷新期间修改排序集合的 WPF 异常；任务复选框使用双向状态绑定，让鼠标、键盘和 UI Automation Toggle 都经过相同业务入口。

截图在 `artifacts/smoke`：`TasksPage.png`、`TasksPage-list.png`、`TasksPage-completed.png`、`TasksPage-dark.png`、`TasksPage-minimum.png`、`TasksPage-minimum-scrolled.png`。

## 当前边界

任务、删除标记、专注记录和设置仍只存于内存，关闭或崩溃会丢失。任务与番茄/自由计时尚未绑定，因此任务进度字段不会随计时增长；Phase 6 接入关联，Phase 7 接入 SQLite 与恢复。

Windows 11 Build 26200、150% 缩放下验收。Windows 10 实机、其他 DPI/多显示器、真实系统睡眠与大规模任务性能仍未完成实测。任务完成通知留到 Phase 10。没有跨阶段实现任务绑定、数据库、Widget 或托盘。

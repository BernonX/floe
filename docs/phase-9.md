# Phase 9：桌面 Widget

范围：独立桌面组件窗口、共享计时操作、显示/隐藏、拖动、置顶、位置锁定、透明度，以及本地设置和位置恢复。完成后停止，确认后进入 Phase 10 系统托盘、通知与系统设置。

## 使用行为

- 首次使用默认关闭；在设置的“桌面组件”区开启。显示状态自动保存，下次启动可自动显示。
- 无普通 Windows 标题栏，使用圆角玻璃表面、轻边缘高光和阴影。组件作为工具窗口，不占任务栏或 Alt+Tab 项；显示时不主动抢焦点，点击后可以使用键盘操作。
- 番茄/自由模式、当前阶段、计时、当前任务、今日专注和状态实时显示。运行时跟随正在计时的模式；该模式运行期间另一模式的入口禁用。两种模式都暂停时可以选择查看。
- “开始/暂停/继续”调用与首页相同的控制器；番茄模式提供跳过和手动进入下一段，自由模式提供结束本次。计时互斥、实际时长、跨日分摊、任务归属和保存失败暂停规则继续集中执行。
- 隐藏后不影响计时；再次显示读取最新状态。暂停的自由计时重启后仍显示原累计，等待手动继续，退出期间不累计。
- 拖动顶部 floe 区移动，松开后保存位置；角落和正文控件保留正常鼠标行为。较长任务标题使用省略号，悬停显示完整标题。
- 设置提供显示、置顶和锁定开关；右键菜单提供置顶、锁定、隐藏和打开主窗口。默认置顶、未锁定。
- 锁定后禁止鼠标拖动，并在原生窗口消息层阻止位置移动；UI Automation 同时报告不可移动。重置位置和屏幕布局恢复仍允许移动，避免组件锁在不可见区域。
- 透明度支持 60%–100%，默认 92%。输入百分比后点击应用；避免拖动输入时反复写数据库。减少透明效果和高对比模式使用 100% 窗口不透明度，保留用户保存的比例供恢复效果后使用。
- 保存物理桌面像素坐标，支持副屏负坐标。首次放在主窗口所在屏幕右下方；重新显示、启动、重置位置及屏幕布局改变时，移回最近显示器的工作区域，避开任务栏。窗口大于工作区域时优先保证顶部拖动入口可见。
- 主窗口最小化时组件继续显示；“打开”恢复并激活主窗口。组件隐藏按钮、右键隐藏或 Alt+F4 只隐藏组件；正常关闭主窗口会保存计时并退出所有窗口，保留下次显示组件的偏好。

## 架构

| 模块 | 职责 |
| --- | --- |
| Domain `WidgetSettings` | 可见、置顶、锁定、透明度、像素坐标及布局标识，校验输入 |
| Application `WidgetController` | 保存成功后发布设置变化；更新单个字段时保留其他设置 |
| Application `WidgetPlacement` | 无 Windows/UI 依赖的工作区域约束，支持负坐标和小屏 |
| Infrastructure `SqliteAppStore` | 独立 Widget 设置写入、读取及 schema 迁移 |
| Desktop `WidgetManager` | 可选窗口生命周期、布局注册使用、屏幕恢复、主题响应及主窗口恢复 |
| Desktop `WidgetWindow` | 通用窗口宿主、拖动、原生位置锁和可访问性移动能力 |
| Desktop `WidgetLayoutDefinition` | 布局 ID、窗口尺寸和视图工厂；未来布局从注册表扩展 |
| Desktop `FocusWidgetViewModel` | 共享 Focus/Todo 快照的展示与命令，可见期间订阅 |
| Desktop `WidgetSettingsViewModel` | 设置编辑、验证和保存；保存失败后继续展示实际设置 |

窗口宿主接受通用 DataContext 和布局描述，不绑定固定番茄视图类型。当前注册 `focus` 布局，324×376 DIP，宿主不负责具体视图内容；未知但合法的布局 ID 使用当前默认视图。后续可新增注册项和视图尺寸，再增加布局/尺寸选择入口；当前不开放用户调整尺寸。

组件使用现有 `FocusController`、`TodoController`、主题资源和存储实例，没有第二份计时器、专注账本或进程。隐藏后取消 Focus/Todo/Widget 快照订阅，重新显示时拉取最新快照；窗口保留供快速再次显示。主程序空闲时沿用午夜唤醒调度，组件不新增秒级轮询、后台动画或截屏循环。

组件不设置主窗口 Owner，避免 WPF 所有者最小化导致组件一并隐藏。应用关闭仍由主窗口控制；`DesktopSession` 在确认退出时通知组件进入关闭流程，释放组件订阅、原生消息 hook 和显示器事件。

## 窗口材质与兼容

采用 `AllowsTransparency=True`、`WindowStyle=None` 的 WPF 分层透明窗口，支持圆角轮廓和真实 Alpha 透明度。微软文档说明该透明模式要求无普通标题栏，适用于非矩形窗口。[WPF AllowsTransparency](https://learn.microsoft.com/en-us/dotnet/api/system.windows.window.allowstransparency?view=windowsdesktop-10.0)

`ShowInTaskbar=False` 配合公开的 `WS_EX_TOOLWINDOW`，并移除 `WS_EX_APPWINDOW`。微软定义工具窗口不出现在任务栏和 Alt+Tab 列表；置顶通过 WPF Topmost 管理。[Windows 扩展窗口样式](https://learn.microsoft.com/en-us/windows/win32/winmsg/extended-window-styles)

位置使用公开的 MonitorFromRect / MonitorFromWindow、GetMonitorInfo、GetWindowRect 和 SetWindowPos。结合现有 PerMonitorV2 manifest，在移动后重新读取物理尺寸并约束工作区域，避免将不同屏幕的 DIP 直接当作像素。[多显示器定位](https://learn.microsoft.com/en-us/windows/win32/gdi/positioning-objects-on-multiple-display-monitors)

当前小组件玻璃层次来自已有冻结画刷、高光、边缘、阴影和 Alpha 合成。Widget 不使用主窗口的 Mica 适配器；实际桌面背景模糊和折射没有在本阶段实现。Windows 10/11 均采用这套窗口宿主，额外材质效果及更细致动画留到 Phase 11。没有加入未公开的桌面嵌入接口或新第三方依赖。

## 本地数据

Schema 由 2 升为 3，增加单行 `widget_settings(id, json)`。迁移在事务中执行，保留已有任务、会话、活动区间、每日缓存和检查点；已有库默认组件关闭。支持旧 schema 1 按 1→2→3 顺序迁移，较新 schema 继续拒绝打开。

Widget 使用独立保存方法和表，修改主题、减少效果、番茄参数不会覆盖组件设置。显示/置顶/锁定即时提交，透明度点击应用提交，位置在拖动结束或恢复后提交；重复值不重复写入。输入、布局标识、坐标配对和透明度均校验。存储失败不发布未保存的配置，拖动位置保存失败时回到此前配置位置，使用现有错误提示。

## 文件清单

新增：

- `src/Floe.Domain/Widgets/WidgetSettings.cs`：组件配置实体及校验。
- `src/Floe.Application/Widgets/WidgetController.cs`：配置服务、位置和值类型及工作区域约束。
- `src/Floe.Desktop/Platform/WidgetWindowPlacement.cs`：公开 Win32 工具窗口与物理像素位置适配。
- `src/Floe.Desktop/Widgets/WidgetManager.cs`：窗口与系统事件生命周期。
- `src/Floe.Desktop/Widgets/WidgetLayoutDefinition.cs`：布局注册与视图工厂。
- `src/Floe.Desktop/Widgets/WidgetWindow.xaml`、`WidgetWindow.xaml.cs`：通用圆角窗口、拖动、位置锁和 UI Automation。
- `src/Floe.Desktop/Widgets/FocusWidgetView.xaml`、`FocusWidgetView.xaml.cs`、`FocusWidgetViewModel.cs`：计时视图与共享快照命令。
- `src/Floe.Desktop/Widgets/WidgetSettingsPanel.xaml`、`WidgetSettingsPanel.xaml.cs`、`WidgetSettingsViewModel.cs`：组件设置 UI 和输入验证。
- `tests/Floe.Application.Tests/WidgetTests.cs`：失败回滚、设置独立保存、重复操作、负坐标/小屏约束、配置验证。
- `docs/phase-9.md`：本阶段交付说明。

修改：

- `src/Floe.Application/Storage/IAppStore.cs`：加载组件配置和独立保存契约。
- `src/Floe.Infrastructure/Storage/SqliteAppStore.cs`：schema 3 迁移、组件配置事务与校验。
- `src/Floe.Desktop/Composition/CompositionRoot.cs`、`DesktopSession.cs`：组件依赖组装、启动和关闭释放。
- `src/Floe.Desktop/App.xaml.cs`：主窗口显示后恢复组件。
- `src/Floe.Desktop/Features/Settings/SettingsPageViewModel.cs`、`SettingsPage.xaml`：桌面组件设置入口。
- `tests/Floe.Application.Tests/TestAppStore.cs`：测试存储支持组件配置。
- `tests/Floe.Infrastructure.Tests/SqliteAppStoreTests.cs`：旧库迁移、配置独立往返、无效配置保留及原有迁移测试适配。
- `scripts/Test-Shell.ps1`：多窗口定位、实际组件操作、原生样式与位置锁、鼠标拖动、最小化/恢复及跨重启验收。
- `README.md`：当前功能、使用和测试入口。

## 运行与手动验收

```powershell
cd E:\Codex\floe
.\scripts\dotnet.ps1 build .\Floe.sln -c Release --no-restore
.\scripts\dotnet.ps1 run --project .\src\Floe.Desktop\Floe.Desktop.csproj -c Release --no-build
```

首次取得项目先按 README 还原锁定依赖。已存在的 Phase 7/8 数据库自动迁移，无需删除数据库。

1. 打开设置并开启“显示桌面组件”；查看圆角、两种模式、今日时长、任务与状态。
2. 从组件开始番茄，在首页暂停，再从组件继续；核对阶段、剩余时间和任务保持一致。
3. 暂停番茄后切换组件到自由计时，开始/暂停/结束；核对首页本次与今日合计，不重复记账。
4. 隐藏组件并继续主窗口计时，再显示组件，应呈现最新状态。
5. 拖动顶部 floe 文字移动；开启锁定后无法移动，重置位置后锁定仍保留。
6. 关闭/开启置顶，输入 75% 并应用；开启减少透明效果时使用 100%，关闭后恢复保存比例。59 或非整数不能应用。
7. 最小化主窗口，组件仍显示；点组件“打开”恢复主窗口。点组件 × 或 Alt+F4 只隐藏组件。
8. 保持组件显示，在计时中关闭主窗口，再运行应用。组件可见、位置、置顶、锁定、透明度保留，计时暂停；手动继续后增长，离线时间不计入。

## 自动化与验证

```powershell
.\scripts\dotnet.ps1 test .\Floe.sln -c Release --no-build --logger trx --results-directory .\artifacts\tests\phase9
# 完整桌面回归，包含真实一分钟到时
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-Shell.ps1 -IncludeCompletion
# 仅检查 Widget，使用全新独立数据目录
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-Shell.ps1 -WidgetOnly
```

- Release / Debug 构建均 0 警告、0 错误；128 项测试通过：Domain 59、Application 56、真实 SQLite Infrastructure 13。TRX 位于 `artifacts/tests/phase9`。
- Windows 11、150% DPI 完整实际窗口验收通过，包括原有 Todo、番茄/自由计时、真实一分钟完成、统计、正常重启、强制终止恢复及跨重启撤销删除。
- Widget 实际验收包括共享计时与任务、互斥、隐藏恢复、原生工具窗口样式、置顶、透明度、减少效果、真实鼠标拖动、原生位置锁、离屏坐标恢复、主窗口最小化/恢复、组件关闭隐藏、显示配置及暂停会话跨重启恢复。
- 本阶段完整验收数据库：`artifacts/smoke/data-7f0e715df4344477958236db0f3467cd`；脚本正常退出，未操作个人数据目录。
- 截图位于 `artifacts/smoke/Widget-light.png`、`Widget-dark.png`、`Widget-free.png`、`Widget-locked.png`、`Widget-recovered.png`。鼠标验收仅在安全获取自己窗口焦点后执行，并恢复原鼠标位置。

尚待后续：Windows 10、高对比、多显示器热插拔和不同 DPI 组合的实机验证；长期 CPU/内存测量；用户调整尺寸、多种布局与样式。当前支持一款可扩展的专注布局。托盘、通知、开机启动与声音属于 Phase 10，未在本阶段实现。

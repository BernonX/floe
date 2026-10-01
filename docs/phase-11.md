# Phase 11 · 动画与 UI 优化

本阶段完成克制的交互过渡、统一的动画取消、组件紧凑模式及键盘导航。动画仅属于 Desktop 的显示层，不参与计时、记账、任务绑定或存储提交。未进入 Phase 12。

## 用户可见变化

| 操作 | 反馈 | 时长 |
| --- | --- | --- |
| 按钮进入/离开 | 轻微高亮淡入/淡出 | 180ms |
| 鼠标或键盘按压/松开 | 1 DIP 位移、既有按压明暗 | 150ms |
| 玻璃卡片 Hover | 低透明度边缘高光，无卡片位移 | 180ms |
| 切换页面 | 新页从 6 DIP 偏移回到原位、透明度 0.9 → 1 | 220ms |
| 开始、暂停、恢复、阶段变化 | 时间展示透明度 0.86 → 1 | 180ms |
| 组件展开/收起 | 高度 376 ↔ 264 DIP | 180ms |
| 组件显示/隐藏 | 玻璃表面淡入/淡出 | 180ms |

统一使用 CubicEase / EaseOut，没有回弹过冲、持续呼吸或循环动画。普通倒计时数字和自由计时秒数更新不触发状态动画；文字和统计图不做持续位移。卡片高光不改变光标，也不暗示整张卡片是按钮。

Ctrl+1–4 分别打开首页、待办、统计、设置。页面立即替换，不保留旧页面等待淡出；命令与可访问性无需等待动画。切页回到内容顶部，ViewModel 的任务草稿、筛选与专注状态继续保留。主窗口仍使用 Windows 原生窗口管理。

键盘焦点边框放在按钮内部，避免小按钮在父布局边界上裁切。组件标题栏按钮最小高度为 28 DIP，主操作保留至少 40 DIP。组件和托盘的右键菜单共享明暗主题、焦点与分隔线资源。

## 减少动态效果与生命周期

“减少动态效果”对按钮、卡片、页面、计时状态及组件尺寸/可见性过渡统一生效，开启时清除正在运行的时钟并立即到达最终值。按压明暗、键盘焦点等静态反馈保留。系统高对比、关闭客户端动画、远程桌面或 WPF 软件渲染也禁用应用动画。减少透明效果单独控制材质和组件淡入淡出，仍可保留尺寸过渡。

每个数值通道只维护一个可替换动画；反向操作从当前显示值开始，旧完成回调通过版本失效。最终基础值在动画前写入，动画完成或取消后移除时钟；不使用持续 HoldEnd、无限 RepeatBehavior 或动画轮询定时器。按钮属性观察只在 Loaded 期间连接，Unloaded 后解除；页面和时间控件隐藏/卸载时清除过渡。隐藏组件立即停止输入与实时快照订阅，玻璃淡出结束后隐藏窗口；再次显示会取消过时的隐藏回调。

WPF 对重叠动画时钟和 Compose 的资源释放有明确要求，本阶段采用 SnapshotAndReplace 并显式清除时钟。参见 [Microsoft WPF 动画注意事项](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/graphics-multimedia/animation-tips-and-tricks) 与 [动画概览](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/graphics-multimedia/animation-overview)。正式空闲资源和长时间运行测量属于 Phase 12，本阶段不把代码结构当成已测量的 CPU/内存指标。

## 紧凑组件与数据兼容

组件标题栏、“紧凑模式”设置和右键菜单使用同一个保存命令。紧凑布局隐藏模式切换、任务标题、次操作及今日统计，保留阶段、计时、状态与主操作；展开后恢复完整信息。当前计时模式、任务归属和实际专注投入保持不变。组件宽度仍为 324 DIP，位置保存为物理像素；动画结束后继续按显示器工作区修正位置。锁定模式阻止用户移动，必要的工作区恢复规则沿用既有实现。

`WidgetLayoutDefinition` 增加可选 `CompactHeight`。布局可自行提供紧凑尺寸；没有紧凑布局的描述不显示标题栏切换按钮。当前仅有 focus 布局，不声称已经交付任意尺寸拖拽或多种布局。

`WidgetSettings` 增加末尾可选 `IsCompact = false`；使用现有 widget_settings JSON，SQLite schema 仍为 4。旧 JSON 缺少字段时默认展开；独立保存外观或系统设置不会覆盖紧凑选项。回归测试实际写入不含新字段的旧配置并重新打开数据库验证。

## 文件清单

新增：

| 文件 | 职责 |
| --- | --- |
| `src/Floe.Desktop/Motion/MotionValue.cs` | 时长与单通道动画替换、取消、释放 |
| `src/Floe.Desktop/Motion/ButtonFeedback.cs` | 鼠标/键盘按钮状态反馈及订阅生命周期 |
| `src/Floe.Desktop/Motion/MenuMotion.cs` | 系统菜单在减少动态效果下的公开 API 处理 |
| `src/Floe.Desktop/Controls/TransitionContentControl.cs` | 页面进入动画、快速切换与滚动复位 |
| `src/Floe.Desktop/Widgets/WidgetMotion.cs` | 组件尺寸、显示/隐藏过渡及反向取消 |
| `docs/phase-11.md` | 本阶段交付与验收说明 |

修改：

| 文件 | 变化 |
| --- | --- |
| `src/Floe.Domain/Widgets/WidgetSettings.cs` | 兼容旧 JSON 的紧凑选项 |
| `src/Floe.Application/Widgets/WidgetController.cs` | 保存后发布紧凑设置 |
| `src/Floe.Desktop/Controls/GlassSurface.cs` | 卡片高光与时钟释放 |
| `src/Floe.Desktop/Controls/TimerDisplay.cs` | 独立状态过渡键；秒数更新不触发动画 |
| `src/Floe.Desktop/Themes/Templates/Buttons.xaml` | 状态反馈接入、内部焦点边框 |
| `src/Floe.Desktop/Themes/Templates/Surfaces.xaml` | 玻璃边缘高光层 |
| `src/Floe.Desktop/Themes/Templates/Menus.xaml` | 组件/托盘共享分隔线 |
| `src/Floe.Desktop/Themes/ThemeService.cs` | 系统动画、渲染与远程会话回退 |
| `src/Floe.Desktop/Shell/MainWindow.xaml` | 页面过渡控件、Ctrl+1–4 与提示 |
| `src/Floe.Desktop/Shell/ShellViewModel.cs` | 键盘导航命令 |
| `src/Floe.Desktop/Features/Home/HomePage.xaml` / `HomePageViewModel.cs` | 状态过渡键及 FOCUS 文案 |
| `src/Floe.Desktop/Features/Settings/SettingsPage.xaml` | 减少动态效果说明 |
| `src/Floe.Desktop/Composition/DesktopSession.cs` | 菜单动画回退与分隔线样式 |
| `src/Floe.Desktop/Widgets/WidgetLayoutDefinition.cs` | 可选紧凑布局尺寸 |
| `src/Floe.Desktop/Widgets/WidgetWindow.xaml` / `.xaml.cs` | 紧凑按钮、菜单、动画表面 |
| `src/Floe.Desktop/Widgets/FocusWidgetView.xaml` / `FocusWidgetViewModel.cs` | 紧凑信息与统一命令/状态键 |
| `src/Floe.Desktop/Widgets/WidgetSettingsPanel.xaml` / `WidgetSettingsViewModel.cs` | 紧凑模式设置 |
| `src/Floe.Desktop/Widgets/WidgetManager.cs` | 动画与可见性协调、主题取消、位置恢复 |
| `tests/Floe.Infrastructure.Tests/SqliteAppStoreTests.cs` | 旧组件配置默认值及独立设置往返 |
| `scripts/Test-Shell.ps1` | MotionOnly 真实几何与快速反向/取消验收，既有检查等待视觉稳定 |
| `README.md` | 当前功能、快捷键、运行与测试入口 |

未引入第三方包、数据库表或额外后台计时器。

## 运行与测试

```powershell
cd E:\Codex\floe
.\scripts\dotnet.ps1 build .\Floe.sln -c Release --no-restore
.\scripts\dotnet.ps1 run --project .\src\Floe.Desktop\Floe.Desktop.csproj -c Release --no-build
.\scripts\dotnet.ps1 test .\Floe.sln -c Release --no-build --logger trx --results-directory artifacts/tests/phase11
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-Shell.ps1 -MotionOnly
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-Shell.ps1 -SystemOnly
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-Shell.ps1 -IncludeCompletion
```

首次还原、SDK 与依赖环境沿用 README。注册表单元测试仅操作随机测试子键；桌面脚本使用独立数据目录，只控制自己启动的应用。MotionOnly 需要本地加速桌面且系统动画开启，否则明确失败提示验收环境；不会更改 Windows 系统动画设置。真实键盘/鼠标输入只有在本测试窗口安全取得焦点后执行。

手动检查：切页、Hover、按压反馈应短促；暂停/继续/切换阶段有反馈，数字每秒变化不闪动。运行计时后反复收起/展开组件，计时保持；动画进行中打开减少动态效果，立即显示最终布局；深浅主题下主按钮及键盘焦点均完整可见；重启保持紧凑、锁定、透明度及暂停恢复。

验证记录（Windows 11 x64，本地桌面，150% DPI）：

- Debug / Release 构建均为 0 警告、0 错误。
- 146 项测试通过：Domain 59、Application 66、Infrastructure 21；包括旧 JSON 的紧凑默认值和配置独立保存。结果在 `artifacts/tests/phase11`。
- MotionOnly 通过快速切页、滚动复位、草稿保留、Ctrl+1–4 真实键盘输入、尺寸过渡中间帧、快速反向操作、正在过渡时取消动态效果、锁定位置、任务绑定、运行计时及紧凑模式重启恢复。最终数据库为 `artifacts/smoke/data-8b808ee955664771b07a643a78f4495c`。
- 紧凑布局经浅色/深色截图复核后缩小标题栏按钮，并将焦点边框改为内部绘制；最终截图中主操作完整显示。几何检查同时核对可见按钮至少 40 DIP 及玻璃内边距，避免只检查窗口边界而漏掉内容裁切。截图为 `artifacts/smoke/Widget-compact-light.png`、`Widget-compact-dark.png`。
- SystemOnly 通过原生托盘注册、菜单、两种计时互斥、隐藏期间计时、关闭/最小化/恢复、后台启动、保存失败取消退出后重试及显式退出。未修改真实开机启动项。数据库为 `artifacts/smoke/data-ecf1f665da1e4344a15865027b44ed99`。
- 完整桌面回归通过：真实一分钟专注自然结束并进入长休息、每日统计准确累计、Todo/任务绑定、自由计时互斥、主题和减少动态效果、键盘/最小窗口/最大化、正常重启、强制终止检查点恢复、跨重启撤销删除及组件真实鼠标拖动和位置提交。组件配置与暂停进度在重启后保持，退出期间不补计时间。数据库为 `artifacts/smoke/data-83f97c9d856c4e518aa09fd06914ee34`。

Windows 10 实机、高对比完整操作、远程桌面与软件渲染环境仍需对应环境验收。现有 Windows 11 Mica / Windows 10 实色降级方案保持；本阶段没有增加真实光学折射、逐帧背景采样或组件后方桌面模糊。发布包、启动/空闲性能实测和更全面异常验证留在 Phase 12，等待确认后进入。

# Phase 1：基础 WPF 项目

状态：完成，等待用户确认后进入 Phase 2。

## 交付范围

建立 .NET 10 / WPF 解决方案、四层项目引用、构造函数注入的 CompositionRoot、MVVM 导航外壳，以及首页/待办/统计/设置四个独立页面。

页面明确说明功能尚未开放，没有模拟业务数据、无效计时按钮或后台计时。下层程序集当前只建立依赖边界，不提前生成状态机、数据库模型或平台服务。此阶段的窗口使用系统标题栏和标准导航控件；Liquid Glass 材质、主题与交互模板由 Phase 2 实现。

## 依赖方向

```text
Floe.Domain         -> 无项目依赖，net10.0
Floe.Application    -> Domain，net10.0
Floe.Infrastructure -> Application，net10.0-windows
Floe.Desktop        -> Application、Infrastructure，net10.0-windows + WPF
```

Desktop 当前唯一 NuGet 依赖为 CommunityToolkit.Mvvm 8.4.0。配置集中在 Directory.Packages.props，并提交每个项目的 packages.lock.json。Nullable、隐式 using、确定性构建、警告视为错误在 Directory.Build.props 统一启用。

主窗口仅初始化 XAML 并接受 ShellViewModel。导航项持有页面 ViewModel，内容控件通过 DataTemplate 自动映射对应 UserControl。切换不重建 ViewModel；后续页面可以保留输入状态。没有页面级全局消息总线或服务定位器。

## 新增文件

根目录：

- `.gitignore`、`.editorconfig`
- `Floe.sln`、`global.json`
- `Directory.Build.props`、`Directory.Packages.props`、`NuGet.config`
- `README.md`

四个项目及其依赖锁文件：

- `src/Floe.Domain/Floe.Domain.csproj`、`packages.lock.json`
- `src/Floe.Application/Floe.Application.csproj`、`packages.lock.json`
- `src/Floe.Infrastructure/Floe.Infrastructure.csproj`、`packages.lock.json`
- `src/Floe.Desktop/Floe.Desktop.csproj`、`packages.lock.json`

Desktop 文件：

- `App.xaml`、`App.xaml.cs`、`app.manifest`
- `Composition/CompositionRoot.cs`
- `Shell/MainWindow.xaml`、`Shell/MainWindow.xaml.cs`
- `Shell/ShellViewModel.cs`、`Shell/PageViewModel.cs`
- `Shell/NavigationItemViewModel.cs`、`Shell/ShellResources.xaml`
- `Features/Home/HomePage.xaml`、`HomePage.xaml.cs`、`HomePageViewModel.cs`
- `Features/Tasks/TasksPage.xaml`、`TasksPage.xaml.cs`、`TasksPageViewModel.cs`
- `Features/Statistics/StatisticsPage.xaml`、`StatisticsPage.xaml.cs`、`StatisticsPageViewModel.cs`
- `Features/Settings/SettingsPage.xaml`、`SettingsPage.xaml.cs`、`SettingsPageViewModel.cs`

工具与说明：

- `scripts/dotnet.ps1`：本地 SDK 优先的命令入口，进程级设置会在结束时恢复。
- `scripts/Test-Shell.ps1`：通过 Windows UI Automation 操作实际窗口的集成冒烟验收。
- `docs/phase-1.md`：本文件。

工具生成但不纳入源码的内容：`.tools` 下的 SDK/下载包/依赖缓存、各项目 bin/obj、`artifacts/smoke` 下的验收截图。没有修改原有用户文件，工作区开始时为空。

## 运行

```powershell
cd E:\Codex\floe
.\scripts\dotnet.ps1 restore .\Floe.sln --locked-mode --configfile .\NuGet.config
.\scripts\dotnet.ps1 build .\Floe.sln -c Release --no-restore
.\scripts\dotnet.ps1 run --project .\src\Floe.Desktop\Floe.Desktop.csproj -c Release --no-build
```

本工作区原先只有 .NET 8 运行时，没有 SDK；经授权，从微软官方源下载 .NET 10.0.401 Windows x64 SDK，并按发布元数据校验 SHA-512 后解压到 `.tools/dotnet`。没有安装系统级 SDK，也没有改动系统 PATH。

## 验证

在 Windows 11、系统构建 26200、150% 缩放环境中完成：

- 使用锁文件还原依赖成功。
- Debug 和 Release 编译通过：0 警告、0 错误。
- 启动实际 WPF 进程，找到主窗口和四个导航入口。
- 逐一选择导航项，检查页面 UserControl 已加载、页面标题与导航项一致。
- 窗口缩放到最小尺寸、最大化并还原成功。
- 关闭主窗口后进程正常退出，退出码为 0。
- 保存四页和最小尺寸首页截图，进行布局检查。

重复窗口验收：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-Shell.ps1
```

脚本只操作其自己启动的进程，失败时也清理该进程。需要一个可用的 Windows 桌面会话。自动化通过 SelectionItemPattern 验证导航；键盘 Tab/方向键操作另按 README 的手动验收步骤检查。

## 当前限制与下一阶段

- 未进行 Windows 10 实机、多显示器、不同 DPI 和屏幕阅读器验收；清单中声明 PerMonitorV2 不等于这些场景已验证。
- 当前关闭窗口直接退出，托盘常驻在 Phase 10 实现。
- 当前只包含浅色基础外壳，不包含材质效果、主题切换或动画。
- 未建立业务测试项目：状态机和跨日逻辑出现时，再按阶段增加有意义的单元与数据库集成测试。
- 当前产物为框架依赖构建，不是自包含发行包。

下一阶段为 Phase 2：Liquid Glass 基础组件。需要用户确认后开始。

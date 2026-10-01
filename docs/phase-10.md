# Phase 10 · 系统托盘、提醒与系统设置

本阶段接入 Windows 系统托盘、专注/休息/任务完成提醒、独立声音开关和当前用户开机启动；未进入 Phase 11。

## 使用与窗口行为

通知区域出现 Floe 时钟图标，Windows 可能将新图标放在折叠菜单中。单击打开主窗口，右击显示菜单：打开主窗口、开始/暂停/继续番茄钟、开始/暂停/继续自由计时、显示桌面组件、退出 Floe。正在运行的另一种计时会禁用对应菜单项，最终业务控制器仍校验互斥与阶段 ID。

默认关闭主窗口时隐藏到托盘，不暂停、不结束、不结算另一套计时；组件继续显示。默认最小化保留普通 Windows 行为，可在设置开启“最小化时收进托盘”。托盘与组件恢复主窗口共用入口，保留最大化状态。关闭组件仍只隐藏组件。

托盘“退出 Floe”与设置中的退出按钮执行最终保存，然后退出所有窗口并释放数据库锁；关闭到托盘关闭时，普通窗口关闭也执行退出。保存失败会询问是否仍退出，选择“否”保留应用、组件与未保存进度，恢复主窗口并允许重试保存。系统注销/关机请求也执行最终保存。托盘注册失败时主窗口保持可达，关闭按退出处理；接收 Explorer 的 `TaskbarCreated` 广播后重建图标，失败时显示主窗口。

设置中的“提醒与系统”提供通知、声音、当前用户登录后启动、关闭到托盘、最小化到托盘、测试提醒与退出。默认通知开启、声音关闭、开机启动关闭、关闭到托盘开启、最小化到托盘关闭。开机启动仅在用户更改该开关时注册/移除当前用户 `Run` 键中的 Floe 值；不会自动启用、修改其他程序或改写 Windows 的 `StartupApproved` 策略。Windows 启动管理可以禁用已注册项目。

启动项使用带引号的绝对路径及 `--background`。通过本地 SDK 的 `dotnet.exe Floe.dll` 启动时注册相同宿主与 DLL；普通应用宿主注册自身 EXE。路径过长只阻止启用启动项，不阻止 Floe 本身启动。开发目录及运行时必须保持可用；正式自包含发布在 Phase 12。后台启动不显示主窗口，恢复已启用的组件；恢复的专注保持暂停，不计入离线时间。

## 提醒与统计边界

`CompletionNotifications` 消费领域阶段结果与 Todo 的成功提交完成事件。只有自然完成的专注/休息产生阶段提醒；跳过、重启、恢复历史、重复勾选、编辑已完成任务或撤销删除均不误发。任务重新恢复后再次完成，属于新完成操作，可再次提醒。任务完成不改变计时状态。

阶段结束写入失败时保留待提醒结果，成功重试后提醒一次。有序版本与阶段 ID 去重处理并发回调；已处理 ID 使用有界缓存，避免常驻内存随历史无限增加。提醒不参与专注记账，托盘、主窗口与组件仍只使用同一个 FocusController。托盘不增加轮询计时器，菜单打开时读取最新快照。

平台采用公开 Win32 `Shell_NotifyIconW` 系统托盘提醒，没有增加第三方包或 WinForms 框架依赖。通知使用 `NIIF_NOSOUND`，声音由独立开关控制系统提示音，避免关闭声音后通知仍自动响。系统通知请求采用 `NIIF_RESPECT_QUIET_TIME`。Windows、勿扰策略与系统通知设置决定呈现方式和是否显示，发送请求成功不等于用户实际看到了通知；声音单独受音量与系统声音方案控制。当前未实现可持久回看的 WinRT 通知中心、通知操作按钮或点击通知深链接，点击提醒只打开主窗口。

## 数据与职责

SQLite schema 3 → 4 在事务中增加 `system_settings(id, json)`，现有任务、阶段、活动区间、每日统计、外观与组件设置均保持。系统配置独立写入，修改主题、Widget 或番茄参数不会覆盖系统选项。开机启动以 Windows 注册表状态为准，不在 SQLite 再保存容易失真的布尔副本。设置页加载时重新读取注册状态。

- Application：系统设置持久化协调、平台接口、提交后的完成提醒。
- Infrastructure：SQLite 顺序迁移、当前用户 Windows 启动项适配与命令构造。
- Desktop：原生 HWND/托盘与 WPF 菜单、可访问的设置面板、窗口隐藏/恢复/退出及通知调度。
- MainWindow：继续只负责窗口与视觉，不承载托盘或计时业务。关闭取消后通过 Dispatcher 恢复窗口，避免在 Closing 回调内非法 Show。

托盘宿主使用 1px WPF 工具窗口接收广播，仅在显示菜单时短暂在屏幕外显示以提供真实的窗口输入所有者；菜单关闭后隐藏。裸 HwndSource 无法在主窗口隐藏后稳定持有菜单焦点，因此最终使用 WPF Window 并在其 HWND 上接收原生通知。菜单复用明暗/高对比资源，支持键盘和 UIAutomation；不存在普通任务栏窗口。原生图标句柄、菜单、事件订阅、计时与数据库连接随退出释放。

## 文件清单

新增：

| 文件 | 职责 |
| --- | --- |
| `src/Floe.Application/SystemIntegration/SystemSettingsController.cs` | 系统配置、启动项接口与平台异常 |
| `src/Floe.Application/SystemIntegration/CompletionNotifications.cs` | 完成提醒与提交、去重规则 |
| `src/Floe.Infrastructure/Platform/WindowsStartupRegistration.cs` | 当前用户启动项、绝对路径命令 |
| `src/Floe.Desktop/Platform/TrayIcon.cs` | 公开 Shell API、宿主 HWND、Explorer 重建、提醒与声音 |
| `src/Floe.Desktop/Features/Settings/SystemSettingsViewModel.cs` | 系统设置、失败反馈、提醒与退出命令 |
| `src/Floe.Desktop/Features/Settings/SystemSettingsPanel.xaml` / `.xaml.cs` | 系统设置 UI、读取启动项状态 |
| `src/Floe.Desktop/Themes/Templates/Menus.xaml` | 托盘菜单的主题、键盘状态与布局 |
| `src/Floe.Desktop/Assets/Floe.ico` | 原生应用/托盘图标 |
| `scripts/Build-Icon.ps1` | 无绘图库依赖的图标生成源 |
| `tests/Floe.Application.Tests/SystemIntegrationTests.cs` | 提醒、存储失败、恢复历史与设置回滚测试 |
| `tests/Floe.Infrastructure.Tests/WindowsStartupRegistrationTests.cs` | 启动命令、隔离注册表与长路径测试 |
| `docs/phase-10.md` | 本阶段交付与验收说明 |

修改：

| 文件 | 变化 |
| --- | --- |
| `src/Floe.Application/Storage/IAppStore.cs` | 独立系统配置加载/保存合同 |
| `src/Floe.Application/Tasks/TodoController.cs` | 成功保存后发布任务完成事件 |
| `src/Floe.Infrastructure/Storage/SqliteAppStore.cs` | schema 4 顺序迁移及系统配置读写 |
| `src/Floe.Desktop/Composition/CompositionRoot.cs` | 系统服务及设置组装 |
| `src/Floe.Desktop/Composition/DesktopSession.cs` | 托盘菜单、共享命令与窗口生命周期 |
| `src/Floe.Desktop/App.xaml` / `.xaml.cs` | 显式退出、后台启动与系统会话结束保存 |
| `src/Floe.Desktop/Floe.Desktop.csproj` | 图标资源与 EXE 图标 |
| `src/Floe.Desktop/Shell/MainWindow.xaml` | 主窗口图标 |
| `src/Floe.Desktop/Features/Settings/SettingsPageViewModel.cs` / `SettingsPage.xaml` | 接入系统设置面板 |
| `src/Floe.Desktop/Widgets/WidgetManager.cs` | 组件打开主窗口共用恢复入口 |
| `tests/Floe.Application.Tests/TestAppStore.cs` | 系统配置测试存储 |
| `tests/Floe.Infrastructure.Tests/SqliteAppStoreTests.cs` | 升级/独立配置/历史保留测试，更新旧库模拟 |
| `scripts/Test-Shell.ps1` | 托盘、后台启动、窗口行为、真实 SQLite 锁下退出保护；既有回归显式使用关闭即退出配置 |
| `README.md` | 当前功能、运行方式和最新行为 |

## 运行与验收

```powershell
cd E:\Codex\floe
.\scripts\dotnet.ps1 build .\Floe.sln -c Release --no-restore
.\scripts\dotnet.ps1 run --project .\src\Floe.Desktop\Floe.Desktop.csproj -c Release --no-build
# 首次还原仍使用 README 中的 locked-mode 命令。
.\scripts\dotnet.ps1 test .\Floe.sln -c Release --no-build --logger trx --results-directory artifacts/tests/phase10
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-Shell.ps1 -SystemOnly
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-Shell.ps1 -SystemOnly -IncludeCompletion
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-Shell.ps1 -IncludeCompletion
```

自动化注册表测试仅使用随机的 `HKCU\Software\Floe.Tests\<GUID>` 子键，测试后删除，不更改真实 Run 项。受限沙箱需要允许该临时注册表测试。桌面脚本只控制自己启动的进程及独立 `artifacts/smoke/data-<GUID>` 数据库；会请求一次系统测试提醒，声音保持关闭，并确认真实 Floe 启动项未变化。托盘测试通过公开图标矩形 API 核对注册，投递正常托盘鼠标回调，再使用真实 WPF 菜单的 UIAutomation 命令。原有拖动与键盘测试只在安全取得本测试窗口焦点时操作输入。

验证记录：

- Debug / Release 构建 0 警告、0 错误。
- 145 项测试通过：Domain 59，Application 66，Infrastructure 20。早期完整运行的注册表测试因沙箱权限失败；授权后通过，新增长路径用例单独通过。结果位于 `artifacts/tests/phase10`。
- Windows 11 x64、150% DPI 的真实托盘注册与菜单、两种计时互斥、组件切换、关闭/最小化隐藏、最大化恢复、后台启动及设置恢复通过。
- 主窗口隐藏期间真实一分钟专注、随后一分钟休息自然结束通过；不是更改系统时间或加速计时。数据库位于 `artifacts/smoke/data-bafaee526dd148f5902c01d28f75cc3f`。
- 用独立 SQLite 连接实际持有写锁，最终保存失败时选择“否”保持应用和组件，释放锁后重试保存及退出通过。数据库位于 `artifacts/smoke/data-40ac9b347d0d4125bd86b9a96ac16449`。
- 系统提醒开关、独立声音设置、请求通知的路径通过；没有把通知请求等同于屏幕实际显示或可听见声音。
- 全部既有桌面功能回归通过，包含真实一分钟番茄、任务绑定/统计、主题/键盘、强制终止恢复、跨重启撤销删除与组件真实鼠标拖动。数据库位于 `artifacts/smoke/data-1516571c82124912ba7173941e0987f7`。
- 菜单最终焦点修复后系统集成全部通过，截图在 Windows 菜单淡入完成后复核，暗色表面、文字、禁用项、勾选与高亮正常。数据库位于 `artifacts/smoke/data-8c1b81427aff4a45b406e057ef5971fb`；截图 `artifacts/smoke/TrayMenu-dark.png` 与 `SettingsPage-system-dark.png`。
- 最终托盘宿主更改后再次通过既有功能完整回归（复用此前真实到时验证，本次不额外等待一分钟），包括多次正常退出/恢复、强制终止与组件拖动。数据库位于 `artifacts/smoke/data-3c415f72eeca469a87066925a1f31a2d`。

Windows 10 实机、实际注销/重新登录的启动项、真实 Explorer 重启、系统关机及高对比模式的完整操作仍需要相应桌面环境验收。没有在用户机器自动开启真实开机启动或重启 Explorer。当前平台调用均是 Win10/11 公开 API；Phase 11 再进行动画与 UI 优化，Phase 12 处理发布和更全面的性能/异常验收。

平台依据：[Microsoft 通知区域与系统提醒](https://learn.microsoft.com/en-us/windows/win32/shell/notification-area)、[NOTIFYICONDATA / 声音与安静时段标志](https://learn.microsoft.com/en-us/windows/win32/api/shellapi/ns-shellapi-notifyicondataw)、[Run / RunOnce 启动项](https://learn.microsoft.com/en-us/windows/win32/setupapi/run-and-runonce-registry-keys)、[原生图标矩形接口](https://learn.microsoft.com/en-us/windows/win32/api/shellapi/nf-shellapi-shell_notifyicongetrect)。

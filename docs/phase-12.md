# Phase 12 · 性能、异常处理与本地发布

UI 精修已先完成。本阶段保留 Domain、Application、Infrastructure 的计时、任务、记账规则与 schema 4，调整显示层订阅生命周期，补齐诊断、可重复构建及便携发布流程。

## 性能调整与实测

首页仅在页面加载且可见时订阅专注和任务更新；再次显示时读取控制器最新快照并刷新任务选择。待办页隐藏时停止计时进度刷新，保留编辑草稿；统计页在整个主窗口隐藏时也取消实时订阅。托盘计时、存储检查点与 Widget 的独立显示订阅继续正常运行。

去掉主窗口整面背景的 BlurEffect，避免大面积中间渲染纹理；冻结的柔和径向渐变保持背景层次，真正的桌面模糊仍由 DWM Acrylic 管理。下拉与菜单的局部装饰模糊保留。减少透明/高对比的 Popup 使用已有实色/系统颜色画刷，避免固定浅色背景影响对比。

Windows 11 build 26200、x64、150% DPI、20 个逻辑处理器，Release，使用工作区 SDK 启动；每个场景约 12 秒。CPU 是进程平均 CPU 除以逻辑处理器数，内存是采样窗口内峰值私有内存。

| 场景 | 优化前 CPU（整机） | 优化后 CPU（整机） | 优化前私有内存 MiB | 优化后私有内存 MiB |
| --- | ---: | ---: | ---: | ---: |
| 首页空闲 | 0.000% | 0.006% | 331.70 | 296.86 |
| 托盘空闲 | 0.071% | 0.019% | 242.04 | 241.61 |
| 托盘专注计时 | 0.520% | 0.135% | 271.37 | 252.00 |
| Widget 专注计时 | 0.289% | 0.186% | 272.73 | 280.06 |

三次启动到 UIAutomation 可用：990.5 / 705.5 / 1136.4 ms。优化前为 876.7 / 797.0 / 864.1 ms；这不支持“启动变快”的结论。80 次切页后私有内存增量从 26.39 降至 15.47 MiB，句柄变化为 +3；这是短时压力观察，不能据此证明无内存泄漏。Widget 场景的内存未改善，仍是后续降低视觉层资源占用的重点。

原始报告：`artifacts/performance/ui-baseline.json`、`artifacts/performance/optimized.json`。数据受缓存、调度、合成与 GC 时机影响；不包含 DWM/GPU 成本，也不是冷启动或数小时运行测试。WPF/运行时/玻璃渲染的内存仍约数百 MiB，不承诺几十 MiB 的极低内存。

## 异常与数据保护

- 启动失败、Dispatcher 异常、未处理异常和未观察任务异常尝试写入本地 `logs/floe.log`。
- 当前日志及上一次日志各限制 256 KiB；异常描述截断为 16,384 字符，在追加前按 UTF-8 字节数轮换。不额外写入任务正文、SQL 数据或会话快照；日志仍包含异常与本机路径，分享前可检查。
- 日志目录不可写时吞掉日志自身的 IO/权限错误，仍显示原始失败并退出。致命错误使用原生对话框，避免依赖可能已经损坏的呈现资源。
- 未预期的 UI 错误提示后退出，不继续运行可能不一致的状态；已有正常退出/检查点行为保留。不能承诺任何崩溃均可保存最后一刻的数据。
- 损坏或较新结构的数据库不会自动重置。目录被普通文件占用时不会替换该文件。重复实例被拒绝，原实例可继续计时及保存。
- 退出时 SQLite 写锁导致保存失败，玻璃确认框的“返回并重试”保留窗口、组件和进度；释放锁后可成功重试并退出。

## 发布产物

版本 `0.1.0-preview.1`，Windows x64，未签名本地预览版。首个发布 RID 在 `Directory.Build.props` 的 `RuntimeIdentifiers` 声明，不改变正常 `bin/Release/net10.0-windows` 输出目录；包依赖版本由各项目 `packages.lock.json` 锁定。

最终包：

`artifacts/releases/Floe-0.1.0-preview.1-win-x64-portable-20261001-070442.zip`

压缩 62.81 MiB，解压约 143.58 MiB；含 .NET / Windows Desktop 10.0.12、自带 SQLite 原生库、依赖许可/通知、版本清单及逐文件 SHA-256。ZIP 使用标准正斜线条目路径。没有打入用户数据库、SDK、测试数据或源代码。

SHA-256：

```text
0b3aae2ea8c129b607a9334573fdfcda98b686dfa2494529e6171355efdd7ac6
```

完整解压后双击包内 `Floe.exe`。不要只移动 EXE，旁边 DLL 需要一起保留；数据默认仍在 `%LOCALAPPDATA%\Floe`，便携应用文件不等于默认数据跟随程序目录。仅应用文件便携，不需要另装 .NET，也不自动安装快捷方式或启用开机启动。

正常退出后复制整个数据目录进行备份。升级时退出 Floe，再替换应用文件，保留数据目录；较新 schema 不支持旧版降级。若开机启动已启用且应用移动位置，从新位置关闭再开启该选项，更新注册路径。

使用目录式自包含发布；不启用裁剪、Native AOT 或单文件临时解包。[微软说明 WPF 目前不兼容裁剪](https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/incompatibilities)。另提供框架依赖发布开关，适合已安装 .NET Desktop Runtime 10 x64 的电脑，体积更小。

## 重建、运行与验证

在项目根目录 PowerShell 中运行：

```powershell
.\scripts\dotnet.ps1 restore .\Floe.sln --locked-mode --configfile .\NuGet.config
.\scripts\dotnet.ps1 build .\Floe.sln -c Release --no-restore
.\scripts\dotnet.ps1 test .\Floe.sln -c Release --no-build --no-restore --logger trx --results-directory .\artifacts\tests
.\scripts\dotnet.ps1 run --project .\src\Floe.Desktop\Floe.Desktop.csproj -c Release --no-build

# 首次发布前按锁定版本还原运行时包（需要 NuGet 网络）。
.\scripts\dotnet.ps1 restore .\src\Floe.Desktop\Floe.Desktop.csproj --runtime win-x64 '/p:SelfContained=true' --locked-mode --configfile .\NuGet.config
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Publish-Windows.ps1
# 可选：不包含运行时。
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Publish-Windows.ps1 -FrameworkDependent
```

每次发布使用时间戳新目录，失败的中间产物保留供排查，不清空既有构建目录。依赖运行时还原完成后，发布使用 `--no-restore`。

```powershell
$package = '.\artifacts\releases\Floe-0.1.0-preview.1-win-x64-portable-20261001-070442'
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Verify-Package.ps1 -PackageDirectory $package -Archive ($package + '.zip')
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-Shell.ps1 -AppDirectory $package -UseAppHost -UiOnly
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-Shell.ps1 -AppDirectory $package -UseAppHost -SystemOnly
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-Shell.ps1 -AppDirectory $package -UseAppHost -FailureOnly
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-Shell.ps1 -AppDirectory $package -UseAppHost -IncludeCompletion
# 原构建目录的短时资源采样。
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-Shell.ps1 -PerformanceOnly
```

桌面脚本只操作自己启动的进程，使用 `artifacts/smoke/data-<GUID>` 隔离数据库；不读取个人数据或修改真实开机启动项。`-UseAppHost` 直接启动 EXE，并在自包含模式核对加载的 coreclr 来自发布目录，避免通过开发 SDK 掩盖漏包。

## 验证结果

- Debug、Release 构建：零警告、零错误。解决方案锁定还原通过。
- 自动测试 146/146：Domain 59、Application 66、Infrastructure 21；包含可控时间、跨日、互斥与重复记账、恢复及 SQLite 保护。
- 发布 EXE 完整桌面回归通过，真实一分钟专注结束；包含 Todo/绑定/统计、自由计时、键盘、主题、实色回退、窗口、Widget 真鼠标拖动、正常重启、强制结束恢复与撤销跨重启。数据库 `data-b27cc28134e04ba5a5b7c325775500e3`。
- 最终包 UI 专项通过，两种默认首页均无需滚动；窗口控制、原生 HTMAXBUTTON、Logo、Popup、组件、玻璃/实色及关闭托盘。数据库 `data-484f59f024d947358ee6c8d4fd91bc4e`。
- 最终包系统专项通过：托盘后台计时、隐藏后恢复、窗口最大化恢复、组件、退出重试与偏好恢复。使用真实 SQLite 写锁验证退出失败后保留窗口，释放锁后正常保存；数据库 `data-803938e9cb8641bfad636061e87eceb2`。
- 异常启动专项通过，逐字节哈希核对原文件；包含损坏、未来 schema、不可用路径和重复实例。数据库 `data-dd06d3891f874454aa057c5a6a243506`。
- 包验证通过：426 个文件校验、运行时存在、ZIP 总哈希和每个条目内容。
- 完整回归/异常检查使用的先前包，与最终包的四个 Floe 程序集、coreclr 及 SQLite 原生 DLL 的 SHA-256 一致。只修正了归档工具和构建目标声明，不以不同代码的结果代替最终包验证。

## 新增与修改文件

新增：

- `src/Floe.Desktop/Diagnostics/DiagnosticLog.cs`
- `src/Floe.Desktop/Properties/PublishProfiles/Windows-x64.pubxml`
- `scripts/Publish-Windows.ps1`、`scripts/Verify-Package.ps1`
- `docs/phase-12.md`、`CHANGELOG.md`

修改：

- `App.xaml.cs`：异常入口与诊断。
- `Features/Home/HomePageViewModel.cs`、`HomePage.xaml.cs`：可见期间订阅及恢复刷新。
- `Features/Tasks/TasksPageViewModel.cs`、`TasksPage.xaml.cs`：隐藏时停止计时展示刷新，草稿保留。
- `Features/Statistics/StatisticsPage.xaml.cs`：整窗隐藏时取消订阅。
- `MainWindow.xaml`：移除整面软件模糊。
- `Themes/Materials/MaterialPalette.cs`：实色 Popup 复用系统颜色画刷。
- `Floe.Desktop.csproj`、`Directory.Build.props`、七个 `packages.lock.json`：版本与发布依赖锁定。
- `scripts/Test-Shell.ps1`：资源采样、发布 EXE、运行时路径、异常启动验证。
- `README.md`、`docs/ui-refinement.md`：运行与交付说明更新。

UI 精修详细文件与视觉取舍见 [UI 精修说明](ui-refinement.md)。

## 已知验证边界

实际验证环境为 Windows 11 x64 / 150% DPI。Windows 10 的玻璃降级已实现，但未做 Win10 实机验收；混合 DPI 多屏、真实 Snap 菜单、高对比完整操作、远程会话及数小时性能还需相应环境测试。目前微软 .NET 10 对 Windows 10 的官方支持限于仍受支持的 LTSC/Enterprise 版本，不能把视觉兼容降级等同于支持所有已停止维护的 Win10 版本。[官方 Windows 支持表](https://learn.microsoft.com/en-us/dotnet/core/install/windows)。

本次交付的是可运行的本地预览发布包；未提供代码签名、安装程序/MSIX、自动更新或公开下载部署。无需账号，数据保留在本地。下一次扩大平台支持或公开发布前，应按目标系统补做验证与签名。

# UI 精修 · Phase 11 后续

按本次要求先完成 UI 精修，再进入 Phase 12。专注状态机、统一记账、Todo、SQLite 数据结构与计时规则未更改。

## 界面与 Windows 体验

- 移除系统标题栏的可见样式，使用 WindowChrome 延伸应用内容；顶部没有图标、标题或分割线。右上角保留轻量最小化、最大化/还原、关闭。拖动、边缘调整大小、双击顶部、Alt+F4 与 Alt+Space/顶部右键窗口菜单保留。最大化按钮返回 HTMAXBUTTON 以支持 Windows 11 Snap。
- 主窗口默认 1080×760 DIP。模式切换、任务选择收进同一张专注卡片，压缩重复说明及上下间距；番茄与自由计时两种默认布局均无需滚动，三项统计完整显示。较小窗口或错误提示展开时仍允许滚动。
- 删除首页右上角“外观”；外观、通知及组件配置仍在设置。
- 取消额外的外围焦点矩形。按钮、选项、输入与开关通过背景亮度、透明度及选中填充显示键盘焦点；Hover、Pressed、Tab 和快捷键仍可用。
- 使用 Segoe UI Variable Text / Display，旧系统回退 Segoe UI / Microsoft YaHei UI。没有复制或捆绑 Apple SF Pro 字体。

## Logo

根目录 `floe.png` 是唯一图形来源；原图 912×912。`Assets/Floe.png` 与原图 SHA-256 完全一致。`scripts/Build-Icon.ps1` 改为转换脚本，生成 16、24、32、48、64、128、256 像素 ICO，不再绘制旧时钟 Logo。Windows ICO 的常用最大帧是 256×256；512×512 或更大的 PNG 可保留作为高分辨率源。

侧栏显示原始 Logo 配合轻量 floe 字标；EXE、任务栏/桌面快捷方式、窗口身份和系统托盘使用同一 ICO。托盘优先读取 32px 帧，避免放大 16px。窗口顶部不显示系统小图标；ICO 仍供 Windows 识别使用。没有为测试向用户桌面自动创建快捷方式；正式压缩包可自行发送 Floe.exe 的快捷方式到桌面。

## 玻璃材质与兼容

Windows 11 build 22621 及以上优先使用公开 DWM `DWMSBT_TRANSIENTWINDOW` 桌面 Acrylic。主窗口、组件和应用内错误/保存确认对话框使用非分层窗口及原生玻璃帧，保留 Windows 合成器管理模糊，不用屏幕采样或每帧截图。WindowChrome 保留所需原生样式但隐藏 DWM 的重复控制按钮。

卡片、按钮和输入的半透明层上叠加方向高光、细微边缘与静态柔和阴影；主窗口不使用整面软件模糊，局部 Popup 装饰的 BlurEffect 不作用于文字。下拉和菜单是 WPF 独立 Popup，当前使用较高不透明度的玻璃渐变、边缘及局部 Gaussian Blur 装饰模拟材质，**不声称这些 Popup 已实时模糊其背后的桌面**。

Windows 10、旧 Win11、关闭系统透明效果、减少透明效果、高对比、远程会话、节电或不满足渲染条件时回退，保证文字可读。系统决定原生 Acrylic 的实际呈现、激活/节能状态下可能减弱或停用；API 设置成功不等于每个场景均具有相同透射效果。

组件固定窗口 Opacity 为 1，文字不透明；玻璃透射由设计资源和系统材质控制。删除用户透明度输入及其显示层草稿/应用命令。数据库中的旧 Opacity 字段为兼容保留但不用于视觉，不重写历史设置或迁移数据。

后续组件精简为时间、任务和 ×，移除阶段、收起及底部控制按钮。点击主体回到首页，按住鼠标移动即拖动，无延时；普通 240×156、紧凑 240×140 DIP。计时操作仍在主窗口或托盘，置顶与锁定可在右键菜单或设置中调整，紧凑模式在设置中选择。最新验收见 [组件精简说明](widget-refinement.md)。

## 文件清单

新增 `Platform/IntegratedWindowChrome.cs`、`Controls/GlassDialog.cs`、`Assets/Floe.png` 与本说明。修改 Desktop 的 MainWindow、HomePage、玻璃控件、按钮反馈、WindowBackdrop、TrayIcon、主题色/字体/模板、Widget 窗口/布局/显示管理/设置面板，以及 App、DesktopSession 中的对话框展示。修改 `Floe.Desktop.csproj` 资源、`scripts/Build-Icon.ps1`、`scripts/Test-Shell.ps1` 与 README。Domain、Application、Infrastructure 的业务代码及数据库 schema 4 保持。

## 验收

```powershell
.\scripts\dotnet.ps1 build .\Floe.sln -c Release --no-restore
.\scripts\dotnet.ps1 run --project .\src\Floe.Desktop\Floe.Desktop.csproj -c Release --no-build
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-Shell.ps1 -UiOnly
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-Shell.ps1 -MotionOnly
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-Shell.ps1 -SystemOnly
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-Shell.ps1 -IncludeCompletion
```

Windows 11 x64 / 150% DPI：Release 构建零警告、零错误；UI 专项检查两种首页无滚动、统计无裁切、控件属于主卡片、Logo、窗口操作及 Snap 命中、下拉、组件、减少透明与关闭托盘。最终 UI 数据库 `artifacts/smoke/data-484f59f024d947358ee6c8d4fd91bc4e`。

系统回归验证玻璃保存失败对话框选择“返回并重试”保持窗口与进度，释放真实 SQLite 写锁后恢复保存和退出；数据库 `artifacts/smoke/data-c33ff00072db48368a96265840f89697`。完整回归通过真实一分钟番茄、Todo、绑定、统计、自由计时互斥、主题、键盘、最小窗口、强制终止恢复、跨重启撤销和组件拖动/配置恢复；数据库 `artifacts/smoke/data-1da0d11dd8ae4b1bb3476c72b9adf39f`。

原生材质使用屏幕合成结果截图，避免把 PrintWindow 的 WPF 内容误当作 DWM 最终效果；截图在 `artifacts/smoke/UI-*.png`。Win10 实机、多显示器混合 DPI、真实 Snap 菜单显示及系统高对比完整操作仍需对应环境验收。

平台依据：[WindowChrome](https://learn.microsoft.com/en-us/dotnet/api/system.windows.shell.windowchrome)、[DWM 系统材质及最低版本](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwm_systembackdrop_type)、[Windows 11 Snap 命中](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/ui/apply-snap-layout-menu)。

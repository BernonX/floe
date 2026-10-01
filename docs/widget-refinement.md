# 桌面组件精简 · 0.1.0-preview.3

本次仅调整组件视觉、鼠标/键盘交互及其打开首页的展示路径，不修改专注状态机、记录、任务或 schema 4。

## 当前界面

- 只有时间、当前任务及右上角 ×；× 始终可见，点击仅隐藏组件。
- 删除“专注/自由专注”阶段标题、“收起/展开”、底部模式/开始/暂停/跳过/打开按钮。
- 宽度由 300 缩至 240 DIP；普通尺寸 240×156，设置中的紧凑尺寸 240×140。时间字号 42，较长任务名省略并保留悬浮全文。
- 保留原生 Acrylic、圆角及浅色/深色/实色回退。计时操作在首页或系统托盘完成。
- 右键菜单仅保留置顶、锁定位置和隐藏；紧凑模式在设置页调整。

## 点击与拖动

组件主体的时间、任务、留白都可单击，直接显示并激活主窗口首页。已最小化的主窗口会恢复之前的正常/最大化状态。关闭按钮不触发打开首页。

按住鼠标移动即进入 Windows 原生拖动，没有长按计时或等待；越过系统的少量移动距离阈值后立即应用首次位移，以区分点击时的轻微手抖。拖动结束保存位置且不打开主窗口。锁定时不能移动，单击仍可打开首页。释放鼠标捕获、隐藏、关闭、失去激活或按 Escape 会取消待处理手势。拖动完成后的屏幕恢复和位置约束保持。

删除拖动 DispatcherTimer 和点击时长判断，只依据鼠标按下与移动事件区分点击和拖动，空闲时不轮询。组件窗口提供整体无障碍 Invoke；Enter/Space 保留打开首页的键盘入口。

## 文件

修改：

- `src/Floe.Desktop/Widgets/WidgetWindow.xaml`：删除控制区，只留下 × 和内容。
- `src/Floe.Desktop/Widgets/WidgetWindow.xaml.cs`：单击/即时拖动分辨、捕获清理、整体无障碍激活。
- `src/Floe.Desktop/Widgets/WidgetLayoutDefinition.cs`：新尺寸。
- `src/Floe.Desktop/Widgets/FocusWidgetView.xaml`：更小时间字号。
- `src/Floe.Desktop/Widgets/FocusWidgetViewModel.cs`：交互提示。
- `src/Floe.Desktop/Widgets/WidgetManager.cs`：转发组件主体打开事件。
- `src/Floe.Desktop/Composition/DesktopSession.cs`：组件入口导航到首页；托盘打开仍保留现有页面行为。
- `src/Floe.Desktop/Floe.Desktop.csproj`：版本 `0.1.0-preview.3`。
- `scripts/Test-Shell.ps1`：真实点击/即时拖动/关闭与锁定回归；在鼠标仍按住且不足原 350ms 等待时间时核对窗口已移动。原组件按钮测试改为首页/托盘操作，紧凑动画改由设置操作。
- `README.md`、`CHANGELOG.md`、`docs/ui-refinement.md`。

新增本说明。未引入依赖，历史设置和数据库保持兼容。

## 运行和验证

原 Release 构建目录保持 `src/Floe.Desktop/bin/Release/net10.0-windows`：

```powershell
.\scripts\dotnet.ps1 build .\Floe.sln -c Release --no-restore
.\scripts\dotnet.ps1 run --project .\src\Floe.Desktop\Floe.Desktop.csproj -c Release --no-build
```

新版自包含包：`artifacts/releases/Floe-0.1.0-preview.3-win-x64-portable-20261001-080945.zip`，62.81 MiB，完整解压后双击 `Floe.exe`。数据仍在原目录，退出旧实例后运行新版。包内包含运行时，未签名；平台验证边界与备份方式仍见 [Phase 12](phase-12.md)。

ZIP SHA-256：`f17d1b0ce81e4eab91d9ed70f5596803072ee20f30f6f45fb82d66a8395f2e9b`。

```powershell
$package = '.\artifacts\releases\Floe-0.1.0-preview.3-win-x64-portable-20261001-080945'
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-Shell.ps1 -AppDirectory $package -UseAppHost -WidgetOnly
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-Shell.ps1 -AppDirectory $package -UseAppHost -MotionOnly
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-Shell.ps1 -AppDirectory $package -UseAppHost -UiOnly
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Verify-Package.ps1 -PackageDirectory $package -Archive ($package + '.zip')
```

Windows 11 x64 / 150% DPI 验证通过：

- Release 构建零警告、零错误。
- 新版 EXE 真实鼠标点击时间/任务/留白、即时拖动、锁定、最大化/最小化恢复、× 仅隐藏。按下 20ms 后移动，在继续按住的 80ms 观察期内确认窗口已移动，松手不打开首页。自由计时和任务同步、整体无障碍激活、紧凑内容完整、玻璃降级，以及位置/暂停状态跨重启恢复通过。独立数据库 `data-ea3fbedb6c36497595e1afa6f1b27e63`。
- 相同版式在 preview.2 的动画专项通过设置紧凑模式快速反向、减少动态效果立即结束、位置锁定、任务绑定和草稿保留；数据库 `data-8f8d4af065e7460db0a697f95ac823f7`。本次没有修改动画或版式。
- 相同版式在 preview.2 的 UI 专项通过两种首页无滚动、窗口按钮、Snap 命中、主题、原生材质和实色回退；数据库 `data-8ac09b98a8924832bad9df959a76ab9e`。
- 426 个包文件、ZIP 哈希与条目内容校验通过。测试直接运行发布 EXE，并核对加载包内运行时；测试实例全部退出。

截图：`artifacts/smoke/Widget-minimal-light.png`、`Widget-minimal-dark.png`。

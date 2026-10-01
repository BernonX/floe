# Phase 2：Liquid Glass UI 基础组件

状态：完成；等待确认后进入 Phase 3。本阶段没有增加计时业务、数据库或 Widget。

## 完成内容

- 建立独立的颜色/尺寸/字体设计变量和 Light、Dark、HighContrast 资源。
- 实现 `GlassSurface`：多层半透明填充、渐变边缘高光、轻微内缘明暗、独立表面阴影。
- 实现只负责展示的 `TimerDisplay`，支持时间文字、阶段标签和等宽数字。首页 25:00 为标注清楚的静态预览，按钮禁用，统计用破折号占位。
- 统一主操作、次级、轻量按钮样式；180ms EaseOut Hover、按下反馈、禁用状态和清晰键盘焦点。
- 统一导航项、主题 RadioButton、CheckBox 开关与垂直滚动条，保留 WPF 的原生交互/自动化语义。
- 设置页可切换跟随系统/浅色/深色、减少透明、减少动态，以及恢复默认。所有选择暂只保存在内存。
- 更新四个页面的表面、排版和留白；原生标题栏、窗口缩放、最大化、系统菜单继续由 Windows 管理。
- Windows 11 22621+ 使用公开 DWM 背景属性接入 Mica；失败或条件不满足时恢复不透明窗口背景。

## 模块边界

`Themes/Tokens` 保存 XAML 设计变量，`Materials/MaterialPalette` 从当前变量生成冻结画刷。切换主题时整体替换 palette，控件通过 DynamicResource 引用画刷，避免 Freezable 内部颜色仍引用旧字典的问题。

`Themes/Templates` 负责可复用控件模板；`Controls` 只定义展示属性，不拥有计时器。`ThemeService` 负责主题资源与系统偏好响应，`Platform/WindowBackdrop` 负责 HWND/DWM 生命周期。MainWindow 只组装窗口适配器并在关闭时释放。

系统事件在 Dispatcher 上合并处理，退出时解除订阅；没有常驻渲染循环或主题轮询。画刷冻结后共享，阴影只作用于表面几何，不模糊文字子树。减少透明或高对比度时隐藏阴影层。

没有新增 NuGet 依赖，Domain、Application、Infrastructure 的业务范围与项目引用保持原状。

## 材质与兼容边界

Mica 是系统壁纸相关材质，不是实时透视后方窗口的 Acrylic，更不是 Apple 的真实折射。当前玻璃感由系统底层材质、局部低饱和氛围色、表面层次与高光共同构成，没有截图桌面、昂贵 Shader 或持续背景动画。

公开 DWM 的 SystemBackdrop 属性从 Windows 11 Build 22621 起可用；代码做系统版本判断并检查返回值。Windows 10、较早 Windows 11 或 API 失败时使用不透明窗口与应用表面层次，不调用未公开的 Acrylic 接口。

系统高对比度使用用户选择的 SystemColors 并禁用材质/动画。关闭系统透明效果、远程会话、节能模式或渲染能力不足时限制材质；系统动画关闭时不播放按钮过渡。读取主题/透明度注册表偏好失败时，使用浅色及关闭透明的保守默认值。

系统透明和主题偏好通过 Windows 用户偏好事件刷新，电源状态通过电源事件刷新。系统合成器仍可能因硬件、远程连接或非活动窗口状态自行降级，DWM 返回成功不等于每一帧都有材质。

## 新增文件

相对 `src/Floe.Desktop`：

| 文件 | 作用 |
| --- | --- |
| `Controls/GlassSurface.cs` | 玻璃表面展示控件 |
| `Controls/TimerDisplay.cs` | 不包含计时逻辑的时间展示控件 |
| `Controls/SurfaceProperties.cs` | 模板继承的材质/动画开关 |
| `Themes/ThemePreference.cs` | 主题选项 |
| `Themes/ThemeService.cs` | 主题资源和系统偏好生命周期 |
| `Themes/Tokens/Light.xaml` | 浅色变量 |
| `Themes/Tokens/Dark.xaml` | 深色变量 |
| `Themes/Tokens/HighContrast.xaml` | 系统高对比度画刷 |
| `Themes/Tokens/Metrics.xaml` | 字体、圆角、间距与动效时间 |
| `Themes/Materials/MaterialPalette.cs` | 从当前变量生成不可变材质画刷 |
| `Themes/Templates/Typography.xaml` | 排版样式 |
| `Themes/Templates/Surfaces.xaml` | 玻璃卡片与计时展示模板 |
| `Themes/Templates/Buttons.xaml` | 三类按钮与状态反馈 |
| `Themes/Templates/Selection.xaml` | 导航、主题选择与开关 |
| `Themes/Templates/Scrolling.xaml` | 细垂直滚动条 |
| `Platform/WindowsAppearance.cs` | 系统主题/透明度/节能状态读取 |
| `Platform/WindowBackdrop.cs` | 原生材质、标题栏与清理 |

另新增本说明 `docs/phase-2.md`。

## 修改文件

- `App.xaml`、`App.xaml.cs`：加载资源，持有并释放 ThemeService。
- `Composition/CompositionRoot.cs`：注入主题服务，提供轻量导航几何。
- `Shell/MainWindow.xaml`、`MainWindow.xaml.cs`：新外壳、系统材质适配。
- `Shell/ShellViewModel.cs`：外观导航命令。
- `Shell/NavigationItemViewModel.cs`：图标几何替代序号。
- `Shell/ShellResources.xaml`：只保留页面映射，主题资源移至独立目录。
- `Features/Home/HomePage.xaml`：静态计时与摘要组件预览。
- `Features/Tasks/TasksPage.xaml`、`Features/Statistics/StatisticsPage.xaml`：统一表面和空状态。
- `Features/Settings/SettingsPage.xaml`、`SettingsPageViewModel.cs`：实际可操作的外观选择。
- 根目录 `README.md`、`scripts/Test-Shell.ps1`：更新运行说明和自动化验收。

## 运行与测试

```powershell
cd E:\Codex\floe
.\scripts\dotnet.ps1 build .\Floe.sln -c Release --no-restore
.\scripts\dotnet.ps1 run --project .\src\Floe.Desktop\Floe.Desktop.csproj -c Release --no-build
```

在新工作区先按 README 还原依赖。自动化验收：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-Shell.ps1
```

本次在 Windows 11 Build 26200、150% 缩放下验证：

- Release 和 Debug 构建均为 0 警告、0 错误。
- 四个页面加载及标题同步通过。
- 浅/深色切换通过；对截图的同一表面位置做亮度断言，浅色约 242、深色约 45，避免只检查选项值而漏掉残留浅色画刷。
- 深色首页、深色设置、实色降级截图完成目视检查。
- 减少透明与减少动态后，运行状态为 `Theme=Dark;Backdrop=Solid;Motion=False`。
- 恢复默认后，运行状态为 `Theme=Light;Backdrop=Mica;Motion=True`，各选项也恢复。
- 真实方向键导航、Tab 焦点转移和空格激活按钮通过。
- 最小窗口下可以滚动到内容底部，缩放/最大化/还原通过。
- 关闭后进程退出码为 0。

截图位于 `artifacts/smoke`，包括 `HomePage.png`、`HomePage-dark.png`、`SettingsPage-light.png`、`SettingsPage-dark.png`、`SettingsPage-solid.png`、`HomePage-minimum.png` 和 `HomePage-minimum-scrolled.png`。

## 未实测场景

没有改动用户的 Windows 系统主题/高对比度设置，因此系统级高对比度热切换、真实节能/RDP 降级尚未实测。Windows 10 实机、多显示器和 100%/200% DPI 也尚未实测。已验证的“减少透明”实色分支不等于完成全部平台兼容验收。

更广泛的页面动画和 Widget 动画仍属于 Phase 11；本阶段仅实现基础按钮反馈。下阶段为 Phase 3 番茄钟核心状态机，须用户确认后开始。

## 官方依据

- [DWM 窗口属性与最低版本](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwmwindowattribute)
- [WPF 窗口与 DWM 客户区集成](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/graphics-multimedia/extend-glass-frame-into-a-wpf-application)
- [Mica 材质说明](https://learn.microsoft.com/en-us/windows/apps/design/style/mica)

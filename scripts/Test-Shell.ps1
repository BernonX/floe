# Real WPF integration smoke test. Run with Windows PowerShell in a desktop session.
# Only the process launched by this script is manipulated or closed.
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',
    [switch] $IncludeCompletion,
    [switch] $WidgetOnly,
    [switch] $SystemOnly,
    [switch] $MotionOnly,
    [switch] $UiOnly,
    [switch] $PerformanceOnly,
    [switch] $FailureOnly,
    [switch] $UseAppHost,
    [string] $AppDirectory
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class FloeWindowCapture
{
    private delegate bool EnumWindowCallback(IntPtr hwnd, IntPtr data);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowCallback callback, IntPtr data);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint process);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern int GetWindowText(IntPtr hwnd, System.Text.StringBuilder text, int count);
    public static string OwnWindows(int process) {
        var result=new System.Text.StringBuilder();
        EnumWindows((hwnd,data)=>{ uint id; GetWindowThreadProcessId(hwnd,out id); if(id==(uint)process) {
            var title=new System.Text.StringBuilder(256); GetWindowText(hwnd,title,256); result.AppendFormat("[{0}:{1}]",hwnd,title);
        } return true; },IntPtr.Zero); return result.ToString();
    }
    public static IntPtr OwnWindow(int process, string name) {
        IntPtr result=IntPtr.Zero;
        EnumWindows((hwnd,data)=>{ uint id; GetWindowThreadProcessId(hwnd,out id); if(id==(uint)process) {
            var title=new System.Text.StringBuilder(256); GetWindowText(hwnd,title,256);
            if(title.ToString()==name) { result=hwnd; return false; }
        } return true; },IntPtr.Zero); return result;
    }
    [DllImport("user32.dll", CharSet=CharSet.Unicode)]
    public static extern IntPtr FindWindow(string className, string title);
    public static IntPtr FindTrayWindow(string title) { return FindWindow(null, title); }
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindowVisible(IntPtr hwnd);
    [StructLayout(LayoutKind.Sequential)]
    public struct IconIdentifier { public uint Size; public IntPtr Window; public uint Id; public Guid Guid; }
    [StructLayout(LayoutKind.Sequential)]
    public struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("shell32.dll", EntryPoint="Shell_NotifyIconGetRect")]
    private static extern int GetIconRect(ref IconIdentifier identifier, out Rect rect);
    public static bool HasTrayIcon(IntPtr window) {
        var identifier = new IconIdentifier { Size=(uint)Marshal.SizeOf(typeof(IconIdentifier)), Window=window, Id=1 };
        Rect rect; return GetIconRect(ref identifier, out rect)==0 && rect.Right>rect.Left;
    }
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll", EntryPoint="GetWindowLongW")]
    public static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")]
    public static extern uint GetDpiForWindow(IntPtr hwnd);
    [StructLayout(LayoutKind.Sequential)] private struct ScreenPoint { public int X, Y; }
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(ScreenPoint point);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
    public static bool PointBelongsToWindow(IntPtr window, int x, int y) {
        return GetAncestor(WindowFromPoint(new ScreenPoint { X=x, Y=y }),2)==window;
    }
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")]
    public static extern IntPtr SendMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")]
    public static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extra);
}
public sealed class FloeDatabaseWriteLock : IDisposable {
    private IntPtr connection;
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] private static extern IntPtr LoadLibrary(string path);
    [DllImport("e_sqlite3.dll", CallingConvention=CallingConvention.Cdecl, CharSet=CharSet.Ansi)]
    private static extern int sqlite3_open_v2(string path, out IntPtr connection, int flags, IntPtr vfs);
    [DllImport("e_sqlite3.dll", CallingConvention=CallingConvention.Cdecl, CharSet=CharSet.Ansi)]
    private static extern int sqlite3_exec(IntPtr connection, string sql, IntPtr callback, IntPtr context, out IntPtr error);
    [DllImport("e_sqlite3.dll", CallingConvention=CallingConvention.Cdecl)] private static extern int sqlite3_close(IntPtr connection);
    [DllImport("e_sqlite3.dll", CallingConvention=CallingConvention.Cdecl)] private static extern void sqlite3_free(IntPtr memory);
    public static void CreateFutureDatabase(string library, string database) {
        if(LoadLibrary(library)==IntPtr.Zero) throw new InvalidOperationException("SQLite test library could not be loaded.");
        IntPtr handle=IntPtr.Zero, error=IntPtr.Zero;
        try {
            if(sqlite3_open_v2(database,out handle,6,IntPtr.Zero)!=0 ||
               sqlite3_exec(handle,"CREATE TABLE preserved(value TEXT); INSERT INTO preserved VALUES('future data'); PRAGMA user_version=99;",IntPtr.Zero,IntPtr.Zero,out error)!=0)
                throw new InvalidOperationException("Could not create the future-version fixture.");
        } finally { if(error!=IntPtr.Zero) sqlite3_free(error); if(handle!=IntPtr.Zero) sqlite3_close(handle); }
    }
    public FloeDatabaseWriteLock(string library, string database) {
        if(LoadLibrary(library)==IntPtr.Zero) throw new InvalidOperationException("SQLite test library could not be loaded.");
        IntPtr error=IntPtr.Zero;
        try {
            if(sqlite3_open_v2(database,out connection,2,IntPtr.Zero)!=0 || sqlite3_exec(connection,"BEGIN IMMEDIATE;",IntPtr.Zero,IntPtr.Zero,out error)!=0)
                throw new InvalidOperationException("Could not acquire the test database write lock.");
        } catch { Dispose(); throw; }
        finally { if(error!=IntPtr.Zero) sqlite3_free(error); }
    }
    public void Dispose() { if(connection!=IntPtr.Zero) { sqlite3_close(connection); connection=IntPtr.Zero; } }
}
'@

$workspaceRoot = Split-Path -Parent $PSScriptRoot
$appPath = Join-Path $workspaceRoot "src/Floe.Desktop/bin/$Configuration/net10.0-windows/Floe.dll"
if ($AppDirectory) { $appPath = Join-Path (Resolve-Path -LiteralPath $AppDirectory) 'Floe.dll' }
if (-not (Test-Path -LiteralPath $appPath)) { throw 'Build Floe.Desktop before running the smoke test.' }
$localSdk = Join-Path $workspaceRoot '.tools/dotnet/dotnet.exe'
$dotnetCommand = if (Test-Path -LiteralPath $localSdk) { $localSdk } else { (Get-Command dotnet).Source }
$captureDirectory = Join-Path $workspaceRoot 'artifacts/smoke'
New-Item -ItemType Directory -Force -Path $captureDirectory | Out-Null
$testDataDirectory = Join-Path $captureDirectory ('data-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $testDataDirectory | Out-Null

function Start-TestApplication([switch] $Background) {
    $previousDirectory = [Environment]::GetEnvironmentVariable('FLOE_DATA_DIRECTORY', 'Process')
    try {
        [Environment]::SetEnvironmentVariable('FLOE_DATA_DIRECTORY', $testDataDirectory, 'Process')
        $arguments = '"{0}"' -f $appPath
        if ($Background) { $arguments += ' --background' }
        if ($UseAppHost) {
            $executable = Join-Path (Split-Path -Parent $appPath) 'Floe.exe'
            if ($Background) { return Start-Process -FilePath $executable -ArgumentList '--background' -WorkingDirectory $workspaceRoot -WindowStyle Hidden -PassThru }
            return Start-Process -FilePath $executable -WorkingDirectory $workspaceRoot -WindowStyle Hidden -PassThru
        }
        return Start-Process -FilePath $dotnetCommand -ArgumentList $arguments -WorkingDirectory $workspaceRoot -WindowStyle Hidden -PassThru
    } finally { [Environment]::SetEnvironmentVariable('FLOE_DATA_DIRECTORY', $previousDirectory, 'Process') }
}

function Wait-ApplicationWindow($process) {
    $processCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $process.Id)
    $idCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, 'FloeMainWindow')
    $condition = New-Object System.Windows.Automation.AndCondition($processCondition, $idCondition)
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    do {
        if ($process.HasExited) { throw "Floe exited during startup: $($process.ExitCode)" }
        $root = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, $condition)
        if ($null -eq $root) { Start-Sleep -Milliseconds 100 }
    } while ($null -eq $root -and [DateTime]::UtcNow -lt $deadline)
    if ($null -eq $root -or $root.Current.AutomationId -ne 'FloeMainWindow') { throw 'Floe main window did not appear.' }
    if ($UseAppHost -and (Test-Path -LiteralPath (Join-Path (Split-Path -Parent $appPath) 'coreclr.dll'))) {
        $runtime = $process.Modules | Where-Object { $_.ModuleName -eq 'coreclr.dll' } | Select-Object -First 1
        $expectedRuntime = Join-Path (Split-Path -Parent $appPath) 'coreclr.dll'
        if ($null -eq $runtime -or $runtime.FileName -ne $expectedRuntime) { throw 'Portable EXE did not load its bundled runtime.' }
    }
    return $root
}

function Read-NavigationItems($root) {
    $condition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)
    return (Find-ById $root 'MainNavigation').FindAll([System.Windows.Automation.TreeScope]::Children, $condition)
}

function Find-WidgetWindow($process) {
    $processCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $process.Id)
    $idCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, 'FloeWidgetWindow')
    $condition = New-Object System.Windows.Automation.AndCondition($processCondition, $idCondition)
    return [System.Windows.Automation.AutomationElement]::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, $condition)
}

function Wait-WidgetWindow($process) {
    $deadline = [DateTime]::UtcNow.AddSeconds(5)
    do {
        $widget = Find-WidgetWindow $process
        if ($null -ne $widget -and -not $widget.Current.IsOffscreen) { return $widget }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)
    throw 'Visible widget did not appear.'
}

function Is-WidgetVisible($process) {
    $widget = Find-WidgetWindow $process
    return $null -ne $widget -and -not $widget.Current.IsOffscreen
}

function Read-WidgetTimer($root) {
    return (Find-ById $root 'WidgetTimer').GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
}

function Toggle-ById($root, [string] $id) {
    (Find-ById $root $id).GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
    Start-Sleep -Milliseconds 220
}

function Find-ById($root, [string] $automationId) {
    if ($null -eq $root) { throw ('Missing parent while finding control: ' + $automationId) }
    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty, $automationId)
    return $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
}

function Invoke-ById($root, [string] $automationId) {
    $element = Find-ById $root $automationId
    if ($null -eq $element) { throw "Control not found: $automationId" }
    if (-not $element.Current.IsEnabled) { throw "Control is disabled: $automationId" }
    $element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Milliseconds 220
}

function Read-TimerValue($root) {
    $timer = Find-ById $root 'TimerDisplay'
    if ($null -eq $timer) { throw 'Accessible timer display not found.' }
    return $timer.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
}

function Read-FreeValue($root) {
    $timer = Find-ById $root 'FreeTimerDisplay'
    return $timer.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
}

function Parse-PomodoroTime([string] $value) {
    return [TimeSpan]::ParseExact($value, 'mm\:ss', [Globalization.CultureInfo]::InvariantCulture)
}

function Set-Input($root, [string] $id, [string] $value) {
    (Find-ById $root $id).GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($value)
    Start-Sleep -Milliseconds 50
}

function Select-ById($root, [string] $id) {
    (Find-ById $root $id).GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 220
}

function Select-FocusTask($root, [string] $title) {
    $combo = Find-ById $root 'FocusTaskSelector'
    if (-not $combo.Current.IsEnabled) { throw 'Task choice unexpectedly locked.' }
    $combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
    Start-Sleep -Milliseconds 220
    $nameCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $title)
    $typeCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)
    $condition = New-Object System.Windows.Automation.AndCondition($nameCondition, $typeCondition)
    $choice = $combo.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
    if ($null -eq $choice) { throw "Task choice not found: $title" }
    $choice.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    $combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Collapse()
    Start-Sleep -Milliseconds 220
}

function Find-TaskRow($root, [string] $title) {
    $list = Find-ById $root 'TaskList'
    $nameCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $title)
    $typeCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)
    $condition = New-Object System.Windows.Automation.AndCondition($nameCondition, $typeCondition)
    return $list.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
}

function Toggle-Task($root, [string] $title) {
    $row = Find-TaskRow $root $title
    if ($null -eq $row) { throw "Task row not found: $title" }
    (Find-ById $row 'TaskComplete').GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
    Start-Sleep -Milliseconds 220
}

function Send-EditorKey($root, [string] $keys) {
    $handle = [IntPtr]$root.Current.NativeWindowHandle
    [void][FloeWindowCapture]::SetForegroundWindow($handle)
    (Find-ById $root 'TaskTitleInput').SetFocus()
    Start-Sleep -Milliseconds 100
    if ([FloeWindowCapture]::GetForegroundWindow() -ne $handle) { return $false }
    [System.Windows.Forms.SendKeys]::SendWait($keys)
    Start-Sleep -Milliseconds 220
    return $true
}

function Scroll-Content($root, [double] $percent) {
    $hostElement = Find-ById $root 'MainContentScroll'
    $scroll = $hostElement.GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern)
    if ($scroll.Current.VerticallyScrollable) { $scroll.SetScrollPercent(-1, $percent); Start-Sleep -Milliseconds 220 }
}

function Save-WindowCapture($window, [string] $fileName) {
    $bounds = $window.Current.BoundingRectangle
    $bitmap = New-Object System.Drawing.Bitmap([int]$bounds.Width, [int]$bounds.Height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $hdc = $graphics.GetHdc()
    try {
        if (-not [FloeWindowCapture]::PrintWindow([IntPtr]$window.Current.NativeWindowHandle, $hdc, 2)) {
            throw 'PrintWindow failed.'
        }
    } finally {
        $graphics.ReleaseHdc($hdc)
        $graphics.Dispose()
    }
    try {
        $bitmap.Save((Join-Path $captureDirectory $fileName), [System.Drawing.Imaging.ImageFormat]::Png)
    } finally {
        $bitmap.Dispose()
    }
}

function Get-SurfaceBrightness([string] $fileName) {
    $bitmap = [System.Drawing.Bitmap]::FromFile((Join-Path $captureDirectory $fileName))
    try {
        # Sample a clean patch inside the appearance surface, away from text and controls.
        $pixel = $bitmap.GetPixel([int]($bitmap.Width * 0.9), [int]($bitmap.Height * 0.26))
        return ($pixel.R + $pixel.G + $pixel.B) / 3.0
    } finally { $bitmap.Dispose() }
}

function Test-Statistics($root, [string] $expectedDuration, [int] $expectedPomodoros, [int] $expectedTasks, [string] $captureName) {
    $navigationItems = Read-NavigationItems $root
    $navigationItems[2].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 220
    Scroll-Content $root 0
    if ((Find-ById $root 'StatisticsTodayDuration').Current.Name -ne $expectedDuration) { throw 'Statistics daily total differs from the shared Home ledger.' }
    if ((Find-ById $root 'StatisticsTodayPomodoros').Current.Name -ne $expectedPomodoros.ToString()) { throw 'Statistics completion count is incorrect.' }
    if ((Find-ById $root 'StatisticsTodayTasks').Current.Name -ne $expectedTasks.ToString()) { throw 'Statistics task completion count is incorrect.' }
    $type = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)
    foreach ($count in @(30, 7)) {
        $periodId = if ($count -eq 30) { 'StatisticsMonth' } else { 'StatisticsWeek' }
        Select-ById $root $periodId
        $chart = Find-ById $root 'StatisticsChart'
        $bars = $chart.FindAll([System.Windows.Automation.TreeScope]::Children, $type)
        if ($bars.Count -ne $count) { throw "Expected $count calendar bars, got $($bars.Count)." }
        if ((Find-ById $root 'StatisticsPeriodTotal').Current.Name -ne $expectedDuration) { throw 'Period total lost or duplicated today focus.' }
        $average = [TimeSpan]::Parse((Find-ById $root 'StatisticsAverage').Current.Name)
        $actual = [TimeSpan]::Parse($expectedDuration).TotalSeconds / $count
        if ($average.TotalSeconds -gt $actual + 1 -or $actual - $average.TotalSeconds -gt 1) { throw 'Daily average denominator excludes empty days.' }
        $bars[0].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
        Start-Sleep -Milliseconds 100
        if ((Find-ById $root 'StatisticsSelectedDay').Current.Name -ne $bars[0].Current.Name) { throw 'Selecting a bar did not show its daily breakdown.' }
        $bars[$count - 1].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
        Start-Sleep -Milliseconds 100
        Save-WindowCapture $root ($captureName + '-' + $count + '.png')
        if ($count -eq 7) {
            $nativeHandle = [IntPtr]$root.Current.NativeWindowHandle
            [void][FloeWindowCapture]::SetForegroundWindow($nativeHandle)
            $bars[$count - 1].SetFocus()
            if ([FloeWindowCapture]::GetForegroundWindow() -eq $nativeHandle) {
                [System.Windows.Forms.SendKeys]::SendWait('{LEFT}')
                Start-Sleep -Milliseconds 100
                if ((Find-ById $root 'StatisticsSelectedDay').Current.Name -ne $bars[$count - 2].Current.Name) { throw 'Chart arrow-key selection failed.' }
                Write-Output 'PASS statistics keyboard daily selection'
            }
        }
    }
    $navigationItems[0].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 220
    Write-Output 'PASS statistics daily totals, completion counts, 7/30 calendar days, averages and daily breakdown'
}

function Open-TrayMenu($process) {
    $handle = [FloeWindowCapture]::FindTrayWindow(('Floe.TrayHost.{0}' -f $process.Id))
    if ($handle -eq [IntPtr]::Zero -or -not [FloeWindowCapture]::HasTrayIcon($handle)) { throw ("Native Shell tray icon was not registered. Handle={0}; PID={1}; Windows={2}" -f $handle, $process.Id, [FloeWindowCapture]::OwnWindows($process.Id)) }
    [void][FloeWindowCapture]::PostMessage($handle, 0x8001, [IntPtr]1, [IntPtr]0x205)
    $condition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $process.Id)
    $deadline = [DateTime]::UtcNow.AddSeconds(3)
    do {
        $roots = [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $condition)
        foreach ($root in $roots) {
            $item = Find-ById $root 'TrayOpenMain'
            if ($null -ne $item) {
                $parent = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($item)
                while ($null -ne $parent) {
                    if ($parent.Current.ControlType -eq [System.Windows.Automation.ControlType]::Menu) { return $parent }
                    $parent = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($parent)
                }
            }
        }
        Start-Sleep -Milliseconds 50
    } while ([DateTime]::UtcNow -lt $deadline)
    throw ('Tray context menu was not accessible. ' + [FloeWindowCapture]::OwnWindows($process.Id) + '; Exited=' + $process.HasExited)
}

function Invoke-TrayItem($process, [string] $id) {
    $menu = Open-TrayMenu $process
    Invoke-ById $menu $id
}

function Test-SystemIntegration($process, $window) {
    $startupKey = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\Microsoft\Windows\CurrentVersion\Run')
    $startupBefore = if ($null -eq $startupKey) { $null } else { $startupKey.GetValue('Floe') }
    if ($null -ne $startupKey) { $startupKey.Dispose() }
    $items = Read-NavigationItems $window
    $items[3].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 220
    foreach ($id in @('NotificationsEnabled', 'CloseToTray')) {
        if ((Find-ById $window $id).GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Current.ToggleState -ne [System.Windows.Automation.ToggleState]::On) { throw "Default preference is incorrect: $id" }
    }
    foreach ($id in @('SoundEnabled', 'MinimizeToTray')) {
        if ((Find-ById $window $id).GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Current.ToggleState -ne [System.Windows.Automation.ToggleState]::Off) { throw "Default preference is incorrect: $id" }
    }
    Toggle-ById $window 'NotificationsEnabled'
    Invoke-ById $window 'TestReminder'
    if ((Find-ById $window 'SystemSettingsStatus').Current.Name -ne '提醒和声音均已关闭。') { throw 'Disabled reminders did not respect preferences.' }
    Toggle-ById $window 'SoundEnabled'
    Toggle-ById $window 'SoundEnabled'
    Toggle-ById $window 'MinimizeToTray'
    Toggle-ById $window 'WidgetVisible'
    Select-ById $window 'ThemeDark'
    Set-Input $window 'FocusMinutes' '1'
    Set-Input $window 'ShortBreakMinutes' '1'
    Set-Input $window 'LongBreakMinutes' '1'
    Set-Input $window 'FocusesPerCycle' '1'
    if ((Find-ById $window 'AutoStartBreak').GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Current.ToggleState -eq [System.Windows.Automation.ToggleState]::Off) { Toggle-ById $window 'AutoStartBreak' }
    Invoke-ById $window 'ApplyPomodoroSettings'
    $widget = Wait-WidgetWindow $process
    $items[0].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 220
    Invoke-TrayItem $process 'TrayPomodoro'
    Write-Output 'PASS tray starts shared pomodoro'
    if ((Find-ById $widget 'WidgetTimer').Current.ItemStatus -ne '进行中') { throw 'Tray did not start shared pomodoro.' }
    $before = Parse-PomodoroTime (Read-WidgetTimer $widget)
    $mainHandle = [IntPtr]$window.Current.NativeWindowHandle
    $dragBounds = $window.Current.BoundingRectangle
    $dragPoint = [IntPtr](([int]($dragBounds.Y + 20) -shl 16) -bor ([int]($dragBounds.X + 100) -band 0xffff))
    if ([FloeWindowCapture]::SendMessage($mainHandle, 0x84, [IntPtr]::Zero, $dragPoint).ToInt32() -ne 2) { throw 'Integrated top area is not a native draggable caption.' }
    $window.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
    Start-Sleep -Milliseconds 2200
    if ($process.HasExited -or [FloeWindowCapture]::IsWindowVisible($mainHandle)) { throw 'Close-to-tray did not hide the main window.' }
    if (-not (Is-WidgetVisible $process)) { throw 'Closing main window hid the widget.' }
    if ((Parse-PomodoroTime (Read-WidgetTimer $widget)) -ge $before) { throw 'Pomodoro stopped while in the tray.' }
    Write-Output 'PASS close-to-tray keeps widget and focus running'
    if ($IncludeCompletion) {
        foreach ($expectedPhase in @('长休息', '专注')) {
            $deadline = [DateTime]::UtcNow.AddSeconds(70)
            do { Start-Sleep -Milliseconds 250 } while ((Find-ById $widget 'WidgetTimer').Current.Name -ne $expectedPhase -and [DateTime]::UtcNow -lt $deadline)
            if ((Find-ById $widget 'WidgetTimer').Current.Name -ne $expectedPhase) { throw "Hidden-window natural transition did not occur: $expectedPhase" }
            Write-Output ("PASS hidden-window natural transition: {0}" -f $expectedPhase)
        }
    } else { Invoke-TrayItem $process 'TrayPomodoro' }
    $menu = Open-TrayMenu $process
    if ((Find-ById $menu 'TrayFreeFocus').Current.IsEnabled -ne $true) { throw 'Free focus stayed disabled after pause.' }
    Invoke-ById $menu 'TrayFreeFocus'
    Start-Sleep -Milliseconds 1300
    $menu = Open-TrayMenu $process
    if ((Find-ById $menu 'TrayPomodoro').Current.IsEnabled) { throw 'Tray did not enforce mutual exclusion.' }
    Start-Sleep -Milliseconds 300 # Let the standard Windows menu fade settle before visual verification.
    $popupBounds = $menu.Current.BoundingRectangle
    if ($popupBounds.Width -lt 100 -or $popupBounds.Height -lt 100) { throw 'Tray menu did not occupy a visible desktop rectangle.' }
    $popupBitmap = New-Object System.Drawing.Bitmap([int]$popupBounds.Width, [int]$popupBounds.Height)
    $popupGraphics = [System.Drawing.Graphics]::FromImage($popupBitmap)
    try {
        # Layered popup windows are not faithfully painted by PrintWindow. Capture only this test menu.
        $popupGraphics.CopyFromScreen([int]$popupBounds.X, [int]$popupBounds.Y, 0, 0, $popupBitmap.Size)
        $popupBitmap.Save((Join-Path $captureDirectory 'TrayMenu-dark.png'), [System.Drawing.Imaging.ImageFormat]::Png)
    } finally { $popupGraphics.Dispose(); $popupBitmap.Dispose() }
    Invoke-ById $menu 'TrayFreeFocus'
    $paused = Read-WidgetTimer $widget
    Start-Sleep -Milliseconds 1100
    if ((Read-WidgetTimer $widget) -ne $paused) { throw 'Tray did not pause free focus.' }
    Invoke-TrayItem $process 'TrayWidget'
    if (Is-WidgetVisible $process) { throw 'Tray did not hide widget.' }
    Invoke-TrayItem $process 'TrayWidget'
    $widget = Wait-WidgetWindow $process
    Invoke-TrayItem $process 'TrayOpenMain'
    if (-not [FloeWindowCapture]::IsWindowVisible($mainHandle)) { throw 'Tray did not restore main window.' }
    $pattern = $window.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern)
    $pattern.SetWindowVisualState([System.Windows.Automation.WindowVisualState]::Maximized)
    $pattern.SetWindowVisualState([System.Windows.Automation.WindowVisualState]::Minimized)
    Start-Sleep -Milliseconds 200
    if ([FloeWindowCapture]::IsWindowVisible($mainHandle)) { throw 'Minimize-to-tray did not hide window.' }
    Invoke-TrayItem $process 'TrayOpenMain'
    if ($pattern.Current.WindowVisualState -ne [System.Windows.Automation.WindowVisualState]::Maximized) { throw 'Tray restore lost maximized state.' }
    $pattern.SetWindowVisualState([System.Windows.Automation.WindowVisualState]::Normal)
    $items[3].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 220
    Toggle-ById $window 'NotificationsEnabled'
    Invoke-ById $window 'TestReminder'
    if ((Find-ById $window 'SystemSettingsStatus').Current.Name -notmatch '已请求提醒') { throw 'Native reminder request failed.' }
    Scroll-Content $window 100
    Save-WindowCapture $window 'SettingsPage-system-dark.png'
    Invoke-TrayItem $process 'TrayExit'
    if (-not $process.WaitForExit(5000) -or $process.ExitCode -ne 0) { throw 'Tray exit did not cleanly close all windows.' }
    $process.Dispose()
    $script:appProcess = Start-TestApplication -Background
    $process = $script:appProcess
    $widget = Wait-WidgetWindow $process
    Select-ById $widget 'WidgetModeFree'
    if ((Read-WidgetTimer $widget) -ne $paused -or (Find-ById $widget 'WidgetTimer').Current.ItemStatus -ne '已暂停') { throw 'Background startup did not recover paused free focus.' }
    $visibleMain = [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children,
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $process.Id)))
    foreach ($root in $visibleMain) { if ($root.Current.AutomationId -eq 'FloeMainWindow' -and [FloeWindowCapture]::IsWindowVisible([IntPtr]$root.Current.NativeWindowHandle)) { throw 'Background startup showed main window.' } }
    Invoke-TrayItem $process 'TrayOpenMain'
    $window = Wait-ApplicationWindow $process
    $items = Read-NavigationItems $window
    $items[3].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 220
    foreach ($id in @('CloseToTray', 'MinimizeToTray', 'NotificationsEnabled', 'WidgetVisible')) {
        if ((Find-ById $window $id).GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Current.ToggleState -ne [System.Windows.Automation.ToggleState]::On) { throw "Preference was overwritten: $id" }
    }
    if ($window.Current.HelpText -notmatch 'Theme=Dark') { throw 'System preferences overwrote appearance.' }
    $startupKey = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\Microsoft\Windows\CurrentVersion\Run')
    $startupAfter = if ($null -eq $startupKey) { $null } else { $startupKey.GetValue('Floe') }
    if ($null -ne $startupKey) { $startupKey.Dispose() }
    if ($startupBefore -ne $startupAfter) { throw 'Smoke test modified real startup registration.' }
    # A real SQLite write lock makes final saving fail. Choosing No must keep the app and widget alive.
    $architecture = if ([IntPtr]::Size -eq 8) { 'win-x64' } else { 'win-x86' }
    $libraryPath = Join-Path (Split-Path -Parent $appPath) ('runtimes/{0}/native/e_sqlite3.dll' -f $architecture)
    if (-not (Test-Path -LiteralPath $libraryPath)) { $libraryPath = Join-Path (Split-Path -Parent $appPath) 'e_sqlite3.dll' }
    $databaseLock = New-Object FloeDatabaseWriteLock($libraryPath, (Join-Path $testDataDirectory 'floe.db'))
    try {
        Invoke-TrayItem $process 'TrayExit'
        $condition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $process.Id)
        $deadline = [DateTime]::UtcNow.AddSeconds(8)
        $dialog = $null
        do {
            $dialogHandle = [FloeWindowCapture]::OwnWindow($process.Id, 'Floe · 保存未完成')
            if ($dialogHandle -ne [IntPtr]::Zero) { $dialog = [System.Windows.Automation.AutomationElement]::FromHandle($dialogHandle) }
            if ($null -eq $dialog) { Start-Sleep -Milliseconds 100 }
        } while ($null -eq $dialog -and [DateTime]::UtcNow -lt $deadline)
        if ($null -eq $dialog) { throw ('Failed final save did not ask before discarding unsaved data. ' + [FloeWindowCapture]::OwnWindows($process.Id) + '; Exited=' + $process.HasExited) }
        # Standard Win32 MessageBox IDNO is 7; UIAutomation exposes that native control ID.
        Invoke-ById $dialog '7'
        if ($process.HasExited -or -not (Is-WidgetVisible $process)) { throw 'Declining unsaved-data exit did not keep the app and widget alive.' }
    } finally { $databaseLock.Dispose() }
    $items[0].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 220
    Invoke-ById $window 'RetryStorage'
    $items[3].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 220
    Write-Output 'PASS failed final save asks before exit; cancelling preserves all windows and retry succeeds'
    # Explicit exit from the settings page must bypass the close-to-tray preference too.
    Invoke-ById $window 'ExitApp'
    if (-not $process.WaitForExit(5000) -or $process.ExitCode -ne 0) { throw 'Settings explicit exit did not terminate.' }
    Write-Output 'PASS native tray registration, menu commands, hidden timing, mutual exclusion, widget, close/minimize/restore, explicit exits, background startup and preference recovery'
    Write-Output 'PASS reminder preferences and native notification request; Windows controls visible delivery'
    Write-Output ('Test database: {0}' -f $testDataDirectory)
}

function Invoke-Now($root, [string] $id) {
    $element = Find-ById $root $id
    if ($null -eq $element) { throw "Control not found: $id" }
    $element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}

function Wait-WidgetSettled($process) {
    $deadline = [DateTime]::UtcNow.AddSeconds(3)
    do {
        $widget = Find-WidgetWindow $process
        if ($null -ne $widget -and $widget.Current.HelpText -match 'Motion=Idle') { return $widget }
        Start-Sleep -Milliseconds 20
    } while ([DateTime]::UtcNow -lt $deadline)
    throw 'Widget did not settle after its presentation transition.'
}

function Test-Motion($process, $window) {
    $items = Read-NavigationItems $window
    $items[3].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 250
    Toggle-ById $window 'NotificationsEnabled'
    if ($window.Current.HelpText -notmatch 'Motion=True') { throw 'This desktop environment does not enable app motion; run in a local accelerated desktop with Windows animations enabled.' }
    Scroll-Content $window 100
    for ($index = 0; $index -lt 16; $index++) { $items[$index % 4].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select() }
    $items[0].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 300
    if ($null -eq (Find-ById $window 'HomePage') -or (Find-ById $window 'PageTitle').Current.Name -ne '首页') { throw 'Rapid navigation showed a stale page.' }
    $scroll = (Find-ById $window 'MainContentScroll').GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern)
    if ($scroll.Current.VerticallyScrollable -and $scroll.Current.VerticalScrollPercent -gt 1) { throw 'Page navigation inherited the previous page scroll position.' }
    $items[1].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 250
    Set-Input $window 'TaskTitleInput' '动画验收任务'
    Set-Input $window 'TaskEstimateInput' '2'
    Invoke-ById $window 'TaskSave'
    Set-Input $window 'TaskTitleInput' '切页保留的草稿'
    $items[3].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 250
    Toggle-ById $window 'WidgetVisible'
    $widget = Wait-WidgetSettled $process
    $fullBounds = $widget.Current.BoundingRectangle
    $scale = $fullBounds.Height / 156.0
    $items[0].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 250
    Select-FocusTask $window '动画验收任务'
    Invoke-ById $window 'TimerPrimaryAction'
    $startTime = Parse-PomodoroTime (Read-WidgetTimer $widget)
    $items[3].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 250
    (Find-ById $window 'WidgetCompact').GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
    $heights = New-Object 'System.Collections.Generic.List[double]'
    $deadline = [DateTime]::UtcNow.AddSeconds(2)
    do {
        $heights.Add($widget.Current.BoundingRectangle.Height)
        Start-Sleep -Milliseconds 15
    } while ($widget.Current.HelpText -notmatch 'Motion=Idle' -and [DateTime]::UtcNow -lt $deadline)
    $widget = Wait-WidgetSettled $process
    if ($widget.Current.HelpText -notmatch 'Compact=True' -or [Math]::Abs($widget.Current.BoundingRectangle.Height - 140 * $scale) -gt 3) { throw 'Compact widget did not reach its layout height.' }
    $intermediate = @($heights | Where-Object { $_ -gt 140 * $scale + 3 -and $_ -lt $fullBounds.Height - 3 })
    if ($intermediate.Count -eq 0) { throw 'Enabled widget resizing did not produce intermediate geometry.' }
    $taskBounds = (Find-ById $widget 'WidgetTask').Current.BoundingRectangle
    if ($taskBounds.Height -le 0 -or -not $widget.Current.BoundingRectangle.Contains($taskBounds)) { throw 'Compact widget clipped its task.' }
    Save-WindowCapture $widget 'Widget-compact-light.png'
    Start-Sleep -Milliseconds 1100
    if ((Parse-PomodoroTime (Read-WidgetTimer $widget)) -ge $startTime -or (Find-ById $widget 'WidgetTask').Current.Name -ne '动画验收任务') { throw 'Presentation changes interrupted focus or task binding.' }
    for ($index = 0; $index -lt 10; $index++) { (Find-ById $window 'WidgetCompact').GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle() }
    $widget = Wait-WidgetSettled $process
    if ($widget.Current.HelpText -notmatch 'Compact=True') { throw 'Rapid compact reversals ended in a stale state.' }
    Toggle-ById $window 'WidgetCompact'
    $widget = Wait-WidgetSettled $process
    $items[3].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 250
    (Find-ById $window 'WidgetCompact').GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
    (Find-ById $window 'ReduceMotion').GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
    Start-Sleep -Milliseconds 50
    if ($window.Current.HelpText -notmatch 'Motion=False' -or $widget.Current.HelpText -notmatch 'Compact=True;Motion=Idle' -or [Math]::Abs($widget.Current.BoundingRectangle.Height - 140 * $scale) -gt 3) { throw 'Reduce motion did not finish the active widget transition immediately.' }
    Toggle-ById $window 'WidgetLocked'
    $position = $widget.Current.BoundingRectangle
    (Find-ById $window 'WidgetCompact').GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
    Start-Sleep -Milliseconds 50
    if ($widget.Current.HelpText -notmatch 'Compact=False;Motion=Idle' -or [Math]::Abs($widget.Current.BoundingRectangle.Height - $fullBounds.Height) -gt 3) { throw 'Reduced-motion expansion did not finish immediately.' }
    if ([Math]::Abs($widget.Current.BoundingRectangle.X - $position.X) -gt 2 -or [Math]::Abs($widget.Current.BoundingRectangle.Y - $position.Y) -gt 2) { throw 'Compact presentation moved a locked widget.' }
    Select-ById $window 'ThemeDark'
    Toggle-ById $window 'WidgetCompact'
    Save-WindowCapture $widget 'Widget-compact-dark.png'
    for ($index = 0; $index -lt 6; $index++) { (Find-ById $window 'WidgetVisible').GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle() }
    $widget = Wait-WidgetSettled $process
    if ($widget.Current.HelpText -notmatch 'Compact=True') { throw 'Rapid widget visibility changes lost layout state.' }
    Toggle-ById $window 'ReduceMotion'
    for ($index = 0; $index -lt 6; $index++) { (Find-ById $window 'WidgetVisible').GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle() }
    $widget = Wait-WidgetSettled $process
    if ($widget.Current.HelpText -notmatch 'Motion=Idle') { throw 'An old hide completion hid a newly shown widget.' }
    $items[1].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 250
    if ((Find-ById $window 'TaskTitleInput').GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value -ne '切页保留的草稿') { throw 'Page transition lost the task draft.' }
    $mainHandle = [IntPtr]$window.Current.NativeWindowHandle
    [void][FloeWindowCapture]::SetForegroundWindow($mainHandle)
    if ([FloeWindowCapture]::GetForegroundWindow() -eq $mainHandle) {
        [System.Windows.Forms.SendKeys]::SendWait('^3')
        Start-Sleep -Milliseconds 300
        if ($null -eq (Find-ById $window 'StatisticsPage')) { throw 'Ctrl+3 navigation did not work.' }
        Write-Output 'PASS Ctrl+1-4 page navigation'
    } else { Write-Output 'SKIP Ctrl+1-4 input: desktop focus could not be acquired safely' }
    Invoke-TrayItem $process 'TrayExit'
    if (-not $process.WaitForExit(5000) -or $process.ExitCode -ne 0) { throw 'Exit during animated UI did not terminate.' }
    $process.Dispose()
    $script:appProcess = Start-TestApplication
    $process = $script:appProcess
    $window = Wait-ApplicationWindow $process
    $widget = Wait-WidgetSettled $process
    if ($widget.Current.HelpText -notmatch 'Locked=True;Opacity=1.00.*Compact=True' -or (Find-ById $widget 'WidgetTimer').Current.ItemStatus -ne '已暂停') { throw 'Compact restart lost preference, lock or paused focus.' }
    $items = Read-NavigationItems $window
    $items[3].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 250
    Invoke-ById $window 'ExitApp'
    if (-not $process.WaitForExit(5000) -or $process.ExitCode -ne 0) { throw 'Final motion test exit failed.' }
    Write-Output 'PASS rapid page/widget reversals, intermediate resize frames, reduced-motion cancellation, compact clipping, binding, draft retention and compact restart'
    Write-Output ('Test database: {0}' -f $testDataDirectory)
}

function Save-GlassCapture($root, [string] $name) {
    # DWM backdrops are composited outside WPF's PrintWindow output.
    $bounds = $root.Current.BoundingRectangle
    $bitmap = [Drawing.Bitmap]::new([int]$bounds.Width, [int]$bounds.Height)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.CopyFromScreen([int]$bounds.X, [int]$bounds.Y, 0, 0, $bitmap.Size)
        $bitmap.Save((Join-Path $captureDirectory $name), [Drawing.Imaging.ImageFormat]::Png)
    } finally { $graphics.Dispose(); $bitmap.Dispose() }
}

function Test-UiPolish($process, $window) {
    $area = [Windows.Forms.Screen]::PrimaryScreen.WorkingArea
    $window.GetCurrentPattern([System.Windows.Automation.TransformPattern]::Pattern).Move($area.Left + 20, $area.Top + 20)
    $items = Read-NavigationItems $window
    if ($null -ne (Find-ById $window 'OpenAppearance') -or $null -eq (Find-ById $window 'BrandLogo')) { throw 'Old appearance entry or missing user logo.' }
    $scrollHost = Find-ById $window 'MainContentScroll'
    $scroll = $scrollHost.GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern)
    foreach ($mode in @('ModePomodoro','ModeFree')) {
        Select-ById $window $mode
        if ($scroll.Current.VerticallyScrollable) { throw ('Default home still scrolls: ' + $mode) }
        foreach ($id in @('TodayFocus','CompletedPomodoros','CompletedTasksToday')) {
            $bounds = (Find-ById $window $id).Current.BoundingRectangle
            if ($bounds.Height -le 0 -or -not $scrollHost.Current.BoundingRectangle.Contains($bounds)) { throw ('Home statistic is clipped: ' + $id) }
        }
        $card = Find-ById $window 'FocusCard'
        if ($null -eq (Find-ById $card $mode) -or $null -eq (Find-ById $card 'FocusTaskSelector')) { throw 'Home controls are outside the focus card.' }
    }
    Select-ById $window 'ModePomodoro'
    $mainHandle = [IntPtr]$window.Current.NativeWindowHandle
    [void][FloeWindowCapture]::SetForegroundWindow($mainHandle)
    Start-Sleep -Milliseconds 300
    Save-GlassCapture $window 'UI-Home-light.png'
    $maxBounds = (Find-ById $window 'WindowMaximize').Current.BoundingRectangle
    $x = [int]($maxBounds.X + $maxBounds.Width/2); $y = [int]($maxBounds.Y + $maxBounds.Height/2)
    $point = [IntPtr](($y -shl 16) -bor ($x -band 0xffff))
    if ([FloeWindowCapture]::SendMessage($mainHandle, 0x84, [IntPtr]::Zero, $point).ToInt32() -ne 9) { throw 'Custom maximize button does not return native HTMAXBUTTON for Snap.' }
    Invoke-ById $window 'WindowMaximize'
    $pattern = $window.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern)
    if ($pattern.Current.WindowVisualState -ne [System.Windows.Automation.WindowVisualState]::Maximized) { throw 'Custom maximize failed.' }
    Invoke-ById $window 'WindowMaximize'
    if ($pattern.Current.WindowVisualState -ne [System.Windows.Automation.WindowVisualState]::Normal) { throw 'Custom restore failed.' }
    Invoke-ById $window 'WindowMinimize'
    if ($pattern.Current.WindowVisualState -ne [System.Windows.Automation.WindowVisualState]::Minimized) { throw 'Custom minimize failed.' }
    $pattern.SetWindowVisualState([System.Windows.Automation.WindowVisualState]::Normal)
    Write-Output 'PASS default home fits both modes, controls integrated, logo, custom minimize/maximize/restore and native Snap hit test'
    $items[1].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 250
    Set-Input $window 'TaskTitleInput' '阅读与思考'
    Set-Input $window 'TaskEstimateInput' '2'
    Invoke-ById $window 'TaskSave'
    $items[0].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 250
    Select-FocusTask $window '阅读与思考'
    $combo = Find-ById $window 'FocusTaskSelector'
    $combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
    Start-Sleep -Milliseconds 250
    Save-GlassCapture $window 'UI-Task-dropdown.png'
    $combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Collapse()
    $items[3].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 250
    Toggle-ById $window 'NotificationsEnabled'
    if ($null -ne (Find-ById $window 'WidgetOpacityPercent')) { throw 'Opacity parameter is still exposed.' }
    Toggle-ById $window 'WidgetVisible'
    $widget = Wait-WidgetSettled $process
    $widget.GetCurrentPattern([System.Windows.Automation.TransformPattern]::Pattern).Move($area.Left + 70, $area.Top + 190)
    if ($widget.Current.HelpText -notmatch 'Opacity=1.00.*Backdrop=Acrylic' -and $window.Current.HelpText -match 'Backdrop=Acrylic') { throw 'Widget did not receive the native backdrop.' }
    if ((Find-ById $widget 'WidgetTask').Current.Name -ne '阅读与思考') { throw 'Minimal widget lost its current task.' }
    Save-GlassCapture $widget 'UI-Widget-light.png'
    Select-ById $window 'ThemeDark'
    Save-GlassCapture $widget 'UI-Widget-dark.png'
    Toggle-ById $window 'WidgetVisible'
    $items[0].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 250
    [void][FloeWindowCapture]::SetForegroundWindow($mainHandle)
    Save-GlassCapture $window 'UI-Home-dark.png'
    $items[3].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 250
    Toggle-ById $window 'WidgetVisible'
    $widget = Wait-WidgetSettled $process
    # A owned test backdrop provides high-frequency content behind the topmost widget.
    $background = [Windows.Forms.Form]::new()
    $patternImage = [Drawing.Bitmap]::new(480,480)
    $brushA = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(250,230,210))
    $brushB = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(50,110,190))
    $drawing = [Drawing.Graphics]::FromImage($patternImage)
    try {
        for ($row=0; $row -lt 480; $row+=8) { $drawing.FillRectangle($(if (($row/8)%2 -eq 0) {$brushA} else {$brushB}),0,$row,480,8) }
        $bounds = $widget.Current.BoundingRectangle
        $background.FormBorderStyle = [Windows.Forms.FormBorderStyle]::None
        $background.StartPosition = [Windows.Forms.FormStartPosition]::Manual
        $background.Location = [Drawing.Point]::new([int]$bounds.X,[int]$bounds.Y)
        $background.Size = [Drawing.Size]::new([int]$bounds.Width,[int]$bounds.Height)
        $background.BackgroundImage = $patternImage
        $background.TopMost = $true
        $background.Show(); [Windows.Forms.Application]::DoEvents()
        [void][FloeWindowCapture]::SetWindowPos([IntPtr]$widget.Current.NativeWindowHandle, [IntPtr](-1), 0, 0, 0, 0, 0x13)
        [void][FloeWindowCapture]::SetForegroundWindow([IntPtr]$widget.Current.NativeWindowHandle)
        Start-Sleep -Milliseconds 350
        Save-GlassCapture $widget 'UI-Widget-blur-proof.png'
    } finally { $background.Close(); $background.Dispose(); $drawing.Dispose(); $brushA.Dispose(); $brushB.Dispose(); $patternImage.Dispose() }
    $items[3].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 250
    Toggle-ById $window 'ReduceTransparency'
    if ($window.Current.HelpText -notmatch 'Backdrop=Solid' -or $widget.Current.HelpText -notmatch 'Backdrop=Solid') { throw 'Reduce transparency did not disable native backdrops.' }
    Save-GlassCapture $widget 'UI-Widget-solid.png'
    Invoke-ById $window 'WindowClose'
    if ($process.HasExited) { throw 'Custom close bypassed close-to-tray preference.' }
    Invoke-TrayItem $process 'TrayOpenMain'
    Invoke-TrayItem $process 'TrayExit'
    if (-not $process.WaitForExit(5000) -or $process.ExitCode -ne 0) { throw 'UI acceptance exit failed.' }
    Write-Output 'PASS task dropdown, minimal widget, native Acrylic, solid fallback, design-controlled translucency, close-to-tray and clean exit'
    Write-Output ('Test database: {0}' -f $testDataDirectory)
}

function Measure-FloeResource($process, [string] $scenario, [int] $seconds = 12) {
    $process.Refresh(); $beforeCpu = $process.TotalProcessorTime.TotalSeconds
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $maximumPrivate = $process.PrivateMemorySize64
    $maximumWorking = $process.WorkingSet64
    for ($sample=0; $sample -lt $seconds; $sample++) {
        Start-Sleep -Milliseconds 1000
        $process.Refresh()
        if ($process.HasExited) { throw 'Application exited during performance sampling.' }
        $maximumPrivate = [Math]::Max($maximumPrivate,$process.PrivateMemorySize64)
        $maximumWorking = [Math]::Max($maximumWorking,$process.WorkingSet64)
    }
    $cpu = 100 * ($process.TotalProcessorTime.TotalSeconds-$beforeCpu) / $watch.Elapsed.TotalSeconds
    return [pscustomobject]@{ Scenario=$scenario; Seconds=[Math]::Round($watch.Elapsed.TotalSeconds,2); CpuOneCorePercent=[Math]::Round($cpu,3); CpuMachinePercent=[Math]::Round($cpu/[Environment]::ProcessorCount,3); PeakPrivateMiB=[Math]::Round($maximumPrivate/1MB,2); PeakWorkingMiB=[Math]::Round($maximumWorking/1MB,2); Handles=$process.HandleCount; Threads=$process.Threads.Count }
}

function Test-Performance($process, $window) {
    $starts = [Collections.Generic.List[double]]::new()
    $starts.Add([Math]::Round($startupWatch.Elapsed.TotalMilliseconds,1))
    for ($index=0; $index -lt 2; $index++) {
        Invoke-TrayItem $process 'TrayExit'
        if (-not $process.WaitForExit(5000)) { throw 'Startup sample did not exit.' }
        $process.Dispose()
        $watch = [Diagnostics.Stopwatch]::StartNew()
        $script:appProcess = Start-TestApplication
        $process = $script:appProcess
        $window = Wait-ApplicationWindow $process
        $starts.Add([Math]::Round($watch.Elapsed.TotalMilliseconds,1))
    }
    $items = Read-NavigationItems $window
    $items[3].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 250
    Toggle-ById $window 'NotificationsEnabled'
    $items[0].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 1500
    $samples = [Collections.Generic.List[object]]::new()
    $samples.Add((Measure-FloeResource $process 'Home idle'))
    Invoke-ById $window 'WindowClose'
    $samples.Add((Measure-FloeResource $process 'Tray idle'))
    Invoke-TrayItem $process 'TrayPomodoro'
    $samples.Add((Measure-FloeResource $process 'Tray focus running'))
    Invoke-TrayItem $process 'TrayWidget'
    $widget = Wait-WidgetSettled $process
    $samples.Add((Measure-FloeResource $process 'Widget focus running'))
    Invoke-TrayItem $process 'TrayOpenMain'
    Invoke-ById $window 'TimerPrimaryAction'
    # Warm all views before comparing allocations; no forced GC or process-wide tuning.
    for ($index=0; $index -lt 8; $index++) { $items[$index%4].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select(); Start-Sleep -Milliseconds 250 }
    Start-Sleep -Milliseconds 1500
    $process.Refresh(); $privateBefore=$process.PrivateMemorySize64; $handlesBefore=$process.HandleCount
    for ($index=0; $index -lt 80; $index++) { $items[$index%4].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select(); Start-Sleep -Milliseconds 50 }
    Start-Sleep -Milliseconds 2500
    $process.Refresh()
    $report = [pscustomobject]@{ Date=[DateTime]::UtcNow.ToString('o'); Architecture=[Environment]::Is64BitProcess; ProcessorCount=[Environment]::ProcessorCount;
        App=$appPath; UseAppHost=[bool]$UseAppHost; StartupToAutomationReadyMs=@($starts); Samples=@($samples.ToArray());
        Navigation80PrivateDeltaMiB=[Math]::Round(($process.PrivateMemorySize64-$privateBefore)/1MB,2); Navigation80HandleDelta=$process.HandleCount-$handlesBefore;
        Database=$testDataDirectory; Note='Startup includes UIAutomation discovery and warm OS caches; CPU is process CPU, not system compositor GPU or a long-term leak guarantee.' }
    $report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $captureDirectory 'performance.json') -Encoding UTF8
    $samples | Format-Table -AutoSize | Out-String | Write-Output
    Write-Output ('Startup ms: ' + ($starts -join ', '))
    Write-Output ('Navigation private delta MiB: ' + $report.Navigation80PrivateDeltaMiB + '; handles: ' + $report.Navigation80HandleDelta)
    Invoke-TrayItem $process 'TrayExit'
    if (-not $process.WaitForExit(5000) -or $process.ExitCode -ne 0) { throw 'Performance run exit failed.' }
    Write-Output ('Performance report: ' + (Join-Path $captureDirectory 'performance.json'))
}

function Use-WidgetPointer($widget, [string] $target = 'WidgetTimer', [int] $holdMs = 100, [int] $dx = 0, [int] $dy = 0, [switch] $ExpectImmediateMove) {
    $handle = [IntPtr]$widget.Current.NativeWindowHandle
    [void][FloeWindowCapture]::SetForegroundWindow($handle)
    $bounds = if ($target -eq 'blank') { $widget.Current.BoundingRectangle } else { (Find-ById $widget $target).Current.BoundingRectangle }
    $x = [int]($bounds.X + $bounds.Width/2)
    $y = [int]($bounds.Y + $bounds.Height/2)
    if ($target -eq 'blank') { $x = [int]($bounds.X+12); $y = [int]($bounds.Y+12) }
    if (-not [FloeWindowCapture]::PointBelongsToWindow($handle,$x,$y)) { throw 'Another window covers the test widget click target.' }
    $cursorBefore = [Windows.Forms.Cursor]::Position
    $beforeGesture = $widget.Current.BoundingRectangle
    try {
        [void][FloeWindowCapture]::SetCursorPos($x,$y)
        [FloeWindowCapture]::mouse_event(2,0,0,0,[UIntPtr]::Zero)
        Start-Sleep -Milliseconds $holdMs
        if ($dx -ne 0 -or $dy -ne 0) {
            [void][FloeWindowCapture]::SetCursorPos(($x+$dx),($y+$dy))
            Start-Sleep -Milliseconds 80
            if ($ExpectImmediateMove -and [Math]::Abs($widget.Current.BoundingRectangle.X-$beforeGesture.X) -lt 20) { throw 'Widget still waits before moving while the mouse is held.' }
        }
    } finally {
        [FloeWindowCapture]::mouse_event(4,0,0,0,[UIntPtr]::Zero)
        [void][FloeWindowCapture]::SetCursorPos($cursorBefore.X,$cursorBefore.Y)
    }
    Start-Sleep -Milliseconds 250
}

function Test-MinimalWidget($process, $window) {
    $items = Read-NavigationItems $window
    $items[1].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 220
    Set-Input $window 'TaskTitleInput' '只做眼前这一件事'
    Set-Input $window 'TaskEstimateInput' '2'
    Invoke-ById $window 'TaskSave'
    $items[0].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 220
    Select-ById $window 'ModeFree'
    if ((Find-ById $window 'FreeFinish').Current.IsEnabled) { Invoke-ById $window 'FreeFinish' }
    Select-ById $window 'ModePomodoro'
    if ((Find-ById $window 'TimerRestart').Current.IsEnabled) { Invoke-ById $window 'TimerRestart' }
    Select-FocusTask $window '只做眼前这一件事'
    $items[3].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 220
    foreach ($id in @('CloseToTray','WidgetVisible')) {
        if ((Find-ById $window $id).GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Current.ToggleState -eq [System.Windows.Automation.ToggleState]::Off) { Toggle-ById $window $id }
    }
    Select-ById $window 'ThemeLight'
    $widget = Wait-WidgetSettled $process
    $handle = [IntPtr]$widget.Current.NativeWindowHandle
    $scale = [FloeWindowCapture]::GetDpiForWindow($handle)/96.0
    if ([Math]::Abs($widget.Current.BoundingRectangle.Width - 240*$scale) -gt 2 -or [Math]::Abs($widget.Current.BoundingRectangle.Height-156*$scale) -gt 2) { throw 'Widget dimensions are not 240x156 DIP.' }
    foreach ($id in @('WidgetTitle','WidgetToggleCompact','WidgetPrimary','WidgetSecondary','WidgetModePomodoro','WidgetModeFree','WidgetOpenMain')) {
        if ($null -ne (Find-ById $widget $id)) { throw ('Removed widget control remains: '+$id) }
    }
    $buttonCondition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty,[System.Windows.Automation.ControlType]::Button)
    if ($widget.FindAll([System.Windows.Automation.TreeScope]::Descendants,$buttonCondition).Count -ne 1) { throw 'Widget must expose only its close button.' }
    if ((Find-ById $widget 'WidgetTask').Current.Name -ne '只做眼前这一件事') { throw 'Minimal widget lost its task.' }
    $area = [Windows.Forms.Screen]::PrimaryScreen.WorkingArea
    $widget.GetCurrentPattern([System.Windows.Automation.TransformPattern]::Pattern).Move($area.Left+70,$area.Top+190)
    Save-GlassCapture $widget 'Widget-minimal-light.png'
    Invoke-TrayItem $process 'TrayPomodoro'
    $mainHandle = [IntPtr]$window.Current.NativeWindowHandle
    foreach ($target in @('WidgetTimer','WidgetTask','blank')) {
        $items[3].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
        Start-Sleep -Milliseconds 220
        $window.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
        Use-WidgetPointer $widget $target
        if (-not [FloeWindowCapture]::IsWindowVisible($mainHandle) -or $null -eq (Find-ById $window 'HomePage')) { throw ('Widget click did not open home: '+$target) }
        if ((Find-ById $widget 'WidgetTimer').Current.ItemStatus -ne '进行中') { throw 'Opening home interrupted focus.' }
    }
    Write-Output 'PASS only close button, narrower geometry, live task/timer, real clicks on timer/task/background open home'
    $window.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
    $before = $widget.Current.BoundingRectangle
    Use-WidgetPointer $widget 'WidgetTimer' 20 60 35 -ExpectImmediateMove
    $after = $widget.Current.BoundingRectangle
    if ([Math]::Abs($after.X-$before.X) -lt 20 -or [FloeWindowCapture]::IsWindowVisible($mainHandle)) { throw 'Immediate drag failed or opened the main window.' }
    $before = $after
    Use-WidgetPointer $widget 'WidgetTask' 20 40 20 -ExpectImmediateMove
    if ([FloeWindowCapture]::IsWindowVisible($mainHandle) -or [Math]::Abs($widget.Current.BoundingRectangle.X-$before.X) -lt 20) { throw 'Immediate task drag did not move or was mistaken for a click.' }
    $before = $widget.Current.BoundingRectangle
    Invoke-TrayItem $process 'TrayOpenMain'
    $items[3].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 220
    Toggle-ById $window 'WidgetLocked'
    $window.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
    Use-WidgetPointer $widget 'WidgetTimer' 20 50 30
    if ([Math]::Abs($widget.Current.BoundingRectangle.X-$before.X) -gt 2 -or [FloeWindowCapture]::IsWindowVisible($mainHandle)) { throw 'Locked hold moved the widget or opened home.' }
    Use-WidgetPointer $widget 'blank'
    if (-not [FloeWindowCapture]::IsWindowVisible($mainHandle) -or $null -eq (Find-ById $window 'HomePage')) { throw 'Lock prevented a normal click opening home.' }
    $pattern = $window.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern)
    $pattern.SetWindowVisualState([System.Windows.Automation.WindowVisualState]::Maximized)
    $pattern.SetWindowVisualState([System.Windows.Automation.WindowVisualState]::Minimized)
    Use-WidgetPointer $widget 'WidgetTask'
    if ($pattern.Current.WindowVisualState -ne [System.Windows.Automation.WindowVisualState]::Maximized -or $null -eq (Find-ById $window 'HomePage')) { throw 'Widget click did not restore a maximized main window.' }
    $pattern.SetWindowVisualState([System.Windows.Automation.WindowVisualState]::Normal)
    Write-Output 'PASS real immediate drag while held, drag release does not open home, lock and minimized/maximized home restore'
    $pattern.Close()
    Use-WidgetPointer $widget 'WidgetHide'
    if (Is-WidgetVisible $process) { throw 'Close icon did not hide the widget.' }
    if ([FloeWindowCapture]::IsWindowVisible($mainHandle) -or $process.HasExited) { throw 'Close icon opened home or exited Floe.' }
    Invoke-TrayItem $process 'TrayWidget'
    $widget = Wait-WidgetSettled $process
    Invoke-TrayItem $process 'TrayPomodoro'
    $widget.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Milliseconds 250
    if ($null -eq (Find-ById $window 'HomePage')) { throw 'Accessible widget activation did not open home.' }
    Invoke-ById $window 'TimerRestart'
    Invoke-TrayItem $process 'TrayFreeFocus'
    Start-Sleep -Milliseconds 1300
    if ([TimeSpan]::Parse((Read-WidgetTimer $widget)).TotalSeconds -lt 1 -or (Find-ById $widget 'WidgetTimer').Current.ItemStatus -ne '进行中') { throw 'Widget did not reflect shared free focus.' }
    $items[3].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 220
    Toggle-ById $window 'WidgetCompact'
    $widget = Wait-WidgetSettled $process
    if ([Math]::Abs($widget.Current.BoundingRectangle.Height - 140*$scale) -gt 2) { throw 'Compact setting did not fit the smaller layout.' }
    foreach ($id in @('WidgetTimer','WidgetTask')) { if (-not $widget.Current.BoundingRectangle.Contains((Find-ById $widget $id).Current.BoundingRectangle)) { throw ('Clipped compact content: '+$id) } }
    Select-ById $window 'ThemeDark'
    Save-GlassCapture $widget 'Widget-minimal-dark.png'
    Toggle-ById $window 'ReduceTransparency'
    if ($widget.Current.HelpText -notmatch 'Backdrop=Solid') { throw 'Solid fallback failed.' }
    Toggle-ById $window 'ReduceTransparency'
    $saved = $widget.Current.BoundingRectangle
    Invoke-TrayItem $process 'TrayExit'
    if (-not $process.WaitForExit(5000) -or $process.ExitCode -ne 0) { throw 'Minimal widget test exit failed.' }
    $process.Dispose()
    $script:appProcess = Start-TestApplication
    $process = $script:appProcess
    $window = Wait-ApplicationWindow $process
    $widget = Wait-WidgetSettled $process
    $restored = Read-WidgetTimer $widget
    Start-Sleep -Milliseconds 1200
    if ((Read-WidgetTimer $widget) -ne $restored -or (Find-ById $widget 'WidgetTimer').Current.ItemStatus -ne '已暂停' -or $widget.Current.HelpText -notmatch 'Locked=True.*Compact=True') { throw 'Restart lost paused focus or layout settings.' }
    if ([Math]::Abs($widget.Current.BoundingRectangle.X-$saved.X) -gt 2 -or [Math]::Abs($widget.Current.BoundingRectangle.Y-$saved.Y) -gt 2) { throw 'Restart lost dragged widget position.' }
    Invoke-TrayItem $process 'TrayExit'
    if (-not $process.WaitForExit(5000) -or $process.ExitCode -ne 0) { throw 'Final minimal widget exit failed.' }
    Write-Output 'PASS close hides without opening home, accessibility, free focus, compact content, glass fallback and position/state restart'
    Write-Output ('Test database: '+$testDataDirectory)
}

function Dismiss-StartupFailure($process) {
    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    do {
        $handle = [FloeWindowCapture]::OwnWindow($process.Id, 'Floe · 无法读取本地数据')
        if ($handle -eq [IntPtr]::Zero) { Start-Sleep -Milliseconds 100 }
    } while ($handle -eq [IntPtr]::Zero -and -not $process.HasExited -and [DateTime]::UtcNow -lt $deadline)
    if ($handle -eq [IntPtr]::Zero) { throw 'Expected startup error dialog did not appear.' }
    $dialog = [System.Windows.Automation.AutomationElement]::FromHandle($handle)
    do {
        $button = Find-ById $dialog '1'
        if ($null -eq $button) { $button = Find-ById $dialog '2' }
        if ($null -eq $button) { Start-Sleep -Milliseconds 100 }
    } while ($null -eq $button -and [DateTime]::UtcNow -lt $deadline)
    if ($null -eq $button) {
        $dialog.FindAll([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.Condition]::TrueCondition) | ForEach-Object { Write-Output ($_.Current.AutomationId+': '+$_.Current.Name) }
        throw 'Startup dialog OK button is missing.'
    }
    # Native MessageBox buttons may expose only the legacy accessibility provider.
    if (-not [FloeWindowCapture]::PostMessage($handle, 0x111, [IntPtr]([int]$button.Current.AutomationId), [IntPtr]$button.Current.NativeWindowHandle)) { throw 'Native error dialog acknowledgement failed.' }
    if (-not $process.WaitForExit(5000) -or $process.ExitCode -ne 1) { throw 'Startup failure did not exit with code 1.' }
}

function Test-StartupFailures {
    $baseDirectory = $testDataDirectory
    $library = Join-Path (Split-Path -Parent $appPath) 'e_sqlite3.dll'
    if (-not (Test-Path -LiteralPath $library)) { $library = Join-Path (Split-Path -Parent $appPath) 'runtimes/win-x64/native/e_sqlite3.dll' }
    foreach ($scenario in @('corrupt', 'future', 'blocked-path')) {
        $script:testDataDirectory = Join-Path $baseDirectory $scenario
        if ($scenario -eq 'blocked-path') {
            [IO.File]::WriteAllText($testDataDirectory, 'This file must not become a data directory.')
            $original = (Get-FileHash -LiteralPath $testDataDirectory).Hash
        } else {
            New-Item -ItemType Directory -Path $testDataDirectory | Out-Null
            $database = Join-Path $testDataDirectory 'floe.db'
            if ($scenario -eq 'corrupt') { [IO.File]::WriteAllText($database, 'An invalid database that must never be reset.') }
            else { [FloeDatabaseWriteLock]::CreateFutureDatabase($library, $database) }
            $original = (Get-FileHash -LiteralPath $database).Hash
        }
        $process = Start-TestApplication
        try {
            Dismiss-StartupFailure $process
            $preservedPath = if ($scenario -eq 'blocked-path') { $testDataDirectory } else { $database }
            if ((Get-FileHash -LiteralPath $preservedPath).Hash -ne $original) { throw ('Startup failure overwrote original data: ' + $scenario) }
            if ($scenario -ne 'blocked-path') {
                $log = Join-Path $testDataDirectory 'logs/floe.log'
                if (-not (Test-Path -LiteralPath $log) -or (Get-Content -LiteralPath $log -Raw) -notmatch '\[Startup data\]') { throw 'Startup diagnostic log is missing.' }
            }
            Write-Output ('PASS startup ' + $scenario + ': dialog, exit code, original bytes preserved; logging does not block shutdown')
        } finally { if (-not $process.HasExited) { Stop-Process -Id $process.Id }; $process.Dispose() }
    }
    $script:testDataDirectory = Join-Path $baseDirectory 'duplicate'
    New-Item -ItemType Directory -Path $testDataDirectory | Out-Null
    $owner = Start-TestApplication
    try {
        $window = Wait-ApplicationWindow $owner
        $duplicate = Start-TestApplication
        try { Dismiss-StartupFailure $duplicate }
        finally { if (-not $duplicate.HasExited) { Stop-Process -Id $duplicate.Id }; $duplicate.Dispose() }
        if ($owner.HasExited) { throw 'Duplicate instance disturbed the original instance.' }
        Select-ById $window 'ModeFree'
        Invoke-ById $window 'FreePrimaryAction'
        Start-Sleep -Milliseconds 1200
        Invoke-ById $window 'FreePrimaryAction'
        if ((Read-FreeValue $window) -eq '00:00:00') { throw 'Original instance stopped working after duplicate rejection.' }
        Invoke-TrayItem $owner 'TrayExit'
        if (-not $owner.WaitForExit(5000) -or $owner.ExitCode -ne 0) { throw 'Original instance could not save and exit.' }
        Write-Output 'PASS duplicate instance rejected while original instance continues and saves'
    } finally { if (-not $owner.HasExited) { Stop-Process -Id $owner.Id }; $owner.Dispose() }
    $script:testDataDirectory = $baseDirectory
    Write-Output ('Failure fixtures: ' + $baseDirectory)
}

if ($FailureOnly) { Test-StartupFailures; return }
$startupWatch = [Diagnostics.Stopwatch]::StartNew()
$appProcess = Start-TestApplication
try {
    $window = Wait-ApplicationWindow $appProcess
    $startupWatch.Stop()
    if ($PerformanceOnly) { Test-Performance $appProcess $window; return }
    if ($UiOnly) { Test-UiPolish $appProcess $window; return }
    if ($MotionOnly) { Test-Motion $appProcess $window; return }

    if ($SystemOnly) { Test-SystemIntegration $appProcess $window; return }
    # Legacy tests explicitly exercise close-and-exit. Keep that preference scoped to their private database.
    $initialNavigation = Read-NavigationItems $window
    $initialNavigation[3].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 220
    Toggle-ById $window 'CloseToTray'
    Toggle-ById $window 'NotificationsEnabled'
    $initialNavigation[0].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 220

    if (-not $WidgetOnly) {
    $navigation = Find-ById $window 'MainNavigation'
    if ($null -eq $navigation) { throw 'Navigation not found.' }
    $itemCondition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::ListItem)
    $items = $navigation.FindAll([System.Windows.Automation.TreeScope]::Children, $itemCondition)
    if ($items.Count -ne 4) { throw "Expected 4 navigation items, found $($items.Count)." }

    $pageIds = @('HomePage', 'TasksPage', 'StatisticsPage', 'SettingsPage')
    for ($index = 0; $index -lt $items.Count; $index++) {
        $selection = $items[$index].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
        $selection.Select()
        $deadline = [DateTime]::UtcNow.AddSeconds(5)
        do {
            $page = Find-ById $window $pageIds[$index]
            if ($null -eq $page) { Start-Sleep -Milliseconds 50 }
        } while ($null -eq $page -and [DateTime]::UtcNow -lt $deadline)
        if ($null -eq $page) { throw "Page did not load: $($pageIds[$index])" }
        $title = Find-ById $window 'PageTitle'
        if ($title.Current.Name -ne $items[$index].Current.Name) { throw 'Page heading is out of sync with navigation.' }
        Start-Sleep -Milliseconds 220
        Save-WindowCapture $window ($pageIds[$index] + '.png')
        Write-Output "PASS navigation: $($pageIds[$index])"
    }

    # Task lifecycle is exercised through the actual bound editor and virtualized list.
    Test-Statistics $window '00:00:00' 0 0 'StatisticsPage-empty'
    $items[1].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 220
    Scroll-Content $window 0
    if ((Find-ById $window 'TaskSave').Current.IsEnabled) { throw 'Empty task title can be saved.' }
    Set-Input $window 'TaskTitleInput' '   '
    if ((Find-ById $window 'TaskSave').Current.IsEnabled) { throw 'Whitespace task title can be saved.' }
    Set-Input $window 'TaskTitleInput' '数据结构复习'
    Set-Input $window 'TaskEstimateInput' 'xx'
    if ((Find-ById $window 'TaskSave').Current.IsEnabled) { throw 'Malformed task estimate can be saved.' }
    Set-Input $window 'TaskEstimateInput' '100'
    if ((Find-ById $window 'TaskSave').Current.IsEnabled) { throw 'Out-of-range task estimate can be saved.' }
    Set-Input $window 'TaskEstimateInput' '5'
    Invoke-ById $window 'TaskSave'
    if ((Find-ById $window 'TaskSummary').Current.Name -ne '待办 1 · 已完成 0') {
        Save-WindowCapture $window 'TasksPage-diagnostic.png'
        throw ('Task was not added: summary=' + (Find-ById $window 'TaskSummary').Current.Name + '; validation=' + (Find-ById $window 'TaskValidation').Current.Name)
    }
    $taskId = (Find-TaskRow $window '数据结构复习').Current.AutomationId
    if ([string]::IsNullOrWhiteSpace($taskId)) { throw 'Task does not expose its stable ID.' }
    Invoke-ById (Find-TaskRow $window '数据结构复习') 'TaskEdit'
    Set-Input $window 'TaskTitleInput' '数据结构与算法复习'
    Set-Input $window 'TaskEstimateInput' '3'
    if (Send-EditorKey $window '{ENTER}') { Write-Output 'PASS task editor Enter saves' } else { Invoke-ById $window 'TaskSave' }
    $row = Find-TaskRow $window '数据结构与算法复习'
    if ($row.Current.AutomationId -ne $taskId) { throw 'Editing replaced task identity.' }
    if ((Find-ById $row 'TaskProgress').Current.Name -ne '0 / 3 番茄') { throw 'Task estimate did not update.' }
    Invoke-ById $row 'TaskEdit'
    Set-Input $window 'TaskTitleInput' '未保存的修改'
    if (Send-EditorKey $window '{ESC}') { Write-Output 'PASS task editor Escape cancels' } else { Invoke-ById $window 'TaskCancel' }
    if ($null -eq (Find-TaskRow $window '数据结构与算法复习')) { throw 'Cancel changed the stored title.' }
    Invoke-ById (Find-TaskRow $window '数据结构与算法复习') 'TaskEdit'
    if (Send-EditorKey $window '^n') { Write-Output 'PASS task editor Ctrl+N starts a new draft' } else { Invoke-ById $window 'TaskNew' }
    if ((Find-ById $window 'TaskEditorTitle').Current.Name -ne '添加一件事') { throw 'New task shortcut did not clear the edit state.' }
    Toggle-Task $window '数据结构与算法复习'
    if ((Find-ById $window 'TaskSummary').Current.Name -ne '待办 0 · 已完成 1') { throw 'Completing task did not update counts.' }
    $items[0].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 220
    if ((Find-ById $window 'CompletedTasksToday').Current.Name -ne '1') { throw 'Home did not count today completed task.' }
    $items[1].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 220
    Select-ById $window 'TaskFilterCompleted'
    $row = Find-TaskRow $window '数据结构与算法复习'
    if ($row.Current.AutomationId -ne $taskId) { throw 'Completed task identity changed.' }
    Toggle-Task $window '数据结构与算法复习'
    Select-ById $window 'TaskFilterActive'
    if ($null -eq (Find-TaskRow $window '数据结构与算法复习')) { throw 'Restore did not return task to active list.' }
    Invoke-ById (Find-TaskRow $window '数据结构与算法复习') 'TaskDelete'
    if ((Find-ById $window 'TaskSummary').Current.Name -ne '待办 0 · 已完成 0') { throw 'Deleted task is still counted.' }
    Invoke-ById $window 'TaskUndoDelete'
    if ((Find-TaskRow $window '数据结构与算法复习').Current.AutomationId -ne $taskId) { throw 'Undo did not restore the same task.' }
    if ((Find-ById $window 'TaskTitleInput').GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value -ne '') { throw 'Editor reset failed.' }
    Toggle-Task $window '数据结构与算法复习'

    foreach ($title in @('英语阅读', '整理今日笔记', '完成一道练习题', '阅读下一章', '整理参考资料')) {
        Set-Input $window 'TaskTitleInput' $title
        Set-Input $window 'TaskEstimateInput' '0'
        Invoke-ById $window 'TaskSave'
    }
    Select-ById $window 'TaskFilterAll'
    Scroll-Content $window 100
    $taskList = Find-ById $window 'TaskList'
    $taskScroll = $taskList.GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern)
    if (-not $taskScroll.Current.VerticallyScrollable) { throw 'Task list does not expose its bounded scrolling viewport.' }
    $taskScroll.SetScrollPercent(-1, 100)
    Start-Sleep -Milliseconds 220
    Save-WindowCapture $window 'TasksPage-completed.png'
    $taskScroll.SetScrollPercent(-1, 0)
    Start-Sleep -Milliseconds 220
    Save-WindowCapture $window 'TasksPage-list.png'
    Scroll-Content $window 0
    Write-Output 'PASS task validation, add, edit, cancel, complete, restore, delete, undo, filtering, identity and list scrolling'
    $items[3].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 220

    # Configure a real one-minute cycle; malformed input must not be applied.
    $focusInput = Find-ById $window 'FocusMinutes'
    $focusInput.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('0')
    Start-Sleep -Milliseconds 100
    if ((Find-ById $window 'ApplyPomodoroSettings').Current.IsEnabled) { throw 'Invalid duration was accepted.' }
    foreach ($id in @('FocusMinutes', 'ShortBreakMinutes', 'LongBreakMinutes', 'FocusesPerCycle')) {
        (Find-ById $window $id).GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('1')
    }
    (Find-ById $window 'AutoStartBreak').GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
    Invoke-ById $window 'ApplyPomodoroSettings'
    Scroll-Content $window 100
    Save-WindowCapture $window 'Pomodoro-settings.png'
    $items[0].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 220
    Scroll-Content $window 0
    $readyValue = Read-TimerValue $window
    if ($readyValue -ne '01:00') {
        Save-WindowCapture $window 'Pomodoro-diagnostic.png'
        throw "Ready-stage timer mismatch: element=$($null -ne $readyValue), value=$readyValue"
    }
    Select-FocusTask $window '英语阅读'
    $taskCombo = Find-ById $window 'FocusTaskSelector'
    $handle = [IntPtr]$window.Current.NativeWindowHandle
    [void][FloeWindowCapture]::SetForegroundWindow($handle)
    $taskCombo.SetFocus()
    Start-Sleep -Milliseconds 100
    if ([FloeWindowCapture]::GetForegroundWindow() -eq $handle) {
        [System.Windows.Forms.SendKeys]::SendWait('{HOME}')
        Start-Sleep -Milliseconds 220
        if ((Find-ById $window 'CurrentFocusTask').Current.Name -ne '未绑定任务') { throw 'Task combo Home did not select unbound.' }
        [System.Windows.Forms.SendKeys]::SendWait('%{DOWN}{END}{ENTER}')
        Start-Sleep -Milliseconds 220
        if ((Find-ById $window 'CurrentFocusTask').Current.Name -ne '英语阅读') { throw 'Task combo keyboard selection failed.' }
        Write-Output 'PASS task choice keyboard Home, Alt+Down, End and Enter'
    } else { Write-Output 'SKIP task choice keyboard: desktop focus could not be acquired safely' }
    if ((Find-ById $window 'CurrentFocusTask').Current.Name -ne '英语阅读') { throw 'Selected task is not shown.' }
    Save-WindowCapture $window 'HomePage-task-bound.png'
    Invoke-ById $window 'TimerPrimaryAction'
    if ((Find-ById $window 'FocusTaskSelector').Current.IsEnabled) { throw 'Running session can be rebound.' }
    Start-Sleep -Milliseconds 1200
    if ((Read-TimerValue $window) -eq '01:00') { throw 'Timer is not counting down.' }
    $items[1].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 1200
    $items[0].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 220
    Invoke-ById $window 'TimerPrimaryAction'
    $pausedText = (Read-TimerValue $window)
    if ((Find-ById $window 'FocusTaskSelector').Current.IsEnabled) { throw 'Paused session can be rebound.' }
    Start-Sleep -Milliseconds 1200
    if ((Read-TimerValue $window) -ne $pausedText) { throw 'Paused timer changed.' }
    Invoke-ById $window 'TimerPrimaryAction'
    Start-Sleep -Milliseconds 1200
    Invoke-ById $window 'TimerPrimaryAction'
    if ((Read-TimerValue $window) -eq $pausedText) { throw 'Resume did not resume elapsed time.' }
    Save-WindowCapture $window 'Pomodoro-paused.png'
    Invoke-ById $window 'TimerRestart'
    if (-not (Find-ById $window 'FocusTaskSelector').Current.IsEnabled) { throw 'Restart did not release task choice.' }
    if ((Read-TimerValue $window) -ne '01:00') { throw 'Restart did not reset the current stage.' }
    Write-Output 'PASS timer configuration, validation, start, page navigation, pause, resume and restart'

    if ($IncludeCompletion) {
        $beforeCompletion = [TimeSpan]::Parse((Find-ById $window 'TodayFocus').Current.Name)
        Invoke-ById $window 'TimerPrimaryAction'
        $completionWatch = [Diagnostics.Stopwatch]::StartNew()
        $reportedBucket = -1
        do {
            Start-Sleep -Milliseconds 1000
            $phaseText = (Find-ById $window 'TimerDisplay').Current.ItemStatus
            $bucket = [int][Math]::Floor($completionWatch.Elapsed.TotalSeconds / 20)
            if ($bucket -ne $reportedBucket) {
                Write-Output ('Waiting for real completion: ' + [int]$completionWatch.Elapsed.TotalSeconds + ' seconds')
                $reportedBucket = $bucket
            }
        } while ($phaseText -ne 'LONG BREAK' -and $completionWatch.Elapsed.TotalSeconds -lt 72)
        if ($phaseText -ne 'LONG BREAK') { throw 'Real one-minute timer did not transition to long break.' }
        if ($completionWatch.Elapsed.TotalSeconds -lt 55) { throw 'Focus completed too early.' }
        if ((Find-ById $window 'CompletedPomodoros').Current.Name -ne '1') { throw 'Natural completion was not counted once.' }
        $afterCompletion = [TimeSpan]::Parse((Find-ById $window 'TodayFocus').Current.Name)
        if (($afterCompletion - $beforeCompletion).TotalSeconds -ne 60) { throw 'Completed focus was not credited exactly once to today.' }
        Invoke-ById $window 'TimerPrimaryAction'
        Save-WindowCapture $window 'Pomodoro-completed.png'
        Write-Output 'PASS real one-minute completion, automatic long break and completed count'
    } else {
        Invoke-ById $window 'TimerSkip'
        if ((Find-ById $window 'TimerDisplay').Current.ItemStatus -ne 'LONG BREAK') { throw 'One-focus cycle did not enter long break.' }
        Invoke-ById $window 'TimerPrimaryAction'
    }
    Invoke-ById $window 'TimerSkip'
    if ((Read-TimerValue $window) -ne '01:00') { throw 'Long break did not return to a new focus cycle.' }
    Write-Output 'PASS skip and next-cycle transition'

    # Free mode shares the ledger and must never run alongside a running Pomodoro (including breaks).
    Invoke-ById $window 'TimerPrimaryAction'
    (Find-ById $window 'ModeFree').GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 220
    if ((Find-ById $window 'FreePrimaryAction').Current.IsEnabled) { throw 'Free timer can start while Pomodoro is running.' }
    (Find-ById $window 'ModePomodoro').GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Invoke-ById $window 'TimerPrimaryAction'
    (Find-ById $window 'ModeFree').GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 220
    Scroll-Content $window 0
    $beforeFree = [TimeSpan]::Parse((Find-ById $window 'TodayFocus').Current.Name)
    Invoke-ById $window 'FreePrimaryAction'
    Start-Sleep -Milliseconds 2200
    if ((Read-FreeValue $window) -eq '00:00:00') { throw 'Free focus did not accumulate.' }
    Save-WindowCapture $window 'FreeFocus-running.png'
    (Find-ById $window 'ModePomodoro').GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 220
    if ((Find-ById $window 'TimerPrimaryAction').Current.IsEnabled -or (Find-ById $window 'TimerSkip').Current.IsEnabled) { throw 'Pomodoro commands can overlap free focus.' }
    (Find-ById $window 'ModeFree').GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Invoke-ById $window 'FreePrimaryAction'
    $freePaused = Read-FreeValue $window
    $todayPaused = (Find-ById $window 'TodayFocus').Current.Name
    Start-Sleep -Milliseconds 1200
    if ((Read-FreeValue $window) -ne $freePaused -or (Find-ById $window 'TodayFocus').Current.Name -ne $todayPaused) { throw 'Pause still adds focus time.' }
    Save-WindowCapture $window 'FreeFocus-paused.png'
    $items[1].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    $items[0].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 220
    if (-not (Find-ById $window 'ModeFree').GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Current.IsSelected) { throw 'Mode selection was lost on navigation.' }
    if ((Read-FreeValue $window) -ne $freePaused) { throw 'Paused progress was lost on navigation.' }
    Invoke-ById $window 'FreePrimaryAction'
    Start-Sleep -Milliseconds 1200
    Invoke-ById $window 'FreePrimaryAction'
    $freeElapsed = [TimeSpan]::Parse((Read-FreeValue $window))
    if ($freeElapsed -le [TimeSpan]::Parse($freePaused)) { throw 'Free resume did not retain and extend the session.' }
    $todayBeforeFinish = (Find-ById $window 'TodayFocus').Current.Name
    $credited = [TimeSpan]::Parse($todayBeforeFinish) - $beforeFree
    # The display floors each total independently; subsecond Pomodoro time may carry one extra second.
    if ($credited -lt $freeElapsed -or ($credited - $freeElapsed).TotalSeconds -gt 1) { throw 'Free time was lost or counted twice.' }
    Invoke-ById $window 'FreeFinish'
    if ((Read-FreeValue $window) -ne '00:00:00') { throw 'Finish did not prepare a new session.' }
    if ((Find-ById $window 'TodayFocus').Current.Name -ne $todayBeforeFinish) { throw 'Finish discarded daily focus.' }
    if ((Find-ById $window 'FreeFinish').Current.IsEnabled) { throw 'Finished free session can be finished twice.' }
    Scroll-Content $window 100
    Save-WindowCapture $window 'FreeFocus-today.png'
    (Find-ById $window 'ModePomodoro').GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 220
    Scroll-Content $window 0
    Write-Output 'PASS free start, pause, resume, navigation, finish, shared daily total and mutual exclusion'
    $completedPomodoros = if ($IncludeCompletion) { 1 } else { 0 }
    Test-Statistics $window $todayBeforeFinish $completedPomodoros 1 'StatisticsPage-focus'

    # Every active interval so far belongs to the same task, in both modes.
    $boundTotal = (Find-ById $window 'TodayFocus').Current.Name
    $items[1].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 220
    Select-ById $window 'TaskFilterActive'
    $taskList = Find-ById $window 'TaskList'
    $taskScroll = $taskList.GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern)
    if ($taskScroll.Current.VerticallyScrollable) { $taskScroll.SetScrollPercent(-1, 100); Start-Sleep -Milliseconds 220 }
    $boundRow = Find-TaskRow $window '英语阅读'
    if ($null -eq $boundRow) { throw 'Bound task did not remain in Todo.' }
    if ((Find-ById $boundRow 'TaskFocusTime').Current.Name -ne $boundTotal) { throw 'Task and daily focus disagree.' }
    $expectedProgress = if ($IncludeCompletion) { '1 番茄 · 未设预计' } else { '0 番茄 · 未设预计' }
    if ((Find-ById $boundRow 'TaskProgress').Current.Name -ne $expectedProgress) { throw 'Task completion counted partial or free sessions.' }
    if ((Find-ById $boundRow 'TaskPeriod').Current.Name -notmatch [regex]::Escape($boundTotal)) { throw 'Task period totals disagree.' }
    Scroll-Content $window 100
    Save-WindowCapture $window 'TasksPage-focus-progress.png'
    Invoke-ById $boundRow 'TaskDelete'
    $items[0].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 220
    if ((Find-ById $window 'CurrentFocusTask').Current.Name -ne '英语阅读（已删除）') { throw 'Deleting a task lost the paused session binding.' }
    $items[1].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 220
    Invoke-ById $window 'TaskUndoDelete'
    $taskScroll = (Find-ById $window 'TaskList').GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern)
    if ($taskScroll.Current.VerticallyScrollable) { $taskScroll.SetScrollPercent(-1, 100); Start-Sleep -Milliseconds 220 }
    $boundRow = Find-TaskRow $window '英语阅读'
    if ($null -eq $boundRow) { throw 'Undo did not restore the bound task.' }
    if ((Find-ById $boundRow 'TaskFocusTime').Current.Name -ne $boundTotal) { throw 'Undo lost task progress.' }
    $items[0].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 220
    Invoke-ById $window 'TimerRestart'
    Select-FocusTask $window '整理今日笔记'
    Select-ById $window 'ModeFree'
    Invoke-ById $window 'FreePrimaryAction'
    Start-Sleep -Milliseconds 1200
    Invoke-ById $window 'FreePrimaryAction'
    $newTaskElapsed = Read-FreeValue $window
    Invoke-ById $window 'FreeFinish'
    $items[1].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 220
    $taskScroll = (Find-ById $window 'TaskList').GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern)
    if ($taskScroll.Current.VerticallyScrollable) { $taskScroll.SetScrollPercent(-1, 100); Start-Sleep -Milliseconds 220 }
    $row = Find-TaskRow $window '整理今日笔记'
    if ((Find-ById $row 'TaskFocusTime').Current.Name -ne $newTaskElapsed) { throw 'New free session was not bound to new task.' }
    $items[0].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 220
    Select-FocusTask $window '不绑定任务'
    Select-ById $window 'ModePomodoro'
    Write-Output 'PASS task selection, binding lock, completed count, shared totals, delete/undo and new-task free focus'

    $items[3].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 220
    Scroll-Content $window 0

    # Change actual appearance controls; verify runtime diagnostics, then capture rendered results.
    $light = Find-ById $window 'ThemeLight'
    $dark = Find-ById $window 'ThemeDark'
    $system = Find-ById $window 'ThemeSystem'
    $transparency = Find-ById $window 'ReduceTransparency'
    $motion = Find-ById $window 'ReduceMotion'
    $reset = Find-ById $window 'ResetAppearance'
    if ($null -eq $light -or $null -eq $dark -or $null -eq $reset) { throw 'Appearance controls are missing.' }

    $light.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 250
    if ($window.Current.HelpText -notmatch 'Theme=Light;') { throw 'Light theme was not applied.' }
    Save-WindowCapture $window 'SettingsPage-light.png'

    $dark.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 250
    if ($window.Current.HelpText -notmatch 'Theme=Dark;') { throw 'Dark theme was not applied.' }
    Save-WindowCapture $window 'SettingsPage-dark.png'
    $lightBrightness = Get-SurfaceBrightness 'SettingsPage-light.png'
    $darkBrightness = Get-SurfaceBrightness 'SettingsPage-dark.png'
    if ($lightBrightness - $darkBrightness -lt 60) { throw "Surface did not visibly change theme: light=$lightBrightness dark=$darkBrightness" }
    Write-Output "PASS rendered surface theme: light=$lightBrightness dark=$darkBrightness"
    $items[0].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 200
    Save-WindowCapture $window 'HomePage-dark.png'
    if (-not (Find-ById $window 'TimerPrimaryAction').Current.IsEnabled) { throw 'Timer controls were disabled by a theme switch.' }
    (Find-ById $window 'ModeFree').GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 220
    Scroll-Content $window 0
    Save-WindowCapture $window 'FreeFocus-dark.png'
    (Find-ById $window 'ModePomodoro').GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 220
    Write-Output 'PASS light/dark theme switch with active timer controls'

    $items[1].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 220
    Scroll-Content $window 100
    Save-WindowCapture $window 'TasksPage-dark.png'

    # Templates recreate views on navigation, so reacquire their automation elements.
    $items[3].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 220
    $transparency = Find-ById $window 'ReduceTransparency'
    $motion = Find-ById $window 'ReduceMotion'
    $reset = Find-ById $window 'ResetAppearance'
    $transparency.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
    $motion.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
    Start-Sleep -Milliseconds 250
    if ($window.Current.HelpText -notmatch 'Backdrop=Solid;Motion=False') { throw 'Reduced effects did not disable the backdrop and motion.' }
    Save-WindowCapture $window 'SettingsPage-solid.png'
    Write-Output ('PASS solid fallback and reduced motion: ' + $window.Current.HelpText)

    $reset.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Milliseconds 250
    $system = Find-ById $window 'ThemeSystem'
    if (-not $system.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Current.IsSelected) { throw 'Reset did not restore system theme selection.' }
    if ($transparency.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Current.ToggleState -ne [System.Windows.Automation.ToggleState]::Off) { throw 'Reset did not restore material preference.' }
    if ($motion.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Current.ToggleState -ne [System.Windows.Automation.ToggleState]::Off) { throw 'Reset did not restore motion preference.' }
    Write-Output ('PASS reset appearance: ' + $window.Current.HelpText)

    # Guard real keyboard input so it is never sent to another application's foreground window.
    $nativeHandle = [IntPtr]$window.Current.NativeWindowHandle
    [void][FloeWindowCapture]::SetForegroundWindow($nativeHandle)
    $items[0].SetFocus()
    Start-Sleep -Milliseconds 100
    if ([FloeWindowCapture]::GetForegroundWindow() -eq $nativeHandle) {
        [System.Windows.Forms.SendKeys]::SendWait('{DOWN}')
        Start-Sleep -Milliseconds 220
        if ($null -eq (Find-ById $window 'TasksPage')) { throw 'Arrow-key navigation failed.' }
        [System.Windows.Forms.SendKeys]::SendWait('{TAB}')
        Start-Sleep -Milliseconds 100
        [System.Windows.Forms.SendKeys]::SendWait('^4')
        Start-Sleep -Milliseconds 250
        if ($null -eq (Find-ById $window 'SettingsPage')) { throw 'Ctrl+4 navigation failed.' }
        Write-Output 'PASS keyboard arrows, Tab and Ctrl+4 navigation'
    } else {
        Write-Output 'SKIP keyboard: desktop focus could not be acquired safely'
    }

    $items[0].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    $transform = $window.GetCurrentPattern([System.Windows.Automation.TransformPattern]::Pattern)
    # WPF minimum dimensions are DIP; use the initial ratio to convert to physical pixels.
    $scale = $window.Current.BoundingRectangle.Width / 1080.0
    $transform.Resize(860 * $scale, 640 * $scale)
    Start-Sleep -Milliseconds 200
    Scroll-Content $window 0
    Save-WindowCapture $window 'HomePage-minimum.png'
    $items[2].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 220
    Scroll-Content $window 0
    Save-WindowCapture $window 'StatisticsPage-minimum.png'
    Scroll-Content $window 100
    Save-WindowCapture $window 'StatisticsPage-minimum-scrolled.png'
    $items[0].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 220
    Scroll-Content $window 0
    # Main navigation may expose ScrollPattern too; locate the viewport with a vertical range.
    $scrollHosts = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::IsScrollPatternAvailableProperty, $true)))
    $scrolled = $false
    foreach ($hostElement in $scrollHosts) {
        $scroll = $hostElement.GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern)
        if ($scroll.Current.VerticallyScrollable) {
            $scroll.SetScrollPercent(-1, 100)
            Start-Sleep -Milliseconds 220
            if ($scroll.Current.VerticalScrollPercent -lt 95) { throw 'Small-window content cannot scroll to the bottom.' }
            Save-WindowCapture $window 'HomePage-minimum-scrolled.png'
            $scroll.SetScrollPercent(-1, 0)
            $scrolled = $true
            break
        }
    }
    if (-not $scrolled) { Write-Output 'PASS minimum-height home fits without scrolling' }
    Write-Output 'PASS small-window content scrolling'
    Write-Output 'PASS window resize'

    (Find-ById $window 'ModeFree').GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 220
    Scroll-Content $window 0
    Save-WindowCapture $window 'FreeFocus-minimum.png'
    Scroll-Content $window 100
    Save-WindowCapture $window 'FreeFocus-minimum-scrolled.png'

    $items[1].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 220
    Scroll-Content $window 0
    Save-WindowCapture $window 'TasksPage-minimum.png'
    Scroll-Content $window 100
    Save-WindowCapture $window 'TasksPage-minimum-scrolled.png'

    $windowPattern = $window.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern)
    $windowPattern.SetWindowVisualState([System.Windows.Automation.WindowVisualState]::Maximized)
    Start-Sleep -Milliseconds 220
    if ($windowPattern.Current.WindowVisualState -ne [System.Windows.Automation.WindowVisualState]::Maximized) {
        throw 'Maximize failed.'
    }
    $windowPattern.SetWindowVisualState([System.Windows.Automation.WindowVisualState]::Normal)
    Write-Output 'PASS maximize and restore'

    # Save changed settings and close while a bound Pomodoro is still running.
    $items[3].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 220
    Select-ById $window 'ThemeDark'
    (Find-ById $window 'ReduceTransparency').GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
    (Find-ById $window 'ReduceMotion').GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
    Set-Input $window 'FocusMinutes' '2'
    Set-Input $window 'ShortBreakMinutes' '3'
    Set-Input $window 'LongBreakMinutes' '7'
    Set-Input $window 'FocusesPerCycle' '2'
    Invoke-ById $window 'ApplyPomodoroSettings'
    $items[0].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 220
    Select-ById $window 'ModePomodoro'
    Scroll-Content $window 0
    Select-FocusTask $window '英语阅读'
    Invoke-ById $window 'TimerPrimaryAction'
    Start-Sleep -Milliseconds 1200
    $timerBeforeClose = Parse-PomodoroTime (Read-TimerValue $window)
    $windowPattern.Close()
    if (-not $appProcess.WaitForExit(5000)) { throw 'Closing the window did not stop Floe.' }
    if ($appProcess.ExitCode -ne 0) { throw "Unexpected exit code: $($appProcess.ExitCode)" }
    Write-Output 'PASS close exits process cleanly'

    $appProcess.Dispose()
    $appProcess = Start-TestApplication
    $window = Wait-ApplicationWindow $appProcess
    $items = Read-NavigationItems $window
    Start-Sleep -Milliseconds 200
    if ($window.Current.HelpText -notmatch 'Theme=Dark;Backdrop=Solid;Motion=False') { throw 'Saved appearance did not return.' }
    if ((Find-ById $window 'TimerState').Current.Name -ne '已暂停') { throw 'Restart did not pause the Pomodoro.' }
    if ((Find-ById $window 'CurrentFocusTask').Current.Name -ne '英语阅读') { throw 'Restart lost the current task.' }
    $restoredTimer = Parse-PomodoroTime (Read-TimerValue $window)
    if ($restoredTimer -gt $timerBeforeClose -or ($timerBeforeClose - $restoredTimer).TotalSeconds -gt 2) { throw 'Normal close lost remaining time.' }
    $restoredToday = (Find-ById $window 'TodayFocus').Current.Name
    Start-Sleep -Milliseconds 1200
    if ((Read-TimerValue $window) -ne $restoredTimer.ToString('mm\:ss') -or (Find-ById $window 'TodayFocus').Current.Name -ne $restoredToday) { throw 'Reopened app counted offline time.' }
    Save-WindowCapture $window 'HomePage-recovered.png'
    Test-Statistics $window $restoredToday $completedPomodoros 1 'StatisticsPage-recovered'
    $items[3].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 220
    if ((Find-ById $window 'FocusMinutes').GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value -ne '2' -or
        (Find-ById $window 'ShortBreakMinutes').GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value -ne '3' -or
        (Find-ById $window 'LongBreakMinutes').GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value -ne '7' -or
        (Find-ById $window 'FocusesPerCycle').GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value -ne '2') { throw 'Saved Pomodoro configuration did not return.' }
    if ((Find-ById $window 'DataPath').Current.Name -ne (Join-Path $testDataDirectory 'floe.db')) { throw 'Smoke test did not use its private data directory.' }
    Scroll-Content $window 100
    Save-WindowCapture $window 'SettingsPage-local-data.png'
    $items[1].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 220
    if ((Find-ById $window 'TaskSummary').Current.Name -ne '待办 5 · 已完成 1') { throw 'Task states were lost on reopen.' }
    Write-Output 'PASS normal restart restores tasks, task binding, pending configuration and appearance without offline credit'

    $items[0].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 220
    Invoke-ById $window 'TimerPrimaryAction'
    Start-Sleep -Milliseconds 1200
    Invoke-ById $window 'TimerPrimaryAction'
    if ((Parse-PomodoroTime (Read-TimerValue $window)) -ge $restoredTimer) { throw 'Recovered Pomodoro did not resume.' }
    Invoke-ById $window 'TimerRestart'
    Select-FocusTask $window '整理今日笔记'
    Select-ById $window 'ModeFree'
    Invoke-ById $window 'FreePrimaryAction'
    Start-Sleep -Milliseconds 6500
    $freeBeforeCrash = [TimeSpan]::Parse((Read-FreeValue $window))
    Stop-Process -Id $appProcess.Id
    [void]$appProcess.WaitForExit(5000)
    $appProcess.Dispose()
    $appProcess = Start-TestApplication
    $window = Wait-ApplicationWindow $appProcess
    $items = Read-NavigationItems $window
    Select-ById $window 'ModeFree'
    $recoveredFree = [TimeSpan]::Parse((Read-FreeValue $window))
    if ($recoveredFree.TotalSeconds -lt 5 -or $recoveredFree -gt $freeBeforeCrash -or ($freeBeforeCrash - $recoveredFree).TotalSeconds -gt 2) { throw 'Crash recovery lost the committed free focus checkpoint.' }
    if ((Find-ById $window 'FreeState').Current.Name -ne '已暂停' -or (Find-ById $window 'CurrentFreeTask').Current.Name -ne '整理今日笔记') { throw 'Crash recovery lost the paused task binding.' }
    $afterCrashToday = (Find-ById $window 'TodayFocus').Current.Name
    Start-Sleep -Milliseconds 1200
    if ([TimeSpan]::Parse((Read-FreeValue $window)) -ne $recoveredFree -or (Find-ById $window 'TodayFocus').Current.Name -ne $afterCrashToday) { throw 'Crash recovery continued counting before resume.' }
    Save-WindowCapture $window 'FreeFocus-crash-recovered.png'
    Invoke-ById $window 'FreePrimaryAction'
    Start-Sleep -Milliseconds 1200
    Invoke-ById $window 'FreePrimaryAction'
    if ([TimeSpan]::Parse((Read-FreeValue $window)) -le $recoveredFree) { throw 'Recovered free session did not resume.' }
    Invoke-ById $window 'FreeFinish'
    Write-Output 'PASS forced process termination, committed checkpoint recovery, paused state and resume without duplicate credit'

    # Statistics stays live while visible, using the same timer rather than its own polling loop.
    Invoke-ById $window 'FreePrimaryAction'
    $items[2].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 220
    Scroll-Content $window 0
    $liveBefore = [TimeSpan]::Parse((Find-ById $window 'StatisticsTodayDuration').Current.Name)
    Start-Sleep -Milliseconds 1800
    $liveAfterText = (Find-ById $window 'StatisticsTodayDuration').Current.Name
    if ([TimeSpan]::Parse($liveAfterText) -le $liveBefore) { throw 'Visible statistics did not update with running focus.' }
    if ((Find-ById $window 'StatisticsPeriodTotal').Current.Name -ne $liveAfterText) { throw 'Live period total and daily total disagree.' }
    Save-WindowCapture $window 'StatisticsPage-live-dark.png'
    Scroll-Content $window 100
    Save-WindowCapture $window 'StatisticsPage-live-dark-detail.png'
    $items[0].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 220
    Invoke-ById $window 'FreePrimaryAction'
    Invoke-ById $window 'FreeFinish'
    Write-Output 'PASS visible statistics live updates from the shared focus timer'

    # The last soft deletion and its stable identity also survive a normal restart.
    $items[1].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 220
    Select-ById $window 'TaskFilterActive'
    Set-Input $window 'TaskTitleInput' '重启后撤销删除'
    Set-Input $window 'TaskEstimateInput' '4'
    Invoke-ById $window 'TaskSave'
    $taskScroll = (Find-ById $window 'TaskList').GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern)
    if ($taskScroll.Current.VerticallyScrollable) { $taskScroll.SetScrollPercent(-1, 0); Start-Sleep -Milliseconds 220 }
    $deletedRow = Find-TaskRow $window '重启后撤销删除'
    $deletedStableId = $deletedRow.Current.AutomationId
    Invoke-ById $deletedRow 'TaskDelete'
    $window.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
    if (-not $appProcess.WaitForExit(5000) -or $appProcess.ExitCode -ne 0) { throw 'Second normal close failed.' }
    $appProcess.Dispose()
    $appProcess = Start-TestApplication
    $window = Wait-ApplicationWindow $appProcess
    $items = Read-NavigationItems $window
    $items[1].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 220
    Invoke-ById $window 'TaskUndoDelete'
    $row = Find-TaskRow $window '重启后撤销删除'
    if ($null -eq $row -or $row.Current.AutomationId -ne $deletedStableId -or (Find-ById $row 'TaskProgress').Current.Name -ne '0 / 4 番茄') { throw 'Reopen undo lost task identity or estimate.' }
    Write-Output 'PASS deletion undo survives restart with the same task ID'
    }

    Test-MinimalWidget $appProcess $window
} finally {
    if (-not $appProcess.HasExited) { Stop-Process -Id $appProcess.Id }
    $appProcess.Dispose()
}

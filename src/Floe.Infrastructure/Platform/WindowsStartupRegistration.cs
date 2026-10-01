using System.Reflection;
using System.Security;
using Floe.Application.SystemIntegration;
using Microsoft.Win32;

namespace Floe.Infrastructure.Platform;

/// <summary>Per-user registration. Never touches other applications or StartupApproved policy.</summary>
public sealed class WindowsStartupRegistration(string launchCommand,
    string registryPath = @"Software\Microsoft\Windows\CurrentVersion\Run", string valueName = "Floe") : IStartupRegistration
{
    public bool IsEnabled => Access(() =>
    {
        using var key = Registry.CurrentUser.OpenSubKey(registryPath);
        return key?.GetValue(valueName) is string { Length: > 0 };
    });

    public void SetEnabled(bool enabled) => Access(() =>
    {
        if (enabled && launchCommand.Length > 260)
            throw new SystemIntegrationException("应用路径过长，Windows 无法保存此开机启动命令。请将应用移到较短的路径。");
        using var key = Registry.CurrentUser.CreateSubKey(registryPath, writable: true);
        if (enabled) key.SetValue(valueName, launchCommand, RegistryValueKind.String);
        else key.DeleteValue(valueName, throwOnMissingValue: false);
        return true;
    });

    private static T Access<T>(Func<T> operation)
    {
        try { return operation(); }
        catch (Exception error) when (error is UnauthorizedAccessException or SecurityException or IOException)
        { throw new SystemIntegrationException("无法访问开机启动设置，请检查 Windows 权限后重试。", error); }
    }

    public static WindowsStartupRegistration ForCurrentProcess()
    {
        var process = Environment.ProcessPath ?? throw new SystemIntegrationException("无法确定启动程序的位置。");
        var assembly = Assembly.GetEntryAssembly()?.Location ?? throw new SystemIntegrationException("无法确定应用的位置。");
        return new(BuildLaunchCommand(process, assembly));
    }

    public static string BuildLaunchCommand(string processPath, string assemblyPath)
    {
        static string Quote(string path)
        {
            if (!Path.IsPathFullyQualified(path) || path.Contains('"')) throw new ArgumentException("启动路径必须是有效的绝对路径。");
            return $"\"{path}\"";
        }
        var command = Path.GetFileNameWithoutExtension(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
            ? $"{Quote(processPath)} {Quote(assemblyPath)} --background"
            : $"{Quote(processPath)} --background";
        return command;
    }
}

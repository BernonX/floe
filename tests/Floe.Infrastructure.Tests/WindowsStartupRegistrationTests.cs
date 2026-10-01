using Floe.Infrastructure.Platform;
using Microsoft.Win32;

namespace Floe.Infrastructure.Tests;

public sealed class WindowsStartupRegistrationTests
{
    [Fact]
    public void LongLaunchPathOnlyRejectsEnablingAndDoesNotBlockApplicationStartup()
    {
        var command = WindowsStartupRegistration.BuildLaunchCommand(@"C:\" + new string('a', 250) + @"\Floe.exe", @"C:\Floe.dll");
        var startup = new WindowsStartupRegistration(command);
        Assert.Throws<Floe.Application.SystemIntegration.SystemIntegrationException>(() => startup.SetEnabled(true));
    }

    [Theory]
    [InlineData(@"C:\Program Files\Floe\Floe.exe", @"C:\Program Files\Floe\Floe.dll", "\"C:\\Program Files\\Floe\\Floe.exe\" --background")]
    [InlineData(@"E:\Floe SDK\dotnet.exe", @"E:\Floe App\Floe.dll", "\"E:\\Floe SDK\\dotnet.exe\" \"E:\\Floe App\\Floe.dll\" --background")]
    public void LaunchCommandsQuotePathsAndSupportFrameworkDependentDevelopment(string process, string assembly, string expected)
        => Assert.Equal(expected, WindowsStartupRegistration.BuildLaunchCommand(process, assembly));

    [Fact]
    public void StartupUsesOnlyItsOwnValueAndReadsActualRegistryState()
    {
        // An isolated key, never the real Windows Run key.
        var path = @"Software\Floe.Tests\" + Guid.NewGuid().ToString("N");
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(path);
            key.SetValue("OtherApplication", "untouched");
            var startup = new WindowsStartupRegistration("\"C:\\Floe.exe\" --background", path);
            Assert.False(startup.IsEnabled); startup.SetEnabled(true); Assert.True(startup.IsEnabled);
            Assert.Equal("\"C:\\Floe.exe\" --background", key.GetValue("Floe"));
            key.DeleteValue("Floe"); Assert.False(startup.IsEnabled);
            startup.SetEnabled(true); startup.SetEnabled(false); startup.SetEnabled(false);
            Assert.False(startup.IsEnabled); Assert.Equal("untouched", key.GetValue("OtherApplication"));
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false); }
    }

    [Theory]
    [InlineData("Floe.exe")] [InlineData("C:\\bad\"path\\Floe.exe")]
    public void InvalidLaunchPathsAreRejected(string path) => Assert.Throws<ArgumentException>(() => WindowsStartupRegistration.BuildLaunchCommand(path, @"C:\Floe.dll"));
}

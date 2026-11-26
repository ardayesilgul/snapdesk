using System.Text.Json;
using SnapDesk.Core;

namespace SnapDesk.Tests;

public class StartupRegistrationTests
{
    [Fact]
    public void BuildStartupCommand_QuotesPathAndAppendsTrayFlag()
    {
        var cmd = StartupRegistration.BuildStartupCommand(@"C:\Program Files\SnapDesk\SnapDesk.exe");
        Assert.Equal("\"C:\\Program Files\\SnapDesk\\SnapDesk.exe\" --tray", cmd);
    }

    [Fact]
    public void BuildStartupCommand_AvoidsDuplicateQuotes()
    {
        var cmd = StartupRegistration.BuildStartupCommand("  \"C:\\SnapDesk\\SnapDesk.exe\"  ");
        Assert.Equal("\"C:\\SnapDesk\\SnapDesk.exe\" --tray", cmd);
    }

    [Theory]
    [InlineData(@"C:\dist\SnapDesk.exe", true)]
    [InlineData(@"C:\Program Files\dotnet\dotnet.exe", false)]
    [InlineData(@"C:\tests\testhost.exe", false)]
    [InlineData(@"C:\bin\SnapDesk.dll", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void ShouldManageExecutable_FiltersTestHosts(string? path, bool expected)
    {
        Assert.Equal(expected, StartupRegistration.ShouldManageExecutable(path));
    }

    [Fact]
    public void Settings_StartWithWindows_DefaultsToTrueForLegacyJson()
    {
        var legacy = JsonSerializer.Deserialize<Settings>("{\"imageFormat\":\"png\"}");
        Assert.NotNull(legacy);
        Assert.True(legacy.StartWithWindows);

        var disabled = JsonSerializer.Deserialize<Settings>(
            "{\"startWithWindows\":false}",
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        Assert.NotNull(disabled);
        Assert.False(disabled.StartWithWindows);
    }

    [Fact]
    public void SingleInstance_BuildPipeName_IsolatesDifferentDesktops()
    {
        var defaultPipe = SingleInstance.BuildPipeName("Arda", "Default");
        var sandboxPipe = SingleInstance.BuildPipeName("Arda", "exebox-123");

        Assert.Equal(defaultPipe, SingleInstance.BuildPipeName("Arda", "Default"));
        Assert.NotEqual(defaultPipe, sandboxPipe);
    }

    [Fact]
    public void Theme_LoadsEmbeddedGeistTypography()
    {
        Assert.Equal("Geist", SnapDesk.UI.Theme.UiFont.FontFamily.Name);
        Assert.Equal("Geist SemiBold", SnapDesk.UI.Theme.TitleFont.FontFamily.Name);
        Assert.Equal("Geist Medium", SnapDesk.UI.Theme.UiFontMedium.FontFamily.Name);
    }
}

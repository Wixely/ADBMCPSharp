using ADBMCPSharp.Configuration;

namespace ADBMCPSharp.Tests;

public sealed class ConfigurationValidatorTests
{
    [Fact]
    public void RemoteServerRequiresHost()
    {
        var options = ValidOptions();
        options.Servers["remote"] = new() { Mode = AdbServerMode.Remote };
        Assert.True(new AdbOptionsValidator().Validate(null, options).Failed);
    }

    [Theory]
    [InlineData("com.example.valid", true)]
    [InlineData("com.example.valid_name2", true)]
    [InlineData("not-a-package", false)]
    [InlineData("com.example;command", false)]
    public void PackageNamesAreStrictlyValidated(string package, bool expectedValid)
    {
        var options = ValidOptions();
        options.Devices["living-room"].AllowedApps["player"] = new() { Package = package };
        Assert.Equal(expectedValid, new AdbOptionsValidator().Validate(null, options).Succeeded);
    }

    [Fact]
    public void NonLoopbackBindingRequiresStrongApiKey()
    {
        var result = new ServerOptionsValidator().Validate(null, new() { Host = "0.0.0.0" });
        Assert.True(result.Failed);
    }

    private static AdbOptions ValidOptions() => new()
    {
        Devices = new()
        {
            ["living-room"] = new() { Server = "local", Selector = "configured-selector" },
        },
    };
}

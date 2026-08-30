using ADBMCPSharp.Services;

namespace ADBMCPSharp.Tests;

public sealed class DiagnosticOutputParserTests
{
    [Fact]
    public void ParsesBatteryWithoutReturningRawFields()
    {
        var result = DiagnosticOutputParser.ParseBattery("""
            AC powered: false
            USB powered: true
            Wireless powered: false
            status: 2
            health: 2
            level: 83
            scale: 100
            voltage: 4210
            temperature: 275
            """);

        Assert.Equal(83, result.LevelPercent);
        Assert.Equal("Charging", result.Status);
        Assert.Equal("Good", result.Health);
        Assert.True(result.UsbPowered);
        Assert.Equal(275, result.TemperatureCelsiusTenths);
        Assert.Equal(4210, result.VoltageMillivolts);
    }

    [Fact]
    public void ParsesMemoryKilobytes()
    {
        var result = DiagnosticOutputParser.ParseMemory("""
            MemTotal:        4096000 kB
            MemFree:          512000 kB
            MemAvailable:    2048000 kB
            Buffers:           12000 kB
            Cached:           900000 kB
            SwapTotal:       1024000 kB
            SwapFree:         800000 kB
            """);

        Assert.Equal(4096000, result.TotalKilobytes);
        Assert.Equal(2048000, result.AvailableKilobytes);
        Assert.Equal(800000, result.SwapFreeKilobytes);
    }

    [Fact]
    public void ParsesStorageAndLoadData()
    {
        var storage = DiagnosticOutputParser.ParseStorage("""
            Filesystem     1K-blocks    Used Available Use% Mounted on
            /dev/block/dm-5  1000000  250000    750000  25% /data
            """);
        var load = DiagnosticOutputParser.ParseCpuLoad("0.12 0.34 0.56 1/200 1234");
        var runtime = DiagnosticOutputParser.ParseRuntime("12345.50 67890.25");

        Assert.Equal(1000000, storage.TotalKilobytes);
        Assert.Equal(25, storage.UsedPercent);
        Assert.Equal(0.12, load.OneMinute);
        Assert.Equal(0.56, load.FifteenMinutes);
        Assert.Equal(12345.50, runtime.UptimeSeconds);
    }

    [Fact]
    public void ParsesPhysicalAndOverrideDisplayValues()
    {
        var result = DiagnosticOutputParser.ParseDisplay(
            "Physical size: 3840x2160\nOverride size: 1920x1080",
            "Physical density: 320\nOverride density: 240");

        Assert.Equal(3840, result.PhysicalWidth);
        Assert.Equal(1080, result.OverrideHeight);
        Assert.Equal(320, result.PhysicalDensityDpi);
        Assert.Equal(240, result.OverrideDensityDpi);
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData("0", false)]
    [InlineData("unexpected", null)]
    public void ParsesBoundedSecurityValues(string output, bool? expected) =>
        Assert.Equal(expected, DiagnosticOutputParser.ParseBooleanProperty(output));
}

using ADBMCPSharp.Services;

namespace ADBMCPSharp.Tests;

public sealed class AdbMdnsParserTests
{
    [Fact]
    public void ParseRecognizesSupportedServiceTypesAndIgnoresOtherOutput()
    {
        const string output = """
            List of discovered mdns services
            adb-device-a _adb._tcp 192.0.2.10:5555
            adb-device-b _adb-tls-connect._tcp. 192.0.2.11:37123
            adb-device-c _adb-tls-pairing._tcp 192.0.2.12:38888
            unrelated _http._tcp 192.0.2.13:80
            """;

        var result = AdbMdnsParser.Parse(output, 25);

        Assert.Collection(result,
            candidate => Assert.Equal("LegacyTcpAdb", candidate.ServiceType),
            candidate => Assert.Equal("WirelessDebugging", candidate.ServiceType),
            candidate => Assert.Equal("Pairing", candidate.ServiceType));
    }

    [Fact]
    public void ParseDeduplicatesAndBoundsResults()
    {
        const string output = """
            adb-a _adb._tcp 192.0.2.10:5555
            adb-a _adb._tcp 192.0.2.10:5555
            adb-b _adb._tcp 192.0.2.11:5555
            adb-c _adb._tcp 192.0.2.12:5555
            """;

        Assert.Equal(2, AdbMdnsParser.Parse(output, 2).Count);
    }
}

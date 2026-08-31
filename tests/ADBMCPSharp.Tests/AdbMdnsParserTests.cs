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

    [Fact]
    public void ParseAcceptsIpv4HostnameAndBracketedIpv6Endpoints()
    {
        const string output = """
            first _adb._tcp 192.0.2.10:5555
            second _adb-tls-connect._tcp android-device.local:37123
            third _adb-tls-pairing._tcp [2001:db8::10]:38888
            """;

        Assert.Equal(3, AdbMdnsParser.Parse(output, 25).Count);
    }

    [Fact]
    public void ParseIgnoresMalformedEndpointsAndUnsupportedServices()
    {
        const string output = """
            missing-port _adb._tcp 192.0.2.10
            empty-host _adb._tcp :5555
            zero-port _adb._tcp 192.0.2.10:0
            oversized-port _adb._tcp 192.0.2.10:65536
            ambiguous-ipv6 _adb._tcp 2001:db8::10:5555
            bad-port _adb-tls-connect._tcp host:not-a-port
            unrelated _http._tcp 192.0.2.13:80
            valid _adb._tcp 192.0.2.14:5555
            """;

        var result = AdbMdnsParser.Parse(output, 25);

        Assert.Single(result);
        Assert.Equal("LegacyTcpAdb", result[0].ServiceType);
    }

    [Fact]
    public void ParseTreatsTrailingServiceDotAsTheSameAdvertisement()
    {
        const string output = """
            duplicate _adb-tls-connect._tcp host.example:37123
            duplicate _adb-tls-connect._tcp. host.example:37123
            """;

        Assert.Single(AdbMdnsParser.Parse(output, 25));
    }

    [Fact]
    public void ParseReturnsNoResultsForNonPositiveLimit()
    {
        Assert.Empty(AdbMdnsParser.Parse("device _adb._tcp 192.0.2.10:5555", 0));
        Assert.Empty(AdbMdnsParser.Parse("device _adb._tcp 192.0.2.10:5555", -1));
    }
}

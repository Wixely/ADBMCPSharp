using System.ComponentModel.DataAnnotations;

namespace ADBMCPSharp.Configuration;

public sealed class ServerOptions
{
    public const string SectionName = "Server";

    public string Host { get; set; } = "localhost";

    [Range(1, 65535)]
    public int Port { get; set; } = 5719;

    [RegularExpression("^/.*")]
    public string Path { get; set; } = "/mcp";

    public string WindowsServiceName { get; set; } = "ADBMCPSharp";

    [Range(1, 10_000)]
    public int RateLimitPermitLimit { get; set; } = 120;

    [Range(1, 3600)]
    public int RateLimitWindowSeconds { get; set; } = 60;

    // Supply via ADBMCP_Server__ApiKey or another protected configuration provider.
    public string? ApiKey { get; set; }
}

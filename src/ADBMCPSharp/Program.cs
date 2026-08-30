using System.Net;
using System.Threading.RateLimiting;
using ADBMCPSharp.Adb;
using ADBMCPSharp.Configuration;
using ADBMCPSharp.Hosting;
using ADBMCPSharp.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Options;
using Serilog;

var contentRoot = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
var isWindowsService = WindowsServiceHelpers.IsWindowsService();

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .WriteTo.File(Path.Combine(contentRoot, "logs", "adbmcp-bootstrap-.log"),
        rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7, shared: true)
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, ContentRootPath = contentRoot });
    builder.Configuration
        .SetBasePath(contentRoot)
        .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
        .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
        .AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true)
        .AddJsonFile("ADBMCPSharp.json", optional: false, reloadOnChange: true)
        .AddJsonFile($"ADBMCPSharp.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
        .AddJsonFile("ADBMCPSharp.Local.json", optional: true, reloadOnChange: true)
        .AddEnvironmentVariables()
        .AddEnvironmentVariables(prefix: "ADBMCP_")
        .AddCommandLine(args);

    builder.Services.AddSingleton<IValidateOptions<AdbOptions>, AdbOptionsValidator>();
    builder.Services.AddSingleton<IValidateOptions<ServerOptions>, ServerOptionsValidator>();
    builder.Services.AddOptions<AdbOptions>().Bind(builder.Configuration.GetSection(AdbOptions.SectionName)).ValidateDataAnnotations().ValidateOnStart();
    builder.Services.AddOptions<PolicyOptions>().Bind(builder.Configuration.GetSection(PolicyOptions.SectionName)).ValidateOnStart();
    builder.Services.AddOptions<ServerOptions>().Bind(builder.Configuration.GetSection(ServerOptions.SectionName)).ValidateDataAnnotations().ValidateOnStart();

    var initialServer = builder.Configuration.GetSection(ServerOptions.SectionName).Get<ServerOptions>() ?? new();
    if (isWindowsService) builder.Host.UseWindowsService(options => options.ServiceName = initialServer.WindowsServiceName);
    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext());

    builder.Services.AddSingleton<DeviceInventory>();
    builder.Services.AddSingleton<CapabilityPolicy>();
    builder.Services.AddSingleton<IAdbTransport, AdbProcessTransport>();
    builder.Services.AddSingleton<AndroidDeviceService>();
    builder.Services.AddRateLimiter(rateLimiter =>
    {
        rateLimiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        rateLimiter.AddPolicy("mcp", context => RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = initialServer.RateLimitPermitLimit,
                Window = TimeSpan.FromSeconds(initialServer.RateLimitWindowSeconds),
                QueueLimit = 0,
                AutoReplenishment = true,
            }));
    });
    builder.Services.AddMcpServer().WithHttpTransport().WithToolsFromAssembly();

    builder.WebHost.ConfigureKestrel(kestrel =>
    {
        if (string.Equals(initialServer.Host, "localhost", StringComparison.OrdinalIgnoreCase)) kestrel.ListenLocalhost(initialServer.Port);
        else if (IPAddress.TryParse(initialServer.Host, out var address)) kestrel.Listen(address, initialServer.Port);
        else kestrel.ListenAnyIP(initialServer.Port);
    });

    var app = builder.Build();
    // Force validated options now so invalid security/configuration fails before listening.
    var server = app.Services.GetRequiredService<IOptions<ServerOptions>>().Value;
    _ = app.Services.GetRequiredService<IOptions<AdbOptions>>().Value;

    app.UseSerilogRequestLogging();
    app.UseRateLimiter();
    app.UseMiddleware<ApiKeyMiddleware>();
    app.MapGet("/healthz", () => Results.Ok(new
    {
        status = "ok",
        service = "ADBMCPSharp",
        mcpPath = server.Path,
        authenticationRequired = !string.IsNullOrEmpty(server.ApiKey),
        timeUtc = DateTimeOffset.UtcNow,
    }));
    app.MapMcp(server.Path).RequireRateLimiting("mcp");

    Log.Information("ADBMCPSharp starting at http://{Host}:{Port}{Path}; mode={Mode}",
        server.Host, server.Port, server.Path, isWindowsService ? "WindowsService" : "Interactive");
    await app.RunAsync();
    return 0;
}
catch (Exception ex)
{
    Log.Fatal(ex, "ADBMCPSharp terminated unexpectedly");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}

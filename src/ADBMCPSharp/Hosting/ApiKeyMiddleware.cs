using System.Security.Cryptography;
using System.Text;
using ADBMCPSharp.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace ADBMCPSharp.Hosting;

public sealed class ApiKeyMiddleware(RequestDelegate next, IOptions<ServerOptions> options)
{
    private readonly ServerOptions _options = options.Value;

    public async Task InvokeAsync(HttpContext context)
    {
        if (string.IsNullOrEmpty(_options.ApiKey) || !context.Request.Path.StartsWithSegments(_options.Path))
        {
            await next(context);
            return;
        }

        var supplied = GetSuppliedKey(context.Request);
        if (supplied is null || !FixedTimeEquals(_options.ApiKey, supplied))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.Headers.WWWAuthenticate = "Bearer";
            await context.Response.WriteAsJsonAsync(new { error = "Authentication required." });
            return;
        }

        await next(context);
    }

    private static string? GetSuppliedKey(HttpRequest request)
    {
        if (request.Headers.TryGetValue("X-ADBMCP-Key", out var key) && key.Count == 1) return key[0];
        if (!request.Headers.TryGetValue(HeaderNames.Authorization, out var authorization) || authorization.Count != 1) return null;
        const string prefix = "Bearer ";
        var value = authorization[0];
        return value is not null && value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? value[prefix.Length..] : null;
    }

    private static bool FixedTimeEquals(string expected, string supplied)
    {
        var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        var suppliedHash = SHA256.HashData(Encoding.UTF8.GetBytes(supplied));
        return CryptographicOperations.FixedTimeEquals(expectedHash, suppliedHash);
    }
}

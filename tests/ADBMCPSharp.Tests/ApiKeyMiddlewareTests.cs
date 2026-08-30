using ADBMCPSharp.Configuration;
using ADBMCPSharp.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace ADBMCPSharp.Tests;

public sealed class ApiKeyMiddlewareTests
{
    private const string Key = "a-test-key-with-at-least-24-characters";

    [Fact]
    public async Task McpRequestWithoutKeyIsRejected()
    {
        var nextCalled = false;
        var middleware = new ApiKeyMiddleware(_ => { nextCalled = true; return Task.CompletedTask; }, Options.Create(new ServerOptions { ApiKey = Key }));
        var context = new DefaultHttpContext();
        context.Request.Path = "/mcp";
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        Assert.False(nextCalled);
    }

    [Fact]
    public async Task McpRequestWithBearerKeyContinues()
    {
        var nextCalled = false;
        var middleware = new ApiKeyMiddleware(_ => { nextCalled = true; return Task.CompletedTask; }, Options.Create(new ServerOptions { ApiKey = Key }));
        var context = new DefaultHttpContext();
        context.Request.Path = "/mcp";
        context.Request.Headers.Authorization = "Bearer " + Key;

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task HealthRequestDoesNotRequireMcpKey()
    {
        var nextCalled = false;
        var middleware = new ApiKeyMiddleware(_ => { nextCalled = true; return Task.CompletedTask; }, Options.Create(new ServerOptions { ApiKey = Key }));
        var context = new DefaultHttpContext();
        context.Request.Path = "/healthz";

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
    }
}

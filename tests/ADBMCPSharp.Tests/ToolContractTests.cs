using ADBMCPSharp.Tools;
using ModelContextProtocol.Server;

namespace ADBMCPSharp.Tests;

public sealed class ToolContractTests
{
    [Fact]
    public void ToolInputsNeverExposeRawAdbPrimitives()
    {
        string[] forbidden = ["serial", "address", "host", "port", "command", "intent", "package", "activity", "keycode", "path", "selector", "credential"];
        var methods = typeof(AdbTools).GetMethods().Where(method => method.GetCustomAttributes(typeof(McpServerToolAttribute), false).Length > 0);

        foreach (var parameter in methods.SelectMany(method => method.GetParameters()))
        {
            var name = parameter.Name ?? string.Empty;
            Assert.DoesNotContain(forbidden, word => name.Contains(word, StringComparison.OrdinalIgnoreCase));
        }
    }
}

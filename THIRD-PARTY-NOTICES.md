# Third-party notices

ADBMCPSharp uses the following NuGet dependencies. They are restored at build time and are not copied into this repository.

| Dependency | Purpose | License |
| --- | --- | --- |
| ModelContextProtocol.AspNetCore | MCP Streamable HTTP server | MIT |
| Microsoft.Extensions.Hosting.WindowsServices | Windows Service hosting | MIT |
| Serilog.AspNetCore and Serilog sinks | Structured logging | Apache-2.0 |
| xUnit, Microsoft.NET.Test.Sdk, coverlet.collector | Tests and coverage | Apache-2.0 / MIT |

Android Debug Bridge (`adb`) is an operator-installed runtime prerequisite. ADBMCPSharp does not bundle Android SDK Platform-Tools. Review the Android SDK terms and the provenance of the `adb` package you install.

# Third-party notices

ADBMCPSharp uses the following NuGet dependencies. They are restored at build time and are not copied into this repository.

| Dependency | Purpose | License |
| --- | --- | --- |
| ModelContextProtocol.AspNetCore | MCP Streamable HTTP server | MIT |
| Microsoft.Extensions.Hosting.WindowsServices | Windows Service hosting | MIT |
| Serilog.AspNetCore and Serilog sinks | Structured logging | Apache-2.0 |
| xUnit, Microsoft.NET.Test.Sdk, coverlet.collector | Tests and coverage | Apache-2.0 / MIT |

Android Debug Bridge (`adb`) is an operator-installed runtime prerequisite for native hosting. ADBMCPSharp does not copy Android SDK Platform-Tools into this repository. The provided Dockerfile installs Ubuntu Noble's `adb` binary package into the resulting image at build time; review that package's installed copyright file and Ubuntu source-package provenance when distributing the image.

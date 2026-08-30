# Agent instructions

Read `README.md` and `PLAN.md` before designing or implementing changes.

## Project boundary

- ADBMCPSharp is a general Android Debug Bridge MCP service. Keep its APIs, models, tools, packages, and project references within the generic Android-device domain; do not add product-specific integrations.
- Support local and remote ADB as first-class connection modes without assuming an NVIDIA Shield or Android TV device.
- Expose semantic, bounded operations. Do not expose a general ADB shell, arbitrary command strings, arbitrary intents, unrestricted input injection, or unrestricted filesystem access through MCP.
- Keep read-only inspection separate from device-control and administration capabilities. New write or high-impact operations must be explicitly gated and disabled by default.

## MCPSharp family conventions

- Build ADBMCPSharp as an independently deployable Wixely MCPSharp service intended for public GitHub under MIT.
- Before scaffolding, inspect the current public `Wixely/RemoteAdminMCPSharp`, `Wixely/BambuMCPSharp`, `Wixely/HomeAssistantMCPSharp`, and `Wixely/MCPHub` repositories.
- Follow their current conventions for .NET/ASP.NET Core hosting, `ModelContextProtocol.AspNetCore` Streamable HTTP, configuration layering, health endpoints, Windows Service behavior, Serilog, feature gates, Docker, tests, release packaging, and MCPHub metadata where those conventions fit this project's risks.
- Use those repositories as design/source references only. Do not add project, package, source-copy, runtime, or release dependencies on another service repository.
- Recheck current package versions and ecosystem conventions during implementation; do not blindly copy pinned versions from an older service.

## Development defaults

- Use C# on .NET 10 with top-level statements.
- Prefer NativeAOT and a single self-contained executable when the selected MCP and ADB dependencies support them; document exceptions.
- Use Windows PowerShell 5.1-compatible automation. Do not introduce Python, Node.js, Tailwind CSS, or tooling that requires them without explicit permission.
- Treat Windows as the primary development/host platform and Linux as secondary.
- Support interactive execution and Windows Service hosting on Windows, interactive execution and systemd on Linux, and Docker deployment.
- Include repository-local VS Code build, run, and debug configurations when the runnable solution is scaffolded.
- Keep third-party dependencies optional and focused. Prefer MIT or Apache-2.0 licensing and document all distributed dependencies and assets.

## Security and source control

- Never commit device addresses, serials, pairing codes, ADB keys, private keys, credentials, screenshots, logs, local paths, or real device metadata.
- Store secrets in protected runtime configuration, never in sample JSON, environment templates, tool arguments, or logs.
- This local repository is intended for `Wixely/ADBMCPSharp` on public GitHub under MIT, but no remote exists yet. Do not create the remote or publish source until explicitly requested.
- Before any future public push, configure the repository-local Wixely GitHub identity and complete the full outgoing-history privacy review.
- Keep `PLAN.md` current as decisions are made; preserve assumptions and unresolved choices as such.

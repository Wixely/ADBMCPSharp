# ADBMCPSharp starting plan

- Status: First vertical slice implemented; real-device acceptance pending
- Created: 2026-08-30
- Owner: Wixely / Agent
- Target stack: C# and .NET 10
- Intended family: Wixely MCPSharp
- Intended host/license: Public GitHub / MIT; remote not yet created

## Product boundary

ADBMCPSharp is a standalone Android-device MCP server. It owns ADB connection management, device identity/aliasing, structured Android inspection, guarded device controls, and operational diagnostics. Its contracts and dependencies remain within the generic Android-device domain.

## First usable outcome

A trusted MCP client can:

1. list configured device aliases without receiving secrets or raw connection details;
2. inspect connection/authorization state and a bounded Android/device summary;
3. identify the foreground package and basic power/display state when supported;
4. wake one explicitly selected device and launch one allowlisted package after write controls are enabled; and
5. re-read state to report whether the requested result was observed.

The slice must be tested with a local ADB server and a remote ADB server. Network ADB pairing/setup may remain an operator workflow initially, but runtime reconnect and authorization failures must be represented accurately.

## Implementation status (2026-08-30)

Implemented:

- .NET 10 Streamable HTTP MCP host with loopback-default binding, API-key enforcement for network binding, per-client rate limiting, health endpoint, Serilog, Windows Service support, and systemd example.
- External `adb` process adapter using shell-free argument lists, bounded output, cancellation, timeouts, and classified offline/unauthorized/unavailable failures.
- Named local/remote ADB servers, named devices, server-side selectors, strict package/alias validation, and no raw selectors or server coordinates in tool results.
- Bounded device/app inspection plus independently gated power, navigation/media/volume, allowlisted app launch, and separately gated app stop.
- Per-device serialization, redacted audit events, and observable postcondition checks.
- Self-contained single-file Windows/Linux publishing scripts, repository-local VS Code debugging, CI, MIT licensing, and third-party notices.
- Unit/contract tests for parsing, policy gates, configuration safety, structured status, postcondition verification, and forbidden tool inputs.

Not yet verified or claimed:

- Physical/emulated Android device operation, including platform-specific `dumpsys` variants.
- Local USB/emulator and remote ADB server acceptance on Windows and Linux.
- Windows Service and systemd installation on target hosts.
- Docker networking, USB access, and ADB key persistence; Docker packaging remains deferred.
- NativeAOT; reflection-based MCP tool discovery currently uses the normal .NET runtime.

## Investigation work

- Compare use of the official `adb` executable with maintained managed .NET ADB protocol libraries and a purpose-built minimal protocol client.
- Verify local server selection, remote server host/port selection, device transport selection, TCP-connected targets, Android wireless-debugging pairing, reconnect behavior, and server-version negotiation.
- Review licensing and redistribution conditions for Android platform-tools before bundling any executable. Prefer an external configured dependency unless redistribution is clearly permitted and operationally justified.
- Measure command cancellation, timeout behavior, output encodings, daemon startup behavior, concurrent device operations, and recovery after device/server disconnect.
- Verify Windows and Linux behavior. Validate Docker networking and device/credential persistence before claiming container support.
- Determine which device facts are stable and safe enough to expose without leaking identifiers or personal data.

### Transport decision

The first slice uses an operator-installed official `adb` executable behind `IAdbTransport`. This preserves local and remote-server behavior (`-H`/`-P`), avoids redistributing platform-tools, avoids another protocol implementation and its pairing/version compatibility burden, and remains directly testable through a fake transport. Commands are selected from a closed internal enum and emitted with `ProcessStartInfo.ArgumentList`; there is no shell or general command adapter. Revisit a managed protocol client only if measured process overhead, cancellation, packaging, or remote-server behavior fails acceptance.

## Proposed architecture

- **Host:** `Microsoft.NET.Sdk` .NET 10 executable using `Microsoft.AspNetCore.App`, the Generic Host, `ModelContextProtocol.AspNetCore` Streamable HTTP, and health/readiness diagnostics.
- **Connection inventory:** validated configuration for named ADB servers and named device targets. Tool contracts use aliases only.
- **ADB transport adapter:** a narrow interface for server/device discovery, command execution, cancellation, and structured error classification.
- **Device operations:** application services that translate semantic requests into allowlisted ADB behavior and verify postconditions.
- **Capability policy:** read-only default plus independent gates for categories of device control.
- **Audit/diagnostics:** bounded, redacted metadata for state-changing requests and connection failures.
- **Tool layer:** small MCP tools over the application services; no raw protocol or command passthrough.

Use the current public Wixely MCPSharp repositories as scaffolding references: RemoteAdminMCPSharp for named remote/local targets and guarded remote operations, BambuMCPSharp for device-service safety and packaging, HomeAssistantMCPSharp for feature toggles and typed/curated tools, and MCPHub for managed-service integration expectations. Retain the family conventions for central package management, JSON/environment/command-line configuration, Serilog, service hosting, Docker, xUnit tests, GitHub Actions, and release assets unless a documented ADB-specific constraint requires a change.

These are reference implementations, not dependencies. ADBMCPSharp must not reference their projects, consume their service assemblies, copy their configuration identities, or require them at runtime.

Do not split assemblies until a tested vertical slice demonstrates a useful boundary. Do not build an abstraction layer that merely renames every ADB command.

## Candidate read-only MCP tools

- `adb_list_devices` - aliases, high-level state, connection mode, and capabilities.
- `adb_get_device_status` - availability, authorization/offline state, Android/API level, model class, display/power state, and foreground package where supported.
- `adb_list_allowed_apps` - server-configured application aliases that may later be launched.
- `adb_get_app_status` - installed/running/foreground state for one allowlisted application alias.
- `adb_get_capabilities` - effective deployment and per-device feature gates.

Every collection must have bounded result counts. Sensitive raw properties must be allowlisted rather than returned wholesale.

## Candidate guarded control tools

- `adb_wake_device` and `adb_sleep_device`.
- `adb_launch_app` for an allowlisted application alias.
- `adb_send_navigation` using a closed action enum such as home, back, up, down, left, right, select, menu, play/pause, next, and previous.
- `adb_set_volume` or bounded volume-up/down/mute actions when device behavior is reliable.
- `adb_stop_app` only behind a separate explicit gate.
- `adb_capture_screen` only if a privacy review defines content handling, size limits, retention, and MCP image behavior.

Tools must not accept raw package/activity names, intent strings, shell fragments, key-code integers, coordinates, local/remote paths, or device connection details.

## Configuration outline

- Server listen address/port and MCP authentication.
- Named ADB servers with local or remote mode, host/port where applicable, and server startup policy.
- Named devices with server alias, non-secret selector strategy, operator-facing alias, and capability overrides.
- Protected locations for ADB keys and wireless-debugging credentials when required.
- Global read-only setting and category gates.
- Allowlists for applications and navigation/media actions.
- Connection, operation, and postcondition-verification timeouts.
- Audit enablement, retention, and redaction policy.

Use JSON, environment variables, and command-line configuration consistently with the MCPSharp ecosystem. Never place live secrets or real device identifiers in checked-in examples.

## Service and deployment requirements

- Run interactively and as a Windows Service on Windows.
- Run interactively and under systemd on Linux.
- Provide Docker only after local/remote ADB server networking and key persistence are verified.
- Prefer a self-contained NativeAOT executable when dependencies and dynamic MCP behavior support it; otherwise document the runtime deployment exception.
- Include PowerShell 5.1 build/test/publish scripts and repository-local VS Code debugging.
- Add Windows and Linux CI, explicit short artifact retention, source-link/symbol/release packaging, and GitHub Release assets if public GitHub hosting is approved.

## Security and privacy requirements

- Threat-model ADB's high privilege, token/key theft, remote-server impersonation, unauthorized devices, command injection, allowlist bypass, malicious package names/output, log leakage, screenshots, and denial of service.
- Bind conservatively and require MCP authentication before network exposure.
- Treat ADB keys, pairing codes, device serials, addresses, installed packages, foreground apps, accounts, notifications, screenshots, and file paths as sensitive.
- Keep credentials and device selectors server-side; tools receive aliases and opaque handles.
- Sanitize all ADB output before logging or returning it.
- Apply per-device locking or conflict rules so concurrent actions cannot create unsafe interleavings.
- Do not claim completion from command exit alone; verify observable state where practical and return uncertainty explicitly.

## Test outline

- Fake ADB transport contract tests for local/remote server selection, parsing, timeouts, cancellation, disconnects, and malformed output.
- Effective-policy tests for read-only and each category gate.
- Tool-schema tests proving raw command, intent, key-code, path, address, and credential inputs are absent.
- Authentication, rate-limit, audit-redaction, and health-state tests.
- Integration tests with an emulator or disposable test device for safe read/navigation operations.
- Manual acceptance on representative Android targets for wake, home/back/navigation, application launch, reconnect, standby, and remote-server use.
- Windows and Linux publish/startup verification; Docker only after its ADB topology is defined.

## Open questions

- Does "remote ADB" require both a remote ADB server and direct network-connected Android targets in the first release?
- Which Android device/emulator version, ADB mode, and network topology are the first acceptance target?
- Should the service manage wireless-debugging pairing, or only consume operator-established connections initially?
- Is the official `adb` executable an acceptable runtime prerequisite, and may it ever be redistributed?
- Which device controls beyond wake, navigation/media keys, volume, and allowlisted app launch are required initially?
- Should screenshots be in scope, and if so, may they be returned only in-memory or also saved to a confined directory?
- Is one active operation per device sufficient, or must some read operations run concurrently?
- Which MCP transports and authentication model should the initial release support?
- Which initial release and MCPHub catalogue milestone should follow technical acceptance?
- Which TLS-terminating reverse proxy or private overlay will protect non-loopback MCP traffic in the target deployment?

## Next actions

- [ ] Record the target Android version, local/remote ADB topology, and required first controls. - Owner: User / Agent
- [x] Compare ADB executable and managed-protocol options, including licensing, packaging, remote-server support, pairing, NativeAOT, and testability. - Owner: Agent; completed 2026-08-30
- [x] Write the transport contract and fake-transport acceptance tests before selecting the implementation. - Owner: Agent; completed 2026-08-30
- [x] Implement read-only local and remote server configuration with aliases and redacted diagnostics. - Owner: Agent; completed 2026-08-30
- [x] Define the capability matrix and add wake plus allowlisted application launch behind explicit gates. - Owner: Agent; completed 2026-08-30
- [ ] Verify postcondition reporting on the primary Android target and a second target or emulator. - Owner: Agent
- [ ] Run Windows local-server and Linux remote-server acceptance, then review the result on 2026-09-30. - Owner: User / Agent
- [ ] Define and test Docker ADB networking/key persistence before adding a Dockerfile. - Owner: TBD; review 2026-09-30
- [ ] After technical acceptance, create `Wixely/ADBMCPSharp`, complete the public pre-push review, publish under MIT, and add MCPHub integration as a separately verified milestone. - Owner: User / Agent

## Recommended next action

Configure one disposable/emulated device alias in ignored local configuration and run read-only acceptance against both the local and remote ADB-server modes before enabling any write gate. Owner: User / Agent.

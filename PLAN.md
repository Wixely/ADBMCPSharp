# ADBMCPSharp delivery plan

- Status: Release candidate implemented and accepted on the primary physical target; public GitHub creation is blocked on authentication
- Created: 2026-08-30
- Last updated: 2026-08-31
- Owner: Wixely / Agent
- Target stack: C# and .NET 10
- Intended family: Wixely MCPSharp
- Intended host/license: Public GitHub / MIT; remote not yet created
- Source-control handling: keep this working plan uncommitted unless the user explicitly changes that instruction

## Product boundary

ADBMCPSharp is a standalone Android-device MCP server. It owns ADB connection management, device identity/aliasing, structured Android inspection, guarded device controls, and operational diagnostics. Its contracts and dependencies remain within the generic Android-device domain.

At the user's explicit request on 2026-08-30, the service also includes an independently gated break-glass arbitrary device-command capability. This is a documented exception to the normal semantic-only boundary: it remains disabled by default, requires a per-device override and per-call confirmation, fixes the configured device selector ahead of client arguments, rejects server-management/selector options, bounds arguments/time/output, and never logs argument content or output.

## First usable outcome

A trusted MCP client can:

1. list configured device aliases without receiving secrets or raw connection details;
2. inspect connection/authorization state and a bounded Android/device summary;
3. identify the foreground package and basic power/display state when supported;
4. wake one explicitly selected device and launch one allowlisted package after write controls are enabled; and
5. re-read state to report whether the requested result was observed.

The first usable outcome has been accepted against the primary physical target through the normal local ADB path, an isolated local remote-server mode, and a two-container remote ADB topology. Network ADB pairing/setup remains an operator workflow; runtime reconnect and authorization failures are represented explicitly.

## Implementation status (2026-08-31)

Implemented:

- .NET 10 Streamable HTTP MCP host with loopback-default binding, API-key enforcement for network binding, per-client rate limiting, health endpoint, Serilog, Windows Service support, and systemd example.
- External `adb` process adapter using shell-free argument lists, bounded output, cancellation, timeouts, and classified offline/unauthorized/unavailable failures.
- Named local/remote ADB servers, named devices, server-side selectors, strict package/alias validation, and no raw selectors or server coordinates in tool results.
- Separately gated passive ADB mDNS discovery with bounded results, opaque short-lived handles, and redaction of service names, addresses, ports, and serial-derived identifiers.
- Discovery never issues pair/connect commands; already-paired auto-connect remains an ADB-server policy controlled on the local or remote server host.
- Bounded device/app inspection plus independently gated power, navigation/media/volume, allowlisted app launch, and separately gated app stop.
- Application launch supports closed `Start`, `Foreground`, and `WakeAndForeground` modes, bounded Android TV launcher fallback, screensaver dismissal, and observable foreground verification.
- Privacy-gated installed-package enumeration with closed all/user/system scopes, strict parsing, and server-side result bounds.
- Curated read-only battery, memory, storage, CPU-load, runtime, display, and security diagnostics with fixed commands, structured parsers, per-device gating, and a global option allowlist.
- Allowlisted media-session inspection with optional metadata redaction, closed media controls, and separately gated volume controls.
- Checksum-pinned, size/time-bounded APK installation from configured local or HTTPS artifacts, plus explicitly allowlisted application removal; both are disabled by default and require per-call confirmation.
- Break-glass arbitrary device-scoped ADB arguments with global, device, and per-call gates; fixed selector scoping; validation bounds; redacted auditing; and disabled-by-default configuration.
- Per-device serialization, redacted audit events, and observable postcondition checks.
- Guarded configured-device connect, reconnect, and disconnect operations with independent global/device gates, per-call confirmation, retries, redacted audit data, and final-state verification.
- OCI container deployment with non-root service and ADB-server containers, private networking, API-key authentication, persistent ADB trust keys, health checks, and Docker-compatible smoke harnesses.
- Self-contained single-file Windows/Linux publishing, reproducible versioned archives, SHA-256 manifests, release documentation, repository-local VS Code debugging, CI, MIT licensing, and third-party notices.
- 98 unit/contract tests covering parsing, policy gates, configuration safety, structured status, postcondition verification, transport contracts, and forbidden tool inputs.

Accepted on the primary physical target:

- Read-only status, application inventory, seven curated diagnostics, media information, and authorization/connection health.
- Wake, Home navigation, application launch/foreground/stop, media Pause/Play, and volume Down/Up with state restoration where applicable.
- Connection lifecycle through local and isolated remote ADB-server modes.
- Two-container remote ADB operation with both containers non-root, a private bridge network, authenticated MCP, persistent ADB keys, and successful ADB-server replacement.
- Rootless Podman execution of the OCI image and both container topology paths.

Not yet verified or claimed:

- A second physical target or disposable emulator, including additional Android versions and platform-specific `dumpsys` variants.
- APK install/uninstall acceptance against a disposable target; no real package changes have been performed during development.
- Break-glass arbitrary-command acceptance has not been performed against a real device because commands may be destructive and return sensitive output.
- Local USB/emulator operation; current physical acceptance uses an operator-established network ADB connection.
- Installed Windows Service and native Linux systemd operation on representative hosts.
- Exact Docker Engine execution; the compatible smoke harnesses have passed under rootless Podman and are configured in GitHub Actions.
- NativeAOT; reflection-based MCP tool discovery currently uses the normal .NET runtime.
- Public GitHub CI and release publication; `Wixely/ADBMCPSharp` does not exist yet and the available Git/browser sessions are not authenticated.

## Investigation work

- Revisit the official `adb` executable versus a managed protocol client only if measured process, cancellation, packaging, or compatibility behavior warrants the added implementation burden.
- Extend verified coverage to USB/emulator transports, another Android version, and a native cross-host Linux ADB server.
- Review licensing and redistribution conditions for Android platform-tools before bundling any executable. Prefer an external configured dependency unless redistribution is clearly permitted and operationally justified.
- Measure command cancellation, timeout behavior, output encodings, daemon startup behavior, concurrent device operations, and recovery after device/server disconnect.
- Verify installed Windows Service and native Linux systemd behavior, then observe the existing container harnesses on Docker Engine.
- Determine which device facts are stable and safe enough to expose without leaking identifiers or personal data.

### Transport decision

The first slice uses an operator-installed official `adb` executable behind `IAdbTransport`. This preserves local and remote-server behavior (`-H`/`-P`), avoids redistributing platform-tools, avoids another protocol implementation and its pairing/version compatibility burden, and remains directly testable through a fake transport. Commands are selected from a closed internal enum and emitted with `ProcessStartInfo.ArgumentList`; there is no shell or general command adapter. Revisit a managed protocol client only if measured process overhead, cancellation, packaging, or remote-server behavior fails acceptance.

## Implemented architecture

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

## Implemented read-only MCP tools

- `adb_list_adb_servers` - configured server aliases, modes, and effective discovery gate without coordinates.
- `adb_discover_devices` - bounded passive mDNS advertisements through one configured server alias; disabled by default and redacted.
- `adb_list_devices` - aliases, high-level state, connection mode, and capabilities.
- `adb_get_device_status` - availability, authorization/offline state, Android/API level, model class, display/power state, and foreground package where supported.
- `adb_list_allowed_apps` - server-configured application aliases that may later be launched.
- `adb_get_app_status` - installed/running/foreground state for one allowlisted application alias.
- `adb_list_installed_apps` - privacy-gated, bounded package identifiers for an all/user/system scope.
- `adb_list_diagnostics` - curated diagnostic names, descriptions, and effective enablement for one device.
- `adb_run_diagnostic` - one fixed battery, memory, storage, CPU-load, runtime, display, or security diagnostic with structured output only.
- `adb_get_capabilities` - effective deployment and per-device feature gates.

Every collection must have bounded result counts. Sensitive raw properties must be allowlisted rather than returned wholesale.

## Implemented guarded control tools

- `adb_wake_device` and `adb_sleep_device`.
- `adb_launch_app` for an allowlisted application alias and closed launch mode.
- `adb_send_navigation` using a closed action enum and global allowlist.
- `adb_get_media_status`, `adb_send_media_action`, and `adb_send_volume_action` behind independent gates and closed action allowlists.
- `adb_stop_app` only behind a separate explicit gate.
- `adb_connect_device`, `adb_reconnect_device`, and `adb_disconnect_device` for a server-configured target with per-call confirmation.
- `adb_install_apk` and `adb_uninstall_app` for checksum-pinned/configured artifacts and allowlisted packages with per-call confirmation.
- `adb_execute_arbitrary_command` as an explicitly documented break-glass exception with global, device, and per-call gates.

Screen capture remains outside the first release unless a later privacy review defines content handling, size limits, retention, and MCP image behavior.

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
- Provide Docker with private-network, non-root, remote ADB-server, and persistent-key examples; retain exact Docker Engine execution as an outstanding acceptance gate.
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
- Windows and Linux publish/startup verification plus basic and remote-topology container smoke tests.

## Remaining decisions

- Should screenshots enter a later milestone, and if so, may they be returned only in memory or also saved to a confined directory?
- Is one active operation per device sufficient long term, or should safe read operations gain limited concurrency?
- When should the pinned MCP package receive its separately tested major-version upgrade?
- Should a GitHub release be created immediately after CI succeeds, or should native Windows Service and systemd acceptance block the first tag?
- Which MCPHub catalogue and managed-service integration milestone should follow the first public release?
- Which TLS-terminating reverse proxy or private overlay will protect non-loopback MCP traffic in the target deployment?

## Next actions

- [x] Record and exercise the primary physical target, local/remote ADB topology, and required first controls. - Owner: User / Agent; completed 2026-08-31
- [x] Compare ADB executable and managed-protocol options, including licensing, packaging, remote-server support, pairing, NativeAOT, and testability. - Owner: Agent; completed 2026-08-30
- [x] Write the transport contract and fake-transport acceptance tests before selecting the implementation. - Owner: Agent; completed 2026-08-30
- [x] Implement read-only local and remote server configuration with aliases and redacted diagnostics. - Owner: Agent; completed 2026-08-30
- [x] Define the capability matrix and add wake plus allowlisted application launch behind explicit gates. - Owner: Agent; completed 2026-08-30
- [x] Add separately gated media inspection/control, volume control, checksum-pinned APK installation, and explicitly allowlisted app removal. - Owner: Agent; completed 2026-08-30
- [x] Add a curated, allowlisted read-only diagnostic catalog with structured output and no raw diagnostic response. - Owner: Agent; completed 2026-08-30
- [ ] Verify postcondition reporting on a second target or disposable emulator; primary-target reporting is accepted. - Owner: Agent; review 2026-09-30
- [ ] Run native Linux remote-server and systemd acceptance; Windows local/isolated-remote and Linux containerized-remote paths are accepted. - Owner: User / Agent; review 2026-09-30
- [ ] Verify mDNS discovery and redaction with legacy TCP ADB and modern wireless debugging on both a local and remote ADB server. - Owner: User / Agent; review 2026-09-30
- [x] Define and test container ADB networking, non-root operation, authentication, server replacement, and key persistence under rootless Podman. - Owner: Agent; completed 2026-08-31
- [ ] Observe both container smoke harnesses on exact Docker Engine through GitHub Actions. - Owner: Agent; blocked until the public repository is created
- [ ] Accept APK installation/removal on a disposable target and decide whether break-glass physical acceptance is necessary. - Owner: User / Agent; review 2026-09-30
- [x] Add reproducible `0.1.0` Windows/Linux archives, checksums, dependency audits, release documentation, and packaged Windows smoke coverage. - Owner: Agent; completed 2026-08-31
- [x] Complete the pre-publication reachable-history, metadata, path, credential, device-data, and binary privacy review. - Owner: Agent; completed 2026-08-31
- [ ] Authenticate GitHub, create public `Wixely/ADBMCPSharp`, push reviewed `main`, and observe CI. - Owner: User / Agent; blocked on user sign-in
- [ ] Add MCPHub integration as a separately verified milestone after the first public release. - Owner: Agent

## Recommended next action

Sign in to GitHub in the already opened browser session, then create public `Wixely/ADBMCPSharp`, push the reviewed `main` history, and observe the Windows/Linux/package/Docker CI jobs. Owner: User for sign-in; Agent for creation, push, and CI follow-through.

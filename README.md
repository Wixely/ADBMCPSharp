# ADBMCPSharp

ADBMCPSharp is a .NET 10 MCP server for controlled Android Debug Bridge access. It maps configured aliases to local or remote ADB-server devices and exposes bounded inspection and explicitly gated controls over MCP Streamable HTTP. Normal tools never expose a raw shell, arbitrary intents, arbitrary key codes, device selectors, network addresses, or filesystem paths. An optional break-glass arbitrary-command tool can deliberately bypass those semantic boundaries for specifically enabled devices.

The current implementation is runnable and has automated contract coverage. Its inspection, diagnostics, application, media, package, reversible-control, and connection-lifecycle paths have also been accepted against a configured physical Android device through local, isolated remote-mode, and two-container remote ADB topologies. The OCI image, authenticated MCP boundary, private container network, non-root service and ADB-server containers, and persisted ADB key replacement path have been accepted with rootless Podman. Exact Docker Engine execution and public release remain outstanding.

## Included MCP tools

Read-only inspection is enabled by default:

- `adb_list_devices`
- `adb_get_device_status`
- `adb_list_allowed_apps`
- `adb_get_app_status`
- `adb_get_capabilities`

Curated device diagnostics are read-only, structured, and disabled by default:

- `adb_list_diagnostics` lists `Battery`, `Memory`, `Storage`, `CpuLoad`, `Runtime`, `Display`, and `Security`, including effective enablement for the selected device.
- `adb_run_diagnostic` runs one globally allowlisted option. It returns only selected fields and never exposes raw ADB diagnostic output.

Passive network discovery is read-only but disabled by default:

- `adb_list_adb_servers` lists configured aliases and local/remote modes without coordinates.
- `adb_discover_devices` invokes the selected ADB server's mDNS discovery and returns bounded, redacted advertisements.

Configured connection lifecycle separates read-only health from guarded changes:

- `adb_get_connection_health` reports redacted online, offline, unauthorized, unavailable, or unknown state for a configured alias.
- `adb_connect_device`, `adb_reconnect_device`, and `adb_disconnect_device` use only the server-side profile. They require the global connection-management gate, the selected device's independent gate, and per-call confirmation.
- Lifecycle changes are serialized per device, audited without endpoints, bounded by time and retry limits, and verified when observable.

Installed-application enumeration is privacy-sensitive and disabled by default:

- `adb_list_installed_apps` returns a bounded list of package identifiers for `All`, `User`, or `System` scope.

Media inspection and control are independently gated and disabled by default:

- `adb_get_media_status` returns the recognized allowlisted media-app alias, playback state, position, speed, and optionally bounded title/artist/album metadata. Unrecognized package identities are redacted.
- `adb_send_media_action` accepts only `Play`, `Pause`, `PlayPause`, `Stop`, `Next`, `Previous`, `FastForward`, or `Rewind` when that action is globally allowlisted.
- `adb_send_volume_action` accepts only `Up`, `Down`, or `Mute` when independently enabled and allowlisted.

Package administration is high-impact, disabled by default, and requires explicit confirmation on every request:

- `adb_list_installable_apks` lists configured artifact aliases without exposing their source, hash, path, URL, or package identifier.
- `adb_install_apk` installs only a server-configured artifact allowed for that device after size and SHA-256 verification.
- `adb_uninstall_app` removes only an allowlisted application whose per-app uninstall flag is enabled.

Break-glass administration is disabled by default:

- `adb_execute_arbitrary_command` accepts a bounded argument array only after `Policy:ArbitraryCommandsEnabled`, the selected device's `AllowArbitraryCommands`, and per-call `confirmHighImpact` are all true. The configured device selector is always inserted by the server; selector/global options and known ADB server-management commands are rejected.

Controls are disabled by default and independently gated:

- `adb_wake_device` / `adb_sleep_device`
- `adb_send_navigation` with a closed action enum and server-side action allowlist
- `adb_launch_app` with a server-side application allowlist
- `adb_stop_app` behind its own higher-impact gate

Outcomes distinguish observed completion, acceptance without observation, failure, timeout, offline, unauthorized, indeterminate, denied, and unknown aliases.

Application launch accepts a closed `Start`, `Foreground`, or `WakeAndForeground` mode. `Start` does not request or verify foreground state. Foreground modes explicitly dismiss Android's dream/screensaver overlay and verify both the focused package and inactive dream state; `WakeAndForeground` also requires the independent power-control gate. Launch first uses Android's standard launcher category and makes one bounded Android TV Leanback retry when foreground is not observed. Callers cannot supply a category, activity, component, or intent.

Launch verification polling is bounded by `Adb:AppLaunchVerificationAttempts` and `Adb:VerificationDelayMilliseconds`.

## Requirements

- .NET SDK 10.0.300 or later 10.0 feature band for building
- An operator-installed `adb` executable from a trusted Android SDK Platform-Tools distribution
- An already-authorized ADB connection; wireless pairing remains an operator workflow

ADBMCPSharp does not explicitly start or manage a daemon, pair devices, manage ADB keys, or redistribute platform-tools. It can issue guarded connect/disconnect operations for preconfigured device selectors. The configured executable may cause the normal `adb` client to start its local server when needed, connect to a local ADB server, or use `adb -H host -P port` for a trusted remote ADB server.

## Configure

Keep checked-in [`ADBMCPSharp.json`](src/ADBMCPSharp/ADBMCPSharp.json) unchanged and put site-specific configuration in an ignored `src/ADBMCPSharp/ADBMCPSharp.Local.json` file. For example, with neutral placeholders:

```json
{
  "Adb": {
    "ExecutablePath": "adb",
    "MaxDiscoveryResults": 25,
    "DiscoveryHandleLifetimeSeconds": 60,
    "MaxInstalledAppResults": 200,
    "AppLaunchVerificationAttempts": 6,
    "ArbitraryCommandTimeoutSeconds": 30,
    "ConnectionOperationTimeoutSeconds": 15,
    "ConnectionVerificationAttempts": 4,
    "ConnectionRetryDelayMilliseconds": 500,
    "MaxArbitraryArgumentCount": 32,
    "MaxArbitraryArgumentLength": 1024,
    "MaxArbitraryTotalCharacters": 8192,
    "Servers": {
      "local": { "Mode": "Local", "Port": 5037 },
      "trusted-remote": { "Mode": "Remote", "Host": "adb-server.example.invalid", "Port": 5037 }
    },
    "Devices": {
      "living-room": {
        "Server": "local",
        "Selector": "operator-configured-selector",
        "DisplayName": "Living room device",
        "Capabilities": {
          "AllowInstalledAppListing": true,
          "AllowDiagnostics": true,
          "AllowMediaInspection": true,
          "AllowMediaMetadata": true,
          "AllowMediaControl": true,
          "AllowVolumeControl": true,
          "AllowPackageInstall": false,
          "AllowPackageUninstall": false,
          "AllowArbitraryCommands": false,
          "AllowConnectionManagement": false
        },
        "AllowedApps": {
          "player": { "Package": "org.example.player", "DisplayName": "Player", "AllowUninstall": false }
        }
      }
    },
    "ApkArtifacts": {
      "player-release": {
        "Package": "org.example.player",
        "DisplayName": "Player release",
        "Source": "C:\\protected-artifacts\\player.apk",
        "Sha256": "0000000000000000000000000000000000000000000000000000000000000000",
        "AllowReplace": true,
        "AllowedDevices": ["living-room"]
      }
    }
  },
  "Policy": {
    "InspectionEnabled": true,
    "DiscoveryEnabled": false,
    "InstalledAppListingEnabled": false,
    "DiagnosticsEnabled": false,
    "MediaInspectionEnabled": false,
    "MediaMetadataEnabled": false,
    "MediaControlEnabled": false,
    "VolumeControlEnabled": false,
    "PackageInstallEnabled": false,
    "PackageUninstallEnabled": false,
    "ArbitraryCommandsEnabled": false,
    "ConnectionManagementEnabled": false,
    "PowerControlEnabled": false,
    "NavigationControlEnabled": false,
    "AppLaunchEnabled": false,
    "AppStopEnabled": false,
    "AllowedNavigationActions": ["Home", "Back"],
    "AllowedMediaActions": ["Play", "Pause", "PlayPause"],
    "AllowedVolumeActions": ["Up", "Down", "Mute"],
    "AllowedDiagnostics": ["Battery", "Memory", "Storage", "CpuLoad", "Runtime", "Display", "Security"]
  }
}
```

Configuration order is JSON, environment-specific JSON, ignored local JSON, environment variables, `ADBMCP_`-prefixed environment variables, then command-line arguments. Nested environment settings use double underscores, such as `ADBMCP_Policy__PowerControlEnabled=true`.

Set `Policy:DiscoveryEnabled` to `true` to allow passive discovery. The tool uses only `adb mdns check` and `adb mdns services`; it does not sweep subnets, probe port 5555, or issue pairing or connection commands. Results classify legacy TCP ADB, wireless debugging, and pairing-window advertisements while replacing service-instance names and endpoints with short-lived opaque handles. One physical device may produce multiple advertisements. With a remote ADB server alias, discovery observes that server host's LAN rather than the MCP service host's LAN.

The ADB server can independently auto-connect wireless devices that it has already paired with. For a remote server, govern that behavior on the remote host through its ADB configuration, including `ADB_MDNS_AUTO_CONNECT`; ADBMCPSharp does not change remote-server policy.

Connection health requires only normal inspection access. To permit explicit lifecycle changes, set `Policy:ConnectionManagementEnabled` and the selected device's `Capabilities:AllowConnectionManagement` to `true`; each call must still pass `confirmChange: true`. Connect, reconnect, and disconnect always use the configured selector and configured local or remote ADB server—MCP callers cannot provide an endpoint. `Adb:ConnectionOperationTimeoutSeconds`, `ConnectionVerificationAttempts`, and `ConnectionRetryDelayMilliseconds` bound command execution and postcondition polling. Pairing, trust prompts, keys, discovery-handle connection, and background reconnect loops remain outside this feature.

Set `Policy:InstalledAppListingEnabled` to `true` to expose installed package identifiers. Inspection must also be enabled, and the selected device's `Capabilities:AllowInstalledAppListing` override must remain enabled. Results are capped by `Adb:MaxInstalledAppResults`; reaching the cap is reported as truncation. No APK paths, versions, permissions, installers, or application data are returned.

Set `Policy:DiagnosticsEnabled` to `true`, keep the selected device's `Capabilities:AllowDiagnostics` enabled, and place each desired option in `Policy:AllowedDiagnostics`. Battery reports charge/power/health values; Memory reports selected `/proc/meminfo` totals; Storage reports aggregate `/data` capacity; CpuLoad reports kernel load averages; Runtime reports uptime and aggregate idle time; Display reports physical/override size and density; Security reports only bounded build/debug/verified-boot/flash-lock/SELinux fields. Diagnostic commands and parsers are fixed internally, and raw output is discarded.

Media status, media metadata, media actions, and volume actions each require their global policy gate plus the device capability override. Media and volume actions must also appear in their closed global allowlists. Only media sessions belonging to a configured allowed-app package are returned; metadata has its own privacy gate.

APK sources are operator configuration, never tool input. A source may be an absolute local `.apk` path or a credential-free HTTPS URL without a query or fragment. Every artifact requires a pinned SHA-256 digest (replace the all-zero example) and at least one allowed device alias. Downloads and local files are size-bounded, network downloads are time-bounded, HTTPS redirects are rejected, and temporary downloads are deleted after the operation. Put authenticated artifacts behind a separately managed local file or credential-free protected endpoint; credentials are intentionally not accepted in artifact URLs. Installation and uninstallation each require their global gate, device gate, allowlist entry, and a `true` confirmation argument. Uninstallation additionally requires `AllowUninstall` on the allowed app.

`adb_execute_arbitrary_command` is an intentional break-glass exception to the normal security model. Enabling it grants the MCP client the effective privileges of ADB on that device, including arbitrary device shell execution; commands such as `push` and `pull` may also access files visible to the service account. Returned output is raw, may contain secrets or personal data, and is capped by the transport's 65,536-character capture limit. Arguments are passed without a host shell, but a requested Android `shell` operation is interpreted by the device shell. Keep the global gate off unless actively needed, enable it only for selected devices, require authenticated private-network MCP access, and disable it again after use. Argument contents and output are intentionally omitted from audit logs.

The service binds to `localhost:21990` and serves MCP at `/mcp`. A non-loopback binding is rejected unless `Server:ApiKey` has at least 24 characters. Supply that secret through protected runtime configuration, preferably `ADBMCP_Server__ApiKey`; clients can use `Authorization: Bearer ...` or `X-ADBMCP-Key`. MCP requests are limited to 120 per minute per client address by default. `/healthz` contains no device data and remains unauthenticated for service monitoring.

## Build, test, and run

```powershell
.\scripts\build.ps1 -Action Test
dotnet run --project .\src\ADBMCPSharp\ADBMCPSharp.csproj
```

The VS Code build, test, and `coreclr` launch configurations are repository-local. Logs are written beneath the executable/content root and retained for a bounded number of days. Device selectors and raw ADB output are not logged.

After publishing the Windows executable, `.\scripts\smoke-test.ps1` starts it hidden, checks `/healthz`, performs an MCP initialization request, and stops the exact process it started.

For a configured ignored local device alias, `.\scripts\device-acceptance.ps1 -DeviceAlias <alias>` runs the structured read-only MCP acceptance suite without printing raw diagnostic or package data. Add `-IncludeConnectionLifecycle` only during an authorized maintenance window; it exercises connect, reconnect, disconnect, restores the connection, and fails unless final health is online and authorized. Add `-IncludeControls -ControlAppAlias <allowlisted-alias>` only when reversible wake, Home, launch/stop, media Pause/Play, and volume Down/Up tests are authorized. Add `-IncludePackageAdministration -ArtifactAlias <disposable-alias>` only for a checksum-pinned disposable APK whose install and removal are both authorized. The harness starts and stops an exact local service process and passes sensitive local configuration only through its temporary child-process environment.

The same harness can target an existing authenticated service—such as a container—using `-SkipProcessStart -BaseUri <uri> -ApiKey <key>`. Local configuration remains operator-side reference data for aliases and expected controls; it is not sent through MCP.

During an authorized maintenance window on Windows, `.\scripts\remote-adb-acceptance.ps1 -DeviceAlias <alias>` starts an isolated loopback-only secondary ADB server, connects the configured device without printing its selector, and runs the full read-only and connection-lifecycle suite through `AdbServerMode.Remote`. It verifies that the secondary listener is restricted to loopback, leaves the primary ADB server untouched, restores the secondary connection before completion, stops the exact secondary server, and deletes its temporary output files. This validates the remote ADB protocol path without claiming cross-host network acceptance.

## Publish and host

Create a self-contained single-file build (normal .NET runtime, not NativeAOT):

```powershell
.\scripts\build.ps1 -Action Publish -Runtime win-x64
.\scripts\build.ps1 -Action Publish -Runtime linux-x64
```

Create versioned release archives and a SHA-256 manifest with:

```powershell
.\scripts\package-release.ps1
```

The default creates `win-x64` and `linux-x64` packages beneath `artifacts/release`. Linux packaging on Windows uses WSL so the executable and service files have deterministic Unix ownership and modes. See [`RELEASING.md`](RELEASING.md) for the candidate checklist and outstanding first-release gates, and [`CHANGELOG.md`](CHANGELOG.md) for release notes.

Windows builds support interactive execution and Windows Service hosting. Install a published executable with your normal service-management tooling, set its working/configuration directory permissions, and supply secrets in a protected service environment rather than command-line arguments. From an elevated Windows PowerShell 5.1 session in a repository checkout, `scripts\windows-service-acceptance.ps1` installs an isolated temporary service, stores an ephemeral API key in the service environment, verifies authenticated MCP startup, restart, failure recovery, and shutdown, then deletes the service and its temporary files. It refuses to replace an existing service or use an occupied port.

Linux builds run interactively or with the example [`adbmcp.service`](deploy/systemd/adbmcp.service). The unit assumes `/opt/adbmcp`, a dedicated `adbmcp` account, and `/etc/adbmcp/environment`; adjust ownership and paths for the target host. From a repository checkout with a clean systemd-enabled WSL2 distribution, `scripts\systemd-wsl-acceptance.ps1 -Distribution <name>` installs the packaged Linux artifact, verifies unit hardening plus authenticated startup, restart, failure recovery, shutdown, health, and the MCP catalogue, then removes only the account, unit, and directories it created. It refuses to overwrite an existing installation.

Build and smoke-test the Linux container from a Docker-capable host:

```powershell
.\scripts\docker-smoke-test.ps1
.\scripts\docker-remote-topology-test.ps1 -SkipBuild
```

The image runs as the non-root .NET `app` account, binds container port `8080`, installs Ubuntu Noble's packaged `adb`, and requires an API key whenever exposed beyond loopback. The basic smoke harness creates an ephemeral API key and uniquely named container, verifies health plus the complete MCP tool catalogue over a loopback-only published port, and removes the container. The remote-topology harness adds a non-root ADB-server sidecar, private network, configured remote-mode device profile, persistent ADB-key volume, server replacement, and an unavailable-device health probe without requiring a physical device. Both are configured in CI. Site-specific configuration must be supplied at runtime; `.dockerignore` prevents local configuration and build artifacts from entering the build context. Persist `/var/lib/adbmcp/adb` when container-local ADB trust is required and `/app/logs` when file logs must survive replacement.

NativeAOT is intentionally disabled because MCP assembly tool discovery uses reflection. The image and physical two-container remote ADB topology have passed under rootless Podman, and both smoke harnesses pass on Docker Engine in public GitHub Actions.

## Security model

- Normal tool contracts accept aliases and enums only; the explicitly marked break-glass tool accepts bounded ADB arguments.
- Device selectors, ADB server coordinates, and package names stay in server-side configuration.
- `ProcessStartInfo.ArgumentList` passes fixed command tokens without a shell.
- Output capture is bounded and only explicitly parsed facts are returned.
- Passive discovery is separately gated and redacts mDNS instance names, device addresses, ports, and serial-derived identifiers.
- Installed package enumeration is separately gated, bounded, and returns validated package identifiers only.
- Curated diagnostics use a global gate, per-device gate, and option allowlist; only structured selected fields are returned.
- Each device has a one-operation-at-a-time lock.
- Write requests are category-gated, device-gated, audited without raw metadata, and verified where observable.
- APK sources and package identifiers remain server-side; installs are checksum-pinned and package changes require per-call confirmation.
- Arbitrary ADB is triple-gated globally, per device, and per call; it is pinned to the configured device selector and omits arguments/output from audit logs, but it intentionally forfeits semantic command safety.
- Non-loopback MCP requires an API key; use network-layer TLS because this service does not terminate TLS itself.

See [`PLAN.md`](PLAN.md) for verification gaps and follow-up milestones, [`RELEASING.md`](RELEASING.md) for the release checklist, and [`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md) for dependency provenance.

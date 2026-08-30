# ADBMCPSharp

ADBMCPSharp is a .NET 10 MCP server for controlled Android Debug Bridge access. It maps configured aliases to local or remote ADB-server devices and exposes bounded inspection and explicitly gated controls over MCP Streamable HTTP. It never exposes a raw shell, arbitrary intents, arbitrary key codes, device selectors, network addresses, or filesystem paths to MCP clients.

The current implementation is a runnable first vertical slice. It has fake-transport coverage but has not yet been accepted against a physical device, remote ADB server, Docker topology, or public release.

## Included MCP tools

Read-only inspection is enabled by default:

- `adb_list_devices`
- `adb_get_device_status`
- `adb_list_allowed_apps`
- `adb_get_app_status`
- `adb_get_capabilities`

Controls are disabled by default and independently gated:

- `adb_wake_device` / `adb_sleep_device`
- `adb_send_navigation` with a closed action enum and server-side action allowlist
- `adb_launch_app` with a server-side application allowlist
- `adb_stop_app` behind its own higher-impact gate

Outcomes distinguish observed completion, acceptance without observation, failure, timeout, offline, unauthorized, indeterminate, denied, and unknown aliases.

## Requirements

- .NET SDK 10.0.300 or later 10.0 feature band for building
- An operator-installed `adb` executable from a trusted Android SDK Platform-Tools distribution
- An already-authorized ADB connection; wireless pairing remains an operator workflow

ADBMCPSharp does not start a daemon, pair devices, manage ADB keys, or redistribute platform-tools. The configured executable may connect to a local ADB server or use `adb -H host -P port` for a trusted remote ADB server.

## Configure

Keep checked-in [`ADBMCPSharp.json`](src/ADBMCPSharp/ADBMCPSharp.json) unchanged and put site-specific configuration in an ignored `src/ADBMCPSharp/ADBMCPSharp.Local.json` file. For example, with neutral placeholders:

```json
{
  "Adb": {
    "ExecutablePath": "adb",
    "Servers": {
      "local": { "Mode": "Local", "Port": 5037 },
      "trusted-remote": { "Mode": "Remote", "Host": "adb-server.example.invalid", "Port": 5037 }
    },
    "Devices": {
      "living-room": {
        "Server": "local",
        "Selector": "operator-configured-selector",
        "DisplayName": "Living room device",
        "AllowedApps": {
          "player": { "Package": "org.example.player", "DisplayName": "Player" }
        }
      }
    }
  },
  "Policy": {
    "InspectionEnabled": true,
    "PowerControlEnabled": false,
    "NavigationControlEnabled": false,
    "AppLaunchEnabled": false,
    "AppStopEnabled": false,
    "AllowedNavigationActions": ["Home", "Back"]
  }
}
```

Configuration order is JSON, environment-specific JSON, ignored local JSON, environment variables, `ADBMCP_`-prefixed environment variables, then command-line arguments. Nested environment settings use double underscores, such as `ADBMCP_Policy__PowerControlEnabled=true`.

The service binds to `localhost:21990` and serves MCP at `/mcp`. A non-loopback binding is rejected unless `Server:ApiKey` has at least 24 characters. Supply that secret through protected runtime configuration, preferably `ADBMCP_Server__ApiKey`; clients can use `Authorization: Bearer ...` or `X-ADBMCP-Key`. MCP requests are limited to 120 per minute per client address by default. `/healthz` contains no device data and remains unauthenticated for service monitoring.

## Build, test, and run

```powershell
.\scripts\build.ps1 -Action Test
dotnet run --project .\src\ADBMCPSharp\ADBMCPSharp.csproj
```

The VS Code build, test, and `coreclr` launch configurations are repository-local. Logs are written beneath the executable/content root and retained for a bounded number of days. Device selectors and raw ADB output are not logged.

After publishing the Windows executable, `.\scripts\smoke-test.ps1` starts it hidden, checks `/healthz`, performs an MCP initialization request, and stops the exact process it started.

## Publish and host

Create a self-contained single-file build (normal .NET runtime, not NativeAOT):

```powershell
.\scripts\build.ps1 -Action Publish -Runtime win-x64
.\scripts\build.ps1 -Action Publish -Runtime linux-x64
```

Windows builds support interactive execution and Windows Service hosting. Install a published executable with your normal service-management tooling, set its working/configuration directory permissions, and supply secrets in a protected service environment rather than command-line arguments.

Linux builds run interactively or with the example [`adbmcp.service`](deploy/systemd/adbmcp.service). The unit assumes `/opt/adbmcp`, a dedicated `adbmcp` account, and `/etc/adbmcp/environment`; adjust ownership and paths for the target host.

NativeAOT is intentionally disabled because MCP assembly tool discovery uses reflection. Docker packaging is intentionally deferred until remote-server networking, local server/device access, and ADB-key persistence are tested and documented. No support is claimed for either yet.

## Security model

- Tool contracts accept aliases and enums only.
- Device selectors, ADB server coordinates, and package names stay in server-side configuration.
- `ProcessStartInfo.ArgumentList` passes fixed command tokens without a shell.
- Output capture is bounded and only explicitly parsed facts are returned.
- Each device has a one-operation-at-a-time lock.
- Write requests are category-gated, device-gated, audited without raw metadata, and verified where observable.
- Non-loopback MCP requires an API key; use network-layer TLS because this service does not terminate TLS itself.

See [`PLAN.md`](PLAN.md) for verification gaps and follow-up milestones, and [`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md) for dependency provenance.

# Changelog

All notable changes will be recorded here. The project follows Semantic Versioning once a public release is tagged.

## Unreleased

## 0.1.0 - 2026-08-31

- Added a controlled .NET 10 MCP service for local and remote ADB servers.
- Added bounded device inspection, passive discovery, curated diagnostics, installed-app inventory, media information, and explicit feature gates.
- Added guarded wake, navigation, application launch/foreground/stop, media, volume, connection lifecycle, APK installation, and package removal operations.
- Added a separately gated break-glass operation for bounded arbitrary ADB argument arrays on specifically enabled devices.
- Added Windows Service, systemd, and OCI-container deployment assets plus local, physical-device, and container topology acceptance harnesses.
- Added self-contained Windows and Linux release archives with SHA-256 manifests.
- Upgraded to MCP SDK 2.2 with hybrid legacy-stateful and 2026-07-28 stateless Streamable HTTP handling.

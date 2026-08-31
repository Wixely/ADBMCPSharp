# Release process

Publishing requires an explicit user instruction and a configured `Wixely/ADBMCPSharp` GitHub remote. Version `0.1.0` is the first public release.

## Prepare a candidate

1. Update `VersionPrefix` in `Directory.Build.props` and move the relevant entries in `CHANGELOG.md` into a dated version section.
2. Run `dotnet format ADBMCPSharp.slnx --verify-no-changes`, `dotnet build ADBMCPSharp.slnx --configuration Release`, and `dotnet test ADBMCPSharp.slnx --configuration Release` on Windows and Linux.
3. Run `dotnet list ADBMCPSharp.slnx package --vulnerable --include-transitive` and `dotnet list ADBMCPSharp.slnx package --deprecated --include-transitive` against the intended NuGet sources.
4. Run both Docker smoke harnesses with Docker Engine. Run physical-device acceptance only during an authorized maintenance window and never capture device output in release artifacts.
5. Build the native archive on each target operating system when possible:

   ```powershell
   .\scripts\package-release.ps1 -Runtime win-x64
   .\scripts\package-release.ps1 -Runtime linux-x64
   ```

   Windows can create the Linux archive through WSL. The script normalizes timestamps and Linux ownership/modes, verifies required files, and rewrites `artifacts/release/SHA256SUMS.txt` for all archives present.

6. Extract each archive, verify it against `SHA256SUMS.txt`, and run `scripts/smoke-test.ps1` against the packaged Windows executable. On Linux, verify `ADBMCPSharp` is executable and smoke-test the service on a native host.

## Review and publish

1. Review the complete outgoing commit range, metadata, filenames, text, binaries, archives, and embedded metadata using the repository privacy checklist in `AGENTS.md`. Do not publish local configuration, device details, addresses, ADB keys, logs, or build directories.
2. Confirm the repository-local author and committer identity is `Wixely <5593644+Wixely@users.noreply.github.com>` and repair only unpublished user-created commits if needed.
3. Create an annotated `v<version>` tag, re-check that the tag points to the reviewed commit, and publish only after explicit authorization.
4. Attach both archives and `SHA256SUMS.txt` to the release. Verify downloaded assets again before announcing the release.

## Post-release acceptance follow-ups

- Exercise `scripts\windows-service-acceptance.ps1` from an elevated session on a representative Windows host.
- Repeat the accepted Ubuntu 24.04 WSL2 systemd lifecycle on a representative non-WSL Linux host.

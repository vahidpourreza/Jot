# Offline Windows installer

## Release commands

From the repository root, using PowerShell 7:

```powershell
.\scripts\release.ps1
```

The output is `dist\installer\Jot-Setup-<version>-win-x64.exe`, accompanied by a SHA-256 checksum and `release-info.json`. The verified v1.5.0 installer is approximately 334 MiB. Send the installer EXE to users, not a development publish folder. The script stops if bundled-runtime checks fail; requested full-suite and installer checks finish before the distributable is compiled.

The developer machine needs the .NET 10 SDK and Inno Setup 6. These are build tools only, not end-user prerequisites. The compiler is detected in the normal install directory or can be supplied with `-InnoCompiler <ISCC.exe path>`. No compiler or other build tool is installed globally by the script.

Options:

```powershell
# Full offscreen application suite plus isolated install/repair/uninstall checks:
.\scripts\release.ps1 -Verify -TestInstaller

# Use another numeric app version without editing Jot.csproj:
.\scripts\release.ps1 -Version 1.5.1

# Reuse already-restored win-x64 package assets when dependencies did not change:
.\scripts\release.ps1 -NoRestore

# Supply the official runtime CAB if Microsoft's CDN is unavailable:
.\scripts\release.ps1 -RuntimeCab 'C:\Downloads\Microsoft.WebView2.FixedVersionRuntime.154.0.4258.37.x64.cab'
```

Normal releases perform a locked NuGet restore. The project declares `win-x64` in `RuntimeIdentifiers`, so a regular restore does not remove the release target from the lock file. `-NoRestore` requires existing matching win-x64 assets; it does not repair or regenerate missing assets. The WebView2 download and extracted runtime are cached under `artifacts/cache`; CAB and extracted-file hashes are checked before reuse. A damaged or incomplete staged runtime is replaced from the verified cache. The app is republished, smoke-tested, and the installer recompiled each time. A file lock prevents simultaneous release builds from modifying the same staging area. Cleanup rejects paths outside the build area and linked ancestors/children.

Measured on this developer machine: the first fully verified release run took 412.5 seconds (including extraction, full app suite and installer verification); a cached `-NoRestore` release with smoke verification took 111.6 seconds. These are observations, not guaranteed timings. The cached run did not download runtimes or require the VPN used for the first Microsoft download.

## Runtime acquisition and pinning

`packaging/webview2-runtime.json` identifies Microsoft's official x64 Fixed Version CAB. On the first successful acquisition, the script checks the extracted Microsoft executable's Authenticode signature and version, records the CAB SHA-256 in that manifest, and preserves the cached download. Keep the populated manifest in Git so subsequent machines enforce the same bytes. Future runtime upgrades require intentionally updating its version, URL, and checksum (or bootstrapping a newly selected version), then repeating the full validation.

Do not disable TLS/certificate verification to obtain a runtime. `-RuntimeCab` is an inbound-file alternative for a CAB downloaded directly from Microsoft's WebView2 page. A nonempty pinned checksum is enforced before extraction. First acquisition still requires a valid Microsoft executable signature and matching version.

The initial download blockage was resolved using the user's connection on 2026-09-27. The official runtime was acquired, its Microsoft signature/version verified, and CAB SHA-256 pinned as `143DA7F7C4939FDDD3875ED918E44022D7EB87063BF912FE3E32DF37C6B0B8C3`. The release passed 384 app checks, 8 private-runtime smoke checks, and 9 isolated installer checks (including another installed-app smoke run). Evidence is in `artifacts/verification/1.5.0-20260927-084729-44dd50`. A cached repeat build passed smoke checks again under `artifacts/verification/1.5.0-20260927-085823-897b98` and includes the collected third-party notices. The exact latest installer hash and timing are in `dist/installer/release-info.json`.

## End-user behavior

- Traditional per-user setup under `%LOCALAPPDATA%\Programs\Jot`, with Start menu access and an optional desktop shortcut. No administrator elevation is requested.
- Self-contained .NET 10/WPF, SQLite native binaries, fonts/icons, and a private Fixed Version WebView2 runtime are inside the installer. No prerequisite download, NuGet, SDK, database server or separate .NET/WebView2 installation is required by the intended package.
- Windows x64-compatible systems, Windows 10 build 19041 or later. This technical minimum is not a promise of Microsoft support for every Windows edition; test on the Windows versions you distribute to. Development is on Windows 10 22H2 x64.
- AppMutex prevents replacing or uninstalling a running Jot. Setup never force-closes it and does not restart it automatically. The optional finish-page Launch action is unchecked and skipped during silent tests.
- Windows 10 Fixed WebView2 app-container read/execute permissions are applied only to the bundled runtime directory, using Microsoft's documented `icacls` commands. User notes are not granted additional permissions.
- Upgrades preserve `%LOCALAPPDATA%\Jot`, including SQLite notes, backups, old JSON, trash and logs. Uninstall removes owned app files/shortcuts, not the data folder. There is no recursive user-data deletion rule.
- The offline build fails clearly if its private runtime or package manifest is missing/mismatched; it does not silently depend on an installed browser. Ordinary developer builds may still use Evergreen WebView2.
- Package-supplied licenses/notices, NuGet metadata, Inno Setup's license, and the relevant SQLite provider license texts are included under `licenses`. WebView2's original embedded credits and supplied license files are retained. Existing font licensing still applies.

The fixed runtime is not auto-updated by Microsoft's Evergreen updater. Release updated installer builds when .NET, SQLite or WebView2 security updates are needed. There is no custom background updater in this change.

## Verification and boundaries

Every successful release command runs `--package-smoke` in an invisible, non-activating, offscreen process using isolated data. It intentionally points inherited .NET/WebView2 environment overrides at a missing directory, then verifies that .NET/WPF assemblies and the actual WebView2 browser process came from the app folder. It also saves and reloads a bilingual SQLite note. The app handles WebView2 selection by setting its own process-only runtime path, not machine settings.

`-Verify` runs the existing full suite against the staged private-runtime build. `-TestInstaller` compiles a separate validation installer with a distinct AppId, no uninstall-registry registration, no icons, no auto-launch and its own directory under `artifacts/verification`. It tests extraction, runtime smoke, repair by reinstalling, and uninstall while retaining the isolated data. It does not install/uninstall the user's actual Jot or alter the system WebView2 installation.

These host-isolated checks do not replace testing on a clean Windows VM. Do not claim a clean-machine test unless it was actually performed.

The setup is unsigned unless a signing workflow is added with the owner's code-signing certificate. Windows may therefore show an unknown-publisher/SmartScreen warning. No certificate has been purchased or used, and Windows security protections are not disabled.

## References

- [Microsoft: Fixed WebView2 distribution, included files, and Windows 10 permissions](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/distribution)
- [Microsoft WebView2 runtime downloads](https://developer.microsoft.com/en-us/microsoft-edge/webview2/)
- [Inno Setup AppMutex](https://jrsoftware.org/ishelp/topic_setup_appmutex.htm)
- [Inno Setup non-administrative install mode](https://jrsoftware.org/ishelp/topic_setup_privilegesrequired.htm)

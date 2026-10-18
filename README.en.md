# Cafe Launcher

[简体中文](./README.md) · [English](./README.en.md)

A community-maintained, third-party desktop launcher for Blue Archive JP. Built with .NET 10 and Avalonia, it brings installation, updates, repair, launch, and uninstall into one window and works with game directories downloaded by the official launcher.

[![Stars](https://img.shields.io/github/stars/bluearchive-cafe/Cafe.Launcher.Avalonia?style=flat-square&label=Stars)](https://github.com/bluearchive-cafe/Cafe.Launcher.Avalonia)
[![Stable release](https://img.shields.io/github/v/release/bluearchive-cafe/Cafe.Launcher.Avalonia?style=flat-square&color=%233fb950&label=Stable)](https://github.com/bluearchive-cafe/Cafe.Launcher.Avalonia/releases/latest)
[![Prerelease](https://img.shields.io/github/v/release/bluearchive-cafe/Cafe.Launcher.Avalonia?include_prereleases&style=flat-square&label=Prerelease)](https://github.com/bluearchive-cafe/Cafe.Launcher.Avalonia/releases)
[![Downloads](https://img.shields.io/github/downloads/bluearchive-cafe/Cafe.Launcher.Avalonia/total?style=flat-square&label=Downloads&logo=github)](https://github.com/bluearchive-cafe/Cafe.Launcher.Avalonia/releases/latest)<br/>
[![Build](https://img.shields.io/github/actions/workflow/status/bluearchive-cafe/Cafe.Launcher.Avalonia/build.yml?style=flat-square&label=Build)](https://github.com/bluearchive-cafe/Cafe.Launcher.Avalonia/actions/workflows/build.yml)
![.NET version](https://img.shields.io/badge/.NET-10-512bd4?style=flat-square)
![Repository size](https://img.shields.io/github/repo-size/bluearchive-cafe/Cafe.Launcher.Avalonia?style=flat-square&color=3cb371)
[![Primary language](https://img.shields.io/github/languages/top/bluearchive-cafe/Cafe.Launcher.Avalonia?style=flat-square)](https://github.com/bluearchive-cafe/Cafe.Launcher.Avalonia/search?l=c%23)
[![License](https://img.shields.io/github/license/bluearchive-cafe/Cafe.Launcher.Avalonia?style=flat-square)](./LICENSE)
[![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20macOS%20%7C%20Linux-2E7DF6?style=flat-square)](#platform-support-and-packages)

[Download](https://github.com/bluearchive-cafe/Cafe.Launcher.Avalonia/releases) · [Installation and First Run](https://docs.bluearchive.cafe/en/cafe-launcher/installation) · [FAQ](https://docs.bluearchive.cafe/en/cafe-launcher/faq) · [Feedback Guide](https://docs.bluearchive.cafe/en/cafe-launcher/feedback) · [Developer Guide](#developer-guide)

> [!IMPORTANT]
> Cafe Launcher is a community project with no affiliation or partnership with Nexon, Nexon Games, Yostar, or the official Blue Archive team. Read the [privacy policy (Chinese)](./PRIVACY.md) before use.

![Cafe Launcher key visual](./docs/assets/key-visual.png)

## Contents

- [Features](#features)
- [Platform support and packages](#platform-support-and-packages)
- [Quick start](#quick-start)
- [Comparison with the official launcher](#comparison-with-the-official-launcher)
- [Linux support (experimental)](#linux-support-experimental)
- [Wallpaper sources](#wallpaper-sources)
- [Help and feedback](#help-and-feedback)
- [Developer guide](#developer-guide)
- [Local data and licensing](#local-data-and-licensing)
- [Acknowledgments](#acknowledgments)

## Features

Cafe Launcher can take over an existing official installation or download and manage the game from scratch. It offers resumable downloads, multiple sources, configurable launch verification, a multilingual interface, appearance customization, and local diagnostics.

- **Game management:** install, incrementally update, repair, launch, and uninstall. Manifests and configuration files remain compatible with the official launcher.
- **Download controls:** up to 10 concurrent transfers, resume, pause and continue, speed limits, and CRC64 verification.
- **Launch verification:** choose a local manifest, a remote manifest, or skip verification, following the official launcher's behavior.
- **Desktop experience:** English, Simplified Chinese, Traditional Chinese, and Japanese; system, light, and dark themes; custom backgrounds and accent colors.
- **Localization resources and diagnostics:** manage Chinese localization resources provided by the Cafe source using a UID; view and export logs for troubleshooting.
- **Launcher updates:** Windows supports in-app updates verified against the release's `SHA256SUMS`. macOS and Linux open the release page.

Settings, download state, and logs stay on your device. The launcher does not modify the game process or inject code into it. The launcher's interface language is independent of the game's resource language; the Cafe source provides Chinese localization.

## Platform support and packages

| Platform | Status | Packages |
| --- | --- | --- |
| Windows 10 1809+ / Windows 11, x64 | Officially supported | Installer (recommended), portable ZIP |
| macOS, Apple Silicon | Experimental; cannot launch the game | `.app` archive |
| Linux, x64 | Experimental | `.deb`, `.rpm`, AppImage, `.tar.gz` |

Packages include the .NET Runtime, so no separate installation is needed.

The macOS launcher can install, update, and repair game files but does not provide the compatibility layer needed to launch the Windows game. Linux can launch through Wine, UMU, or Proton; see [Linux support](#linux-support-experimental).

Prereleases are testing builds. Choose the latest stable release for everyday use. macOS and Linux have not received the same level of adaptation and testing as Windows; read the release notes for your version.

## Quick start

1. Open [Releases](https://github.com/bluearchive-cafe/Cafe.Launcher.Avalonia/releases) and download the package for your platform. Windows users should normally choose the installer, or portable ZIP if installation is not desired.
2. Open Cafe Launcher and use the first-run wizard to choose the interface language, download source, game directory, and proxy mode.
3. Select an existing `BlueArchive_JP` directory, or choose an installation location and install from the main window.
4. Update or start the game from the main window; repair and uninstall are also available in Settings.

See [Installation and First Run](https://docs.bluearchive.cafe/en/cafe-launcher/installation) for the complete walkthrough. The documentation also covers [settings](https://docs.bluearchive.cafe/en/cafe-launcher/settings), [game operations](https://docs.bluearchive.cafe/en/cafe-launcher/operations), the [Resource Panel](https://docs.bluearchive.cafe/en/cafe-launcher/resource-panel), and [uninstall and data](https://docs.bluearchive.cafe/en/cafe-launcher/uninstall).

## Comparison with the official launcher

Cafe Launcher shares the official launcher's **game directory and manifest format**. Installation and verification protocols stay compatible, so switching launchers does not require downloading the game again. This comparison is based on [static research of official launcher v1.7.2 (Chinese)](./docs/research/official-launcher-v1.7.2-comparison.md), a historical version; current official behavior may differ.

| Area | Official launcher v1.7.2 | Cafe Launcher |
| --- | --- | --- |
| Game running during installation | Uses `taskkill /F /T` on matching processes in the game directory before replacing files | Refuses writes with an explanation; checks again before writing and recognizes related anti-cheat processes |
| Download controls | Stores tasks in browser `localStorage` and re-enters them after restart; no session-level pause | Pause/continue, speed limits, exit recovery, and distinct user-stop and process-exit behavior |
| Uninstall scope | Manifest files and installation state | Entire game directory, state, and desktop shortcut; optional managed compatibility cleanup; preserves custom Prefixes outside the deletion targets |
| Localization resources | No corresponding feature identified | UID-based management of the Cafe source's Chinese localization resources |
| Diagnostics and privacy | Local logs, remotely controlled cloud log reporting, and AIHelp support | Local log viewing/export and crash reports; no remote telemetry |
| Interface languages | Japanese in the JP package | English / 简体中文 / 繁體中文 / 日本語 |
| Platforms | Windows | Windows supported; macOS and Linux experimental |

Official features not implemented here include `clickCode` file migration, temporarily moving/restoring game directories during self-update, and AIHelp support. Read the comparison's limitations before treating Cafe Launcher as an in-place replacement for the official installer.

## Linux support (experimental)

Linux can install, update, repair, and **launch** the game through a compatibility runtime. The full launch chain, including XIGNCODE3, has been verified on the single combination listed below.

### Configure a compatibility runtime

Use Settings → Game → Compatibility Runtime:

| Setting | Description |
| --- | --- |
| Runtime | Auto prefers UMU; UMU / Proton and Wine can also be selected explicitly. Choose a compatibility runtime rather than Windows (Native) |
| Runner path | Leave blank to detect `umu-run` / `wine`, or select an executable |
| Proton path | Custom Proton build for UMU; leave blank for UMU's selection |
| Prefix path | Leave blank for separate launcher-managed prefixes per runner, or select your own |

### Additional Linux behavior

- **Preflight checks:** detect unwritable prefixes, `noexec` mounts, and filesystems without symbolic links, then explain blocking issues and remedies. Distribution and case sensitivity are recorded without independently blocking launch.
- **Protection while running:** ownership markers identify game processes started by the launcher, including through UMU/pressure-vessel and after the launcher exits. Processes launched through other entry points that use the installation are also checked.
- **Automatic UID reading:** the Resource Panel reads the UID from game Cookie data in the configured compatibility prefix.
- **Diagnostics:** exported information includes runner process snapshots and stdout/stderr, GPU and Vulkan detection, desktop session type, prefix metadata, and locally installed Proton builds.
- **Single instance and proxy:** another launch activates the existing instance. System proxy mode reads GNOME proxy settings.

### Verified combination and limits

On 2026-09-22, a Debug build was tested on **Arch Linux + UMU 1.4.4 + UMU-Proton 10.0-4 + an NTFS3 game directory**. First launch, login, gameplay, exit, and relaunch succeeded without an XIGNCODE3 block. See the [hardware verification record (Chinese)](./docs/design/linux-p0a-verification-2026-09-22.md).

Other distributions, Proton builds, and graphics drivers remain **unverified**. Anti-cheat compatibility depends on these components, and major updates may require retesting.

## Wallpaper sources

Settings → Appearance → Background offers three sources.

### Bundled wallpaper

The default wallpaper is cropped from **[初めてのゲーム](https://www.pixiv.net/artworks/142932674)** by Pixiv artist **めるき (Meruki)**, artwork ID `142932674`.

- The bundled file is `src/Cafe.Launcher.UI/Assets/launcher-background.png` (2560 × 1388), center-cropped and converted to PNG.
- Original and cropped source materials are archived in [`docs/assets/art-sources/`](./docs/assets/art-sources/), outside application packaging: `142932674_p0.jpg` (2970 × 1610) and `142932674_p0_cut.jpg` (2560 × 1388).
- Attribution also appears under Settings → About → Legal, with a link to the [original Pixiv artwork](https://www.pixiv.net/artworks/142932674).

**The illustration belongs to めるき (Meruki).** It is used only as the launcher background, is excluded from the source code's MIT license, and must not be extracted for other uses. Contact the artist for permission to use it elsewhere.

### Remote wallpaper

Remote mode reads `LauncherBackgroundImg` and its CRC64 from the official game launcher configuration. The image is downloaded into the local cache and decoded for the window size. Yostar's API supplies it; copyright belongs to its rights holders. It is not distributed in this project's packages.

The launcher falls back to the bundled wallpaper if downloading fails or no image is configured.

### Custom wallpaper

Choose a local image or a folder from which an image is randomly selected on refresh. Supported formats are `.png`, `.jpg`, `.jpeg`, `.bmp`, and `.webp`. These files stay local; users are responsible for their rights and permissions. The project does not collect, upload, or distribute them.

The repository's [key visual](./docs/assets/key-visual.png) and release banners are project-created promotional materials containing launcher screenshots.

## Help and feedback

Check the [FAQ](https://docs.bluearchive.cafe/en/cafe-launcher/faq), then follow the [Feedback Guide](https://docs.bluearchive.cafe/en/cafe-launcher/feedback) to prepare your version, reproduction steps, screenshots, and diagnostic logs.

Search [existing Issues](https://github.com/bluearchive-cafe/Cafe.Launcher.Avalonia/issues) first. If new Issues are unavailable or the matter is private, use the contact email in the feedback guide. Review logs for local paths and other private information before sharing.

## Developer guide

This repository contains the Avalonia host and UI assembly, an application core without Avalonia, the Windows update helper and its core library, and unit and Headless UI test projects.

| Project | Responsibility |
| --- | --- |
| [`src/Cafe.Launcher`](./src/Cafe.Launcher/) | WinExe host: process lifetime, single instance, DI composition |
| [`src/Cafe.Launcher.UI`](./src/Cafe.Launcher.UI/) | Views, ViewModels, themes, localization, and runtime assets |
| [`src/Cafe.Launcher.Core`](./src/Cafe.Launcher.Core/) | Protocols, installation state, networking, settings, downloads, and runtimes; no Avalonia |
| [`src/Cafe.Launcher.Updater.Core`](./src/Cafe.Launcher.Updater.Core/) | Update argument contract, validation, path planning, and application |
| [`src/Cafe.Launcher.Updater`](./src/Cafe.Launcher.Updater/) | Single-file Windows helper, copied to a temporary directory to apply updates |
| [`tests/Cafe.Launcher.Tests`](./tests/Cafe.Launcher.Tests/) | xUnit v3 unit tests |
| [`tests/Cafe.Launcher.HeadlessTests`](./tests/Cafe.Launcher.HeadlessTests/) | Avalonia Headless UI tests and golden screenshots |

### Requirements

- .NET SDK `10.0.302`, pinned by `global.json`, with roll-forward within the same feature band.
- PowerShell 7 (`pwsh`) for repository scripts. Windows PowerShell 5.1 cannot parse the scripts' `??` syntax.
- Nullable references, compiled bindings, code-style checks, and warnings as errors are enabled. A successful build has no warnings.

### Build and run

From the repository root in `pwsh`:

```powershell
.\build.ps1
dotnet run --project .\src\Cafe.Launcher\Cafe.Launcher.csproj
```

### Common commands

| Command | Purpose |
| --- | --- |
| `.\test.ps1` | Run unit and Headless UI tests |
| `.\test.ps1 -Suite Unit` / `-Suite Headless` | Run one suite; choices are All, Unit, Headless |
| `.\test.ps1 -Filter "FullyQualifiedName~VersionComparerTests"` | Run a filtered subset |
| `.\dev.ps1 ui` | Run UI style contracts and Headless checks |
| `.\coverage.ps1` | Run the coverage gate |
| `.\verify.ps1` | Debug build, coverage, then Release build |
| `.\scripts\Test-LocalizationContract.ps1` | Check resource keys and format placeholders |
| `.\scripts\Build-Distribution.ps1` | Publish and package self-contained releases |
| `.\scripts\New-WindowsInstaller.ps1` | Build the Inno Setup installer from `artifacts/publish/win-x64` |

Run one test class:

```powershell
dotnet test .\tests\Cafe.Launcher.Tests\Cafe.Launcher.Tests.csproj --filter "FullyQualifiedName~VersionComparerTests"
```

Run `.\verify.ps1` before submitting code. Regenerate golden screenshots with `.\test.ps1 -UpdateGolden`.

### Code structure

```text
src/Cafe.Launcher/               # Program, App, composition, forwarding, crash reporter launch
├── Composition/               # DI composition root
├── Constants/                 # BuildInfo
├── Services/                  # Cross-process forwarding, shutdown deferral, crash reporter launch
├── Properties/                # Test-only InternalsVisibleTo
└── Assets/                    # Icon pipeline input: app-icon-source.png

src/Cafe.Launcher.Core/          # Protocols, state, transport, settings
src/Cafe.Launcher.UI/            # Views, ViewModels, Features, Controls, Converters,
                                # Models, Helpers, Services, Resources, Assets
src/Cafe.Launcher.Updater.Core/  # Update contracts and implementation
src/Cafe.Launcher.Updater/       # Single-file Windows helper host
tests/
├── Cafe.Launcher.Tests/         # Unit tests
├── Cafe.Launcher.HeadlessTests/ # Headless UI tests
└── Support/                    # Facilities shared by both suites
```

Each project's directory, `.csproj`, `AssemblyName`, `RootNamespace`, and source namespace use the same token. Release assets use the product token as well. See [ADR-043 (Chinese)](./docs/design/adr/ADR-043-程序集与命名空间同名.md). The GitHub repository keeps its historical name, `Cafe.Launcher.Avalonia`.

### Architecture

- Dependencies flow host → UI → Core → Updater.Core; the host also references Core and Updater.Core directly. Core cannot reference Avalonia, MarkView, Material Icons, or UI resources. Production assemblies do not use `InternalsVisibleTo` with each other. See [ADR-042](./docs/design/adr/ADR-042-Avalonia启动器按Core与UI程序集分层.md) and [ADR-043](./docs/design/adr/ADR-043-程序集与命名空间同名.md), both in Chinese.
- `Composition/ServiceConfiguration.cs` is the DI composition root. It calls `AddLauncherCore` before registering presentation services. All services and ViewModels are singletons for this single-window app.
- `Features/` is organized vertically. Features communicate through narrow shared service contracts rather than each other's concrete types. `Features/Shell` intentionally aggregates the feature ViewModels as the window shell.
- `Services/RemoteHttpTransport` owns outbound requests, proxy behavior, SSRF validation, redirects, timeouts, and retries. `Services/HttpClientFactory` owns pooled connections and proxy-aware leases; do not create ad-hoc `HttpClient` instances elsewhere.
- Game file operations go through `Helpers/GamePathValidator` and remain within the normalized `YostarGames\BlueArchive_JP` directory.
- The composition root resolves the data root once and injects it for test isolation.
- Only `ISavedSettingsWriter` writes `settings.json`; other modules read through `LauncherSettingsService`.
- Interface strings must be added alphabetically to `src/Cafe.Launcher.UI/Resources/LauncherStrings.resx` and the `zh-Hans`, `zh-Hant`, and `ja` variants.

Detailed contracts are in [`AGENTS.md`](./AGENTS.md), [`PROJECT_CONVENTIONS.md`](./PROJECT_CONVENTIONS.md), [`CONTEXT.md`](./CONTEXT.md), and [`UBIQUITOUS_LANGUAGE.md`](./UBIQUITOUS_LANGUAGE.md). Design decisions are in [`docs/design/adr/`](./docs/design/adr/). Some contributor documents are currently in Chinese.

### Tests

- xUnit v3, with `Avalonia.Headless.XUnit` for UI tests.
- Name tests `Method_State_ExpectedResult`; source and resource contracts may use `Subject_Expectation`.
- Use handwritten `HttpMessageHandler` subclasses, fakes, and stubs rather than mocking frameworks. Shared doubles live in `tests/TestDoubles/`.
- Use `Assert.SkipUnless` / `Assert.SkipWhen` for explicit platform skips.
- Run `UiStyleContractTests` after XAML or style edits, and `.\dev.ps1 ui` for larger UI changes.

### Contributing

- Search existing [Issues](https://github.com/bluearchive-cafe/Cafe.Launcher.Avalonia/issues) before reporting, and include your version, platform, and reproduction steps.
- Follow Conventional Commits, for example `fix(settings): restore legal information after changing language`.
- Keep commits focused. Include screenshots for visible UI changes and update all language resources for new interface text.
- Update `AGENTS.md` in the same change when architecture or development workflows change.

## Local data and licensing

On Windows, settings, download state, and logs default to `%LOCALAPPDATA%\Cafe Launcher\`. Manifest and configuration files in the game directory record installation state and remain compatible with the official launcher. Uninstalling the launcher preserves game files by default. See [Uninstall and Data](https://docs.bluearchive.cafe/en/cafe-launcher/uninstall).

Source code is released under the [MIT License](./LICENSE). Third-party components and their licenses are listed in [THIRD-PARTY-NOTICES.md](./THIRD-PARTY-NOTICES.md).

The bundled wallpaper belongs to めるき (Meruki) and is excluded from the MIT license. See [Wallpaper sources](#wallpaper-sources).

## Acknowledgments

- The default wallpaper is [初めてのゲーム](https://www.pixiv.net/artworks/142932674) by [めるき (Meruki)](https://www.pixiv.net/users/15737611). Copyright remains with the artist; this project uses it only as a launcher background and claims no ownership.
- Thanks to [Avalonia](https://avaloniaui.net/), [Material.Icons.Avalonia](https://github.com/SKProCH/Material.Icons.Avalonia), [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet), and the other open-source projects used here.
- Thanks to everyone who reports issues, shares feedback, and helps test the launcher.

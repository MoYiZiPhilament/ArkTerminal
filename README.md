# ArkTerminal

**[简体中文](README.zh-CN.md)**

<p align="center">
  <img src="Assets/AppIcon.png" alt="ArkTerminal Camellia icon" width="96" height="96" />
</p>

A native Windows terminal and Agent CLI workbench built with WPF, ConPTY, and DirectWrite / Direct3D. It provides real Shell sessions, multiple tabs, and a customizable interface.

Created by **GPT-6.1 Sol**, **Kimi K3.1 Pro**, and **Qwen4 Max Preview**, using the staged prompts in [Prompt/](Prompt/). The application icon was drawn by **GPT Image 2.5 Sunburst**.

Versions use three numeric components plus a letter suffix: `a` for a major release, `b` for a beta release (replacing the former `preview`), and `c` for a special-handling release.

## Download and use

[Download the 1.2.6a major release](https://github.com/MoYiZiPhilament/ArkTerminal/releases/tag/v1.2.6a). Packages target Windows x64 and include the .NET runtime.

| Format | Usage | Data directory |
| --- | --- | --- |
| Portable ZIP (recommended) | Extract the entire archive to a writable directory and run `ArkTerminal.exe` | `data` beside the application |
| MSI | Per-user installation with Start menu and uninstall entries | `%APPDATA%\ArkTerminal` |
| Development-signed MSIX | Isolated development validation only; not trusted by default | Package-private `LocalState\ArkTerminal` |

The first launch opens the new session page. Select a Shell / Profile or preview an Agent recipe before starting anything. Restored layouts contain only unstarted tabs. Updates are manual, and data is not automatically migrated between modes. Exit and back up the data directory before upgrading. See [Distribution and maintenance](Packaging/README.md) for installation, backup, and build details.

The first launch also selects the UI language from Windows (Simplified Chinese for a Chinese system, English otherwise) and saves the current Windows time zone. Both can be changed under **Settings → Configuration and Profiles**. Later launches use the saved choices rather than tracking system changes. User-facing times use the configured time zone; stored records and diagnostics retain UTC.

`1.2.6a` is a major release that fixes special-key input (Esc, Ctrl combinations, and function keys) for Agents launched directly from a Shell, and makes typing echo and block-cursor motion smooth by presenting completed GPU frames, shortening the D3DImage lock hold, and repainting cursor row bands incrementally at render priority. It builds on the English/Chinese localization and English-first README. Windows 10, cross-display, physical-input, and long-running resource validation remain incomplete. Actual MSI install / repair / uninstall / upgrade acceptance is still pending. MSIX has not completed isolated installation validation; MCP and administrator sessions remain unavailable under package identity. See [Project progress](PROGRESS.md) for the full scope.

## Features

- **Terminal and sessions**: CMD, Windows PowerShell, PowerShell 7, and Git Bash; multiple tabs, three tab-bar positions, independent sessions, restart, and layout recovery.
- **Input and reading**: Unicode graphemes, Chinese input, selection and copy, search, scrollback, font zoom, line numbers, and timestamps.
- **Appearance and settings**: light/dark themes, Mica / Acrylic capability fallback, background images, animated aurora, performance mode, and separate settings and Profile drafts.
- **Shell workflows**: command status, link previews, unfocused-window notifications, bounded history and transcription, and UAC administrator sessions in standard / portable modes.
- **Agent CLI**: bounded path detection, on-demand version probes, launch recipes and previews, and reviewed custom JSON adapters.
- **Context and MCP**: editable context, copy-based handoff, Markdown snapshots, independent command review, and five read-only MCP tools.

Agent detection and launch support depend on adapter declarations. A successful version probe does not validate an authenticated model task. The default DeepSeek Harness adapter does not provide an unverified interactive TUI recipe.

## Context and privacy

Open the context workflow from the application menu, command palette, or `Ctrl+Shift+F9`. Review and edit the final content, copy it, and paste it manually into the target CLI. Copying does not submit or execute anything. Credential redaction is best-effort; inspect content before sharing.

Copy MCP configuration from the running application. The bridge resides in the `Mcp` subdirectory of the complete application directory. Every connection starts without authorization and can read only explicitly authorized fields for one session; it cannot send input or execute commands. Configuration and authorization do not persist across application restarts. See [Read-only MCP usage](Examples/readonly-mcp.md).

Application command history is disabled by default on every run, and transcription is disabled by default for every new session. The application does not modify global Agent configuration, collect telemetry, or automatically upload terminal content.

## Build from source

Requires Windows and the .NET 10 SDK. The current build uses SDK `10.0.400`, with the runtime pinned to `10.0.11`. Dependencies are pinned by `packages.lock.json`.

```powershell
git clone https://github.com/MoYiZiPhilament/ArkTerminal.git
cd ArkTerminal
dotnet build ArkTerminal.sln -c Release -p:RestoreLockedMode=true
dotnet run --project ArkTerminal.csproj -c Release --no-build
```

For an isolated trial, append `-- --portable` to the run command. The standard data root is `%APPDATA%\ArkTerminal`; portable mode can also be enabled by a `portable.flag` beside the application.

```powershell
pwsh -File Packaging/Build-Packages.ps1 -WindowsSdkBin '<Windows SDK x64 tools directory>' -DevelopmentSign
pwsh -File Packaging/Build-Msi.ps1
```

Packaging also requires the Windows SDK and WiX toolchains. See [Packaging instructions](Packaging/README.md) for exact versions and setup. Default output is `bin/Packages/1.2.6a/`, containing ZIP, MSIX, MSI, and SHA-256 manifests. The exe and MSI are not Authenticode-signed; MSIX uses an ephemeral development certificate.

## Source and documentation

| Path | Contents |
| --- | --- |
| `ArkTerminal.csproj` | WPF windows and application orchestration |
| `ArkTerminal.Core/` | Configuration, localization, Profiles, recovery, storage, context, and read-only contracts |
| `ArkTerminal.Terminal/` | ConPTY, VT, buffers, Shell / Agent, elevation, and read-only IPC |
| `ArkTerminal.Mcp/` | Independent stdio MCP bridge |
| `Rendering/`, `Input/` | DirectWrite / Direct3D rendering and WPF / TSF input |
| `Selection/`, `Search/`, `Sessions/`, `Visuals/` | Selection, search, tabs, and visual effects |
| [AGENTS.md](AGENTS.md) / [CLAUDE.md](CLAUDE.md) | Development constraints and Claude entry point |
| [PROGRESS.md](PROGRESS.md) | Current progress, validation scope, and outstanding work |
| [Implementation history](docs/IMPLEMENTATION-HISTORY.md) | Original implementation and acceptance records by stage |
| [Prompt/](Prompt/) | Staged prompts; requirements are not completion records |
| [Examples/](Examples/) | Configuration, Profile, adapter, and MCP examples |
| [Packaging/](Packaging/) | Packaging scripts, licenses, and maintenance notes |

## License

Licensed under [Apache License 2.0](LICENSE). Third-party dependencies retain their own licenses. Release packages include [third-party notices](Packaging/THIRD-PARTY-NOTICES.md), runtime and Unicode licenses, and a dependency manifest. See [MSI notices](Packaging/MSI-NOTICES.md) for WiX component licenses and corresponding source.

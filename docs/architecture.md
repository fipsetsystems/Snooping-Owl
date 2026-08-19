# SnoopingOwl Architecture

Authorized BPO workstation operations/telemetry platform.

## Product

- Product name: **OWL**
- Project name: **SnoopingOwl**
- Agent service name: **SnoopingOwl**
- Service display name: **SnoopingOwl Agent**

## System

```text
Windows 10/11 workstation
        ↓
SnoopingOwl Agent (C++/Qt 6, Windows Service)
        ↓
Secure WebSocket (WSS)
        ↓
Node.js + TypeScript server
        ↓
Next.js + React + TypeScript dashboard
        ↓
PostgreSQL
```

## Phase 1 Scope (this phase)

Windows installation foundation only:

- Windows Service skeleton (SCM integration, lifecycle, failure recovery)
- Bounded file logging
- Minimal configuration boundary
- Professional installer (WiX v5, MSI + Burn) — built on Windows machines

Explicitly NOT in Phase 1: telemetry, WebSocket protocol, identity/enrollment
protocol, dashboard, server, database schema.

## Decisions (Phase 1)

| Decision | Choice | Rationale |
|---|---|---|
| Agent UI | Service only; no tray app, no UI after installation | Installer is the only UI; less surface area |
| Service start | Automatic; starts immediately after install | Legitimate persistence via SCM only |
| Service account | LocalSystem | Standard for endpoint agents; no user session dependency |
| Logging | File only, bounded rotation (~5 MB x 5) | `%ProgramData%\SnoopingOwl\Logs\agent.log` |
| Config | Versioned JSON, `%ProgramData%\SnoopingOwl\config.json` | Simple, diffable, machine-wide |
| Installer | WiX v5 (MSI + Burn bootstrapper) | Enterprise: transactional service install, repair, silent deploy |
| Deployment | Native Windows build | Windows 10/11 test machines available |
| Install location | `%ProgramFiles%\SnoopingOwl` (fixed; Options UI suppressed) | Standard for managed enterprise agents |
| Versioning | Single source of truth: `agent/version.json` | CMake + Windows file version info + CI artifact naming |
| CI | GitHub Actions, `windows-latest`, x64 MSVC Release | Produces win64 `agent.exe` only; GitHub Release on `v*` tags |

## Reserved Boundaries

Created only when their purpose is established (protocol phase):

- `networking/` — WebSocket client. Protocol to be designed next phase.
- `identity/` — stable agent identity + enrollment. IP address is never the
  primary identity.

## Security Model

- Per-machine install with UAC elevation (`requireAdministrator`).
- Service installed via MSI `ServiceInstall`/`ServiceControl` (transactional).
- Service DACL restricted to SYSTEM + Administrators: ordinary users cannot
  stop/start/configure the service.
- `%ProgramData%\SnoopingOwl\` ACL: SYSTEM/Administrators full, Users read.
- Uninstall: normal Programs & Features / `msiexec /x`. No anti-uninstall
  behavior. No persistence outside the SCM (no Run keys, scheduled tasks,
  startup folders, injection, or watchdog processes).
- No secrets in logs or config. Config never logged wholesale.

## Service Lifecycle

```text
Install (MSI)
  → ServiceInstall (auto start) + ServiceConfig (failure actions)
  → service starts immediately
Reboot → SCM auto-starts service
  → ServiceMain: register handler → SERVICE_RUNNING
  → Qt event loop (agent idle; WebSocket arrives next phase)
Stop (SCM) → clean shutdown → SERVICE_STOPPED
Crash → SCM failure actions restart the service
```

## Config Schema (v1)

Only fields with an established purpose. Identity/server/enrollment fields are
added in the protocol phase.

```json
{
  "schemaVersion": 1,
  "logging": {
    "level": "info"
  }
}
```

## Logging

- Location: `%ProgramData%\SnoopingOwl\Logs\agent.log` (Windows);
  `~/.local/share/SnoopingOwl/logs/agent.log` (Linux dev).
- Bounded rotation: ~5 MB per file, 5 files.
- Line format: `ISO8601 [level] [category] message`.
- Levels: debug, info, warn, critical (configurable via config).
- Messages sanitized (newlines stripped) to prevent log injection.
- Never log credentials or config contents.

## Agent Executable Modes

| Mode | Trigger | Behavior |
|---|---|---|
| Service | launched by SCM (no args) | `StartServiceCtrlDispatcher` |
| `--run` | console/dev | foreground run, no SCM |
| `--install` | admin console | register service via SCM API (dev/test; installer uses MSI) |
| `--uninstall` | admin console | stop + remove service |

## Verification Targets

- CachyOS: build Linux subset (configuration, diagnostics, entry) with Qt
  6.11.2 kit.
- Windows 10 + Windows 11: full build; `--install`/`--run`/`--uninstall`;
  reboot persistence; uninstall dialog.
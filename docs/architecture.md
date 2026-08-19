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

## Phase 2 Scope (this phase)

Agent ↔ server protocol v1 over WebSocket, CLI-only:

- Server gateway: Node.js + TypeScript, **Fastify + @fastify/websocket**
- Token-authenticated `hello`, 15 s heartbeats, 45 s server watchdog
- Agent reconnect with exponential backoff (1 s → 30 s)
- Stable per-machine device ID (`device.id`, seeded from machine unique ID)

Explicitly NOT in Phase 2: telemetry, commands, enrollment, dashboard,
database schema, per-device auth.

## Decisions (Phase 1 + Phase 2)

| Decision | Choice | Rationale |
|---|---|---|
| Agent UI | Service only; no tray app, no UI after installation | Installer is the only UI; less surface area |
| Service start | Automatic; starts immediately after install | Legitimate persistence via SCM only |
| Service account | LocalSystem | Standard for endpoint agents; no user session dependency |
| Logging | File only, bounded rotation (~5 MB x 5) | `%ProgramData%\SnoopingOwl\Logs\agent.log` |
| Config | Versioned JSON, `%ProgramData%\SnoopingOwl\config.json` | Simple, diffable, machine-wide |
| Installer | WiX v7 (MSI + Burn bootstrapper) | Enterprise: transactional service install, repair, silent deploy |
| Deployment | Native Windows build | Windows 10/11 test machines available |
| Install location | `%ProgramFiles%\SnoopingOwl` (fixed; Options UI suppressed) | Standard for managed enterprise agents |
| Versioning | Single source of truth: `agent/version.json` | CMake + Windows file version info + CI artifact naming |
| CI | GitHub Actions, `windows-latest`, x64 MSVC Release | Produces win64 `agent.exe` only; auto GitHub Release on `v*` tags |
| Server runtime | Node.js + TypeScript | Already installed; shared type discipline with dashboard |
| Server framework | Fastify + @fastify/websocket | Fast, structured HTTP+WS in one TypeScript codebase |
| Realtime protocol | Raw WebSocket, JSON, versioned (`v` field) | Lean agent link; no framework lock-in on the wire |
| Auth (current) | Shared token (`AGENT_TOKEN` / `connection.token`) | Dev-grade; per-device auth/enrollment later |

## Reserved Boundaries

Created only when their purpose is established (enrollment/dashboard phases):

- `enrollment/` — per-device identity + enrollment. IP address is never the
  primary identity.
- `telemetry/` — metrics/event payloads (`telemetry`, `event`, `command`
  protocol types reserved in v1).

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
  → Qt event loop (agent idles + maintains WebSocket to server)
Stop (SCM) → clean shutdown → SERVICE_STOPPED
Crash → SCM failure actions restart the service
```

## Config Schema (v1)

Only fields with an established purpose.

```json
{
  "schemaVersion": 1,
  "logging": {
    "level": "info"
  },
  "connection": {
    "url": "ws://127.0.0.1:8080/ws/agent",
    "token": "dev-token"
  }
}
```

`connection.token` is a shared secret; never logged. Enrollment/per-device
auth replaces it in a later phase.

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
| Service | launched by SCM (no args) | `StartServiceCtrlDispatcher`; runs WebSocket link |
| `--run` | console/dev | foreground run, no SCM; runs WebSocket link |
| `--install` | admin console | register service via SCM API (dev/test; installer uses MSI) |
| `--uninstall` | admin console | stop + remove service |

## Protocol

Agent ↔ server WebSocket protocol v1, token-authenticated `hello`,
15 s heartbeats, 45 s server watchdog, reconnect backoff 1 s → 30 s.
Full message spec: `docs/protocol.md`.

## Verification Targets

- CachyOS: Linux subset (configuration, diagnostics, entry, WebSocket link)
  with Qt 6.11.2 kit; server E2E: connect, heartbeat, kill/restart server,
  bad-token rejection — all passing.
- Windows 10 + Windows 11: full build; `--install`/`--run`/`--uninstall`;
  reboot persistence; uninstall dialog; agent ↔ server over LAN.
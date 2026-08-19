# SnoopingOwl

Authorized BPO workstation operations/telemetry platform.

- **Agent**: C++ / Qt 6 / CMake — Windows Service on Windows 10/11
- **Server**: Node.js + TypeScript — Fastify + WebSocket gateway
- **Dashboard**: Next.js + React + TypeScript (future phase)

## Current status: Phase 2 — agent ↔ server protocol v1

The agent connects to the server over WebSocket (JSON protocol v1), performs
a token-authenticated `hello`, and heartbeats every 15 s with reconnect
backoff. Both sides run as CLI for now; the dashboard and per-device auth
come in later phases.

## Layout

```text
agent/            C++/Qt 6 agent
  src/service/        Windows Service core (SCM integration)
  src/configuration/  versioned JSON config boundary
  src/diagnostics/    bounded file logging
  src/identity/       stable per-machine device ID
  src/protocol/       WebSocket link to the server (protocol v1)
  installer/          WiX v7 project (optional, Windows-only build)
server/           Node.js + TypeScript — Fastify WebSocket gateway
dashboard/        Next.js + React (future)
docs/             architecture decisions, protocol spec
```

## Versioning

The product version lives in a single source of truth: `agent/version.json`.
CMake reads it (also embedded as Windows file version info in `agent.exe`),
and CI uses it to name artifacts and GitHub Releases.

## Releases (win64 only)

`.github/workflows/win64-release.yml` builds the **win64 Release** `agent.exe`
(MSVC, x64 — no ARM, no Linux) on GitHub Actions:

- **Manual run**: Actions → "win64-release" → Run workflow → download
  `SnoopingOwl-Agent-<version>-win64.exe` — a **single static binary**
  (Qt built in, no DLLs/plugins to ship)
- **Tag push** (`git tag v0.1.0 && git push origin v0.1.0`): builds and
  **publishes a GitHub Release** automatically with the exe attached

No MSI is produced by CI; the WiX sources in `agent/installer/` remain
available for enterprise packaging if needed.

## Running (dev, CLI)

```sh
# Server (http://127.0.0.1:8080, ws://127.0.0.1:8080/ws/agent)
cd server && npm install && npm run dev

# Agent (connects from config.json defaults)
./build/agent --run
```

Endpoint, port, and agent token are configurable via `HOST`, `PORT`,
`AGENT_TOKEN` env vars on the server; `connection.url` / `connection.token`
in the agent config (`%ProgramData%\SnoopingOwl\config.json` on Windows,
`~/.config/SnoopingOwl/config.json` on Linux). See `docs/protocol.md` for
the message spec.

## Building

### Linux (development subset — configuration, logging, entry)

```sh
cmake -G Ninja -S agent -B build -DCMAKE_PREFIX_PATH=~/Qt/6.11.2/gcc_64
ninja -C build
./build/agent --run
```

### Windows (full agent, including service)

Install Qt 6 with the Windows kit (MinGW or MSVC), then:

```sh
cmake -G Ninja -S agent -B build -DCMAKE_PREFIX_PATH=<path-to-qt-windows-kit>
ninja -C build
```

Run from an **administrator** console:

```bat
agent.exe --install    :: register + start the SnoopingOwl service
agent.exe --uninstall  :: stop + remove the service
agent.exe --run        :: run in the foreground (no service)
```

## Files on Windows after install

```text
%ProgramFiles%\SnoopingOwl\agent.exe
%ProgramData%\SnoopingOwl\config.json
%ProgramData%\SnoopingOwl\Logs\agent.log
```

## Installer (optional, enterprise packaging)

WiX v7 MSI + Burn bundle sources in `agent/installer/` (build on Windows,
see its README). `SnoopingOwlSetup.exe` provides the professional wizard
flow; silent deploy via `-quiet` for Intune/GPO/SCCM. The standard release
path is the win64 `agent.exe` from CI, not the MSI.

See `docs/architecture.md` for decisions and reserved boundaries.# Snooping-Owl
# Snooping-Owl

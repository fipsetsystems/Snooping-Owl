# SnoopingOwl

Authorized BPO workstation operations/telemetry platform.

- **Agent**: C++ / Qt 6 / CMake — Windows Service on Windows 10/11
- **Server**: Node.js + TypeScript (future phase)
- **Dashboard**: Next.js + React + TypeScript (future phase)
- **Database**: PostgreSQL (future phase)

## Current status: Phase 1 — Windows installation foundation

The agent service skeleton, bounded file logging, and a minimal configuration
boundary. No telemetry, no protocol, no dashboard yet.

## Layout

```text
agent/            C++/Qt 6 agent
  src/service/        Windows Service core (SCM integration)
  src/configuration/  versioned JSON config boundary
  src/diagnostics/    bounded file logging
  installer/          WiX v5 project (Windows-only build)
server/           Node.js + TypeScript (future)
dashboard/        Next.js + React (future)
docs/             architecture decisions
```

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

See `docs/architecture.md` for decisions and reserved boundaries.# Snooping-Owl

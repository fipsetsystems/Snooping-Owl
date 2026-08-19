# SnoopingOwl — Handoff Context

This file gives a fresh AI (or human) the full state of the SnoopingOwl project so
work can continue without re-deriving history.

## What SnoopingOwl is

Authorized BPO workstation operations/activity monitoring system (250 Windows 10/11
workstations). Product name **OWL**. Explicitly NOT: screen/mic/camera recording,
keylogging, or capturing unrelated personal data. Focus: operational signals
(online/offline, activity state, dialer state, etc. — telemetry schema NOT designed yet).

## Architecture (locked with the product owner)

```text
Windows Agents (C++/Qt6, Windows Service, outbound WSS only)
        ↓ wss://api.snoopingowl.com/agent
Cloudflare Tunnel → mini PC (Linux) → Node.js + TypeScript (Fastify + WS)
        ↓ /live
Next.js dashboard on Vercel (browser holds the WS; pages only, no serverless WS)
        ↓ (later) persistence only
Supabase PostgreSQL — NOT involved yet
```

Constraints the owner explicitly imposed (do not violate):
- NO VPS, NO ngrok, NO Tailscale, NO router port-forwarding, NO inbound ports
  anywhere. Public ingress = Cloudflare Tunnel only.
- Agent identity ≠ employee identity (station movement must be possible later).
- Do NOT store everything: live state in memory; only curated events persist later.
- Dashboard on Vercel, never serverless WS, never aggressive polling.
- No "Owl-prefixed" cute names (OwlGateway etc.) — normal technical names.
- Single-responsibility files, ~300 lines/file, KISS, no premature abstraction,
  no magic numbers, no hardcoded secrets, no fake/mock functionality.
- UNSPECIFIED items are NOT to be invented: auth mechanism, event schema, retention,
  employee correlation (Slack/VICIdial), monitoring rules, dashboard auth, UI pages.

## Current implementation status (all committed & pushed, repo `fipsetsystems/Snooping-Owl`, private)

- **Phase 1 — installation foundation** ✅ Windows Service skeleton (SCM, LocalSystem,
  auto-start, failure recovery, SYSTEM+Admins DACL), bounded file logging
  (5 MB × 5), versioned JSON config boundary.
- **Phase 2 — protocol v1** ✅ Agent↔server WebSocket: token `hello`, heartbeat 15 s,
  server watchdog 45 s, reconnect backoff 1→30 s with ±20% jitter, stable per-machine
  device ID (`device.id`, seeded from machine unique ID, persisted).
- **Phase 3 — live dashboard** ✅ Next.js App Router + `/live` WS channel on the server
  (snapshot on connect + agent_joined/agent_left deltas + 25 s keepalive tick for
  Cloudflare idle timeout). No polling, no mock data, theme tokens from owner.
- **CI (win64-release workflow)** — builds a **single static agent.exe** on
  windows-latest: builds Qt 6.11.2 from source (qtbase + qtwebsockets) with
  **static OpenSSL via vcpkg** (WSS-capable), cached via actions/cache
  (key `qt-static-6.11.2-openssl-v1`). Tag push `v*` → auto-published GitHub Release.
- **Local dev verified end-to-end** on CachyOS: agent connect/heartbeat/reconnect
  (server kill/restart), bad-token rejection (4001), /live snapshot+deltas, dashboard build.

## Repository layout

```text
agent/            C++/Qt6 CMake (src/: service, configuration, diagnostics,
                  identity, protocol; installer/: WiX v7 sources, optional)
server/           Node+TS, Fastify + @fastify/websocket (src/: server, agent-session,
                  protocol, config)
dashboard/        Next.js App Router (app/: page.tsx, layout.tsx, globals.css)
docs/             architecture.md (decisions), protocol.md (protocol v1 + /live)
agent/version.json — single source of truth for version (0.1.0)
```

## How to run (dev)

```sh
cd server    && npm install && npm run dev        # :8080, /ws/agent, /live, /health
cd dashboard && npm install && npm run dev       # :3000
./build/agent --run                              # Linux; Windows: agent.exe --run/--install/--uninstall
```

Agent config on Windows: `%ProgramData%\SnoopingOwl\config.json` (created on first
run with defaults; `connection.url` defaults to `ws://127.0.0.1:8080/ws/agent`,
token `dev-token`). Server overrides: `PORT`, `HOST`, `AGENT_TOKEN` env.

## Pending items / decisions awaiting the owner

1. Cloudflare: domain + account (named tunnel) or quick tunnel — owner said "go"
   on quick tunnel but never executed; tunnel + live wss test is the next big step.
2. Confirm the CI run of commit `f4e8d33` (static OpenSSL) went green — owner
   hasn't confirmed; the current released exe's WSS support is unverified.
3. Login/auth design (replaces dev-token; per-device enrollment).
4. Event/telemetry schema + retention (persistence design; Supabase decision).
5. Installer: owner asked about Inno Setup vs WiX Burn for the install dialog
   (exe already has --install/--uninstall); no decision made.
6. Color scheme tokens already provided (dashboard uses them); license text still needed.
7. Owner debated moving dev to Windows; unresolved — a KVM Windows VM was offered.

## Environment notes (this machine)

- CachyOS; Qt 6.11.2 Linux kit at ~/Qt/6.11.2/gcc_64; MinGW available.
- sudo password: 12345. Node v26. dotnet + WiX v7 installed (EULA accepted).
- Public IP 45.115.86.159 (NO port forwarding configured — by design); ufw allows 8080.
- Tailscale present on the box (100.104.163.84) — owner declined Tailscale anyway.
- `pkill -f "pattern"` self-matches the shell's own command line — use bracket
  trick (`pkill -f "agen[t]"`) or kill by PID.
- Backgrounding long-lived processes in the bash tool keeps its stdout open → the
  tool times out waiting; check state in a separate call instead.
- CI logs come from the owner pasting them (no gh CLI, private repo, anonymous API 404).

## Owner communication style

- Stream-of-consciousness, abbreviations, typos. Short, direct answers preferred.
- Wants professional, to-the-point results; explicitly against "vibe-coded" neon/
  deep-blue "dystopian terminal" aesthetics.
- Says "go" to green-light implementation; expects commit+push+tag as the established
  workflow (tag re-push: delete, re-create, push).
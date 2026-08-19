# SnoopingOwl Agent Protocol v1

Versioned JSON messages over WebSocket. The agent is the client, the server
is the endpoint. Endpoint: `ws://<host>:<port>/ws/agent` (TLS via `wss://`
in production).

## Conventions

- Every message is a single JSON object with at least `v` (protocol
  version, currently `1`) and `type`.
- Unknown fields are ignored (forward compatible). Unknown types or
  malformed JSON cause the server to close the connection with status
  1003 (unsupported data).
- All timestamps are ISO-8601 UTC strings.

## Message types

### Agent → Server

| type         | fields                              | purpose |
|--------------|-------------------------------------|---------|
| `hello`      | `token`, `deviceId`, `agentVersion`, `os` | sent immediately after connect; server validates `token` |
| `heartbeat`  | `seq` (incrementing integer)        | sent every 15 s while connected |

### Server → Agent

| type           | fields        | purpose |
|----------------|---------------|---------|
| `hello_ack`    | —             | confirms valid token; sent before any other traffic |
| `heartbeat_ack`| `seq`         | echoes the agent's `seq` |

## Flow

1. Agent connects, sends `hello`. Server validates `token`; on failure it
   closes the connection (status 4001, unauthorized). No other messages
   are exchanged before `hello_ack`.
2. While connected, the agent sends `heartbeat` every 15 s. The server
   expects traffic at least every 45 s; otherwise it closes the
   connection (timeout).
3. The agent reconnects on any disconnect using exponential backoff
   (1 s → 2 s → 4 s → … capped at 30 s), sending a fresh `hello` on every
   new connection.

## Examples

```json
{"v":1, "type":"hello", "token":"dev-token", "deviceId":"a1b2c3d4",
 "agentVersion":"0.1.0", "os":"windows"}
{"v":1, "type":"hello_ack"}
{"v":1, "type":"heartbeat", "seq":7}
{"v":1, "type":"heartbeat_ack", "seq":7}
```

## Reserved (later phases)

`telemetry`, `event`, `command` (+ `command_ack`), `status`. These will be
added without changing `v` unless breaking; a breaking change bumps `v`
and the server rejects older versions.

## Live dashboard channel (/live)

Separate WebSocket endpoint for dashboards: `ws://<host>:<port>/live`.
No authentication yet (dev phase). Server → dashboard messages:

| type           | fields | purpose |
|----------------|--------|---------|
| `snapshot`     | `agents: AgentView[]` | sent once on connect |
| `agent_joined` | `agent: AgentView` | broadcast when an agent registers |
| `agent_left`   | `deviceId` | broadcast when an agent disconnects |
| `tick`         | —      | keepalive every 25 s (Cloudflare edge idle timeout) |

`AgentView` = `{ deviceId, agentVersion, os, remote, connectedAt }`
(read-only live state; never persisted).

## Security notes (current phase)

- `token` is a shared secret from `config.json` / server env
  (`AGENT_TOKEN`); default `dev-token` for development only.
- No transport encryption in dev (`ws://`); production uses `wss://`
  with TLS termination at the server.
- Per-device auth, enrollment, and rotation replace this in a later phase.
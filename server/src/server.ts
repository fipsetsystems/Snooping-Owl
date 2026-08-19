import Fastify from "fastify";
import websocket from "@fastify/websocket";
import type { WebSocket } from "ws";

import { AgentSession } from "./agent-session.js";
import { loadConfig } from "./config.js";
import {
  AgentView,
  HelloMessage,
  LiveMessage,
  isAgentMessage,
  serialize,
  serializeLive,
} from "./protocol.js";

const config = loadConfig();

const fastify = Fastify({ logger: true });
await fastify.register(websocket);

const sessions = new Map<string, AgentSession>();
const liveClients = new Set<WebSocket>();

function sessionView(session: AgentSession): AgentView {
  return {
    deviceId: session.deviceId,
    agentVersion: session.agentVersion,
    os: session.os,
    remote: session.remote,
    connectedAt: session.connectedAt.toISOString(),
  };
}

function broadcastLive(message: LiveMessage): void {
  const payload = serializeLive(message);
  for (const client of liveClients) {
    if (client.readyState === client.OPEN) {
      client.send(payload);
    }
  }
}

function announceSessions(reason: string): void {
  fastify.log.info(
    { count: sessions.size, reason },
    "agent count changed",
  );
}

fastify.get("/health", async () => {
  return {
    status: "ok",
    agents: sessions.size,
    protocolVersion: 1,
    uptimeSeconds: Math.round(process.uptime()),
  };
});

// Realtime live state for dashboards: snapshot on connect, then deltas.
fastify.get("/live", { websocket: true }, (socket) => {
  socket.send(serializeLive({
    type: "snapshot",
    agents: [...sessions.values()].map(sessionView),
  }));
  liveClients.add(socket);
  fastify.log.info({ viewers: liveClients.size }, "dashboard connected");
  socket.on("close", () => {
    liveClients.delete(socket);
    fastify.log.info({ viewers: liveClients.size }, "dashboard disconnected");
  });
});

// Cloudflare's edge drops idle WebSocket connections (~100 s); keep the
// /live channel alive for dashboards behind the tunnel.
const liveKeepalive = setInterval(() => {
  broadcastLive({ type: "tick" });
}, 25_000);
liveKeepalive.unref();

fastify.get("/ws/agent", { websocket: true }, (socket, request) => {
  const remote = request.ip;
  fastify.log.info({ remote }, "agent connected, awaiting hello");

  let session: AgentSession | null = null;
  let helloSeen = false;

  const beginSession = (hello: HelloMessage): void => {
    session = new AgentSession(socket, hello, config.heartbeatTimeoutMs, remote);
    sessions.set(hello.deviceId, session);
    session.sendHelloAck();
    fastify.log.info(
      {
        deviceId: hello.deviceId,
        agentVersion: hello.agentVersion,
        os: hello.os,
        remote,
      },
      "agent registered",
    );
    announceSessions("register");
    broadcastLive({ type: "agent_joined", agent: sessionView(session) });
  };

  socket.on("message", (raw) => {
    let payload: unknown;
    try {
      payload = JSON.parse(raw.toString());
    } catch {
      session?.closeUnsupported();
      return;
    }

    if (!isAgentMessage(payload)) {
      session?.closeUnsupported();
      return;
    }

    if (payload.type === "hello") {
      if (payload.token !== config.agentToken) {
        fastify.log.warn({ remote }, "agent rejected: bad token");
        session?.closeUnauthorized();
        return;
      }
      if (helloSeen) {
        fastify.log.warn({ deviceId: payload.deviceId }, "duplicate hello");
        return;
      }
      helloSeen = true;
      beginSession(payload);
      return;
    }

    if (!session) {
      socket.close(4001, "hello required");
      return;
    }

    session.markActivity();

    if (payload.type === "heartbeat") {
      session.send(serialize({ v: 1, type: "heartbeat_ack", seq: payload.seq }));
    }
  });

  socket.on("close", (code, reason) => {
    const deviceId = session?.deviceId;
    session?.close(code, String(reason));
    if (deviceId) {
      sessions.delete(deviceId);
      fastify.log.info({ deviceId, code, reason: String(reason) }, "agent disconnected");
      announceSessions("disconnect");
      broadcastLive({ type: "agent_left", deviceId });
    }
  });
});

const shutdown = async (signal: string): Promise<void> => {
  fastify.log.info({ signal }, "shutting down");
  for (const session of sessions.values()) {
    session.close(1001, "server shutdown");
  }
  for (const client of liveClients) {
    client.close(1001, "server shutdown");
  }
  await fastify.close();
  process.exit(0);
};

process.on("SIGINT", () => void shutdown("SIGINT"));
process.on("SIGTERM", () => void shutdown("SIGTERM"));

try {
  await fastify.listen({ host: config.host, port: config.port });
  fastify.log.info(`agent endpoint ws://${config.host}:${config.port}/ws/agent`);
} catch (error) {
  fastify.log.error(error);
  process.exit(1);
}
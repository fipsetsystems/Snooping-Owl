import Fastify from "fastify";
import websocket from "@fastify/websocket";

import { AgentSession } from "./agent-session.js";
import { loadConfig } from "./config.js";
import { HelloMessage, isAgentMessage, serialize } from "./protocol.js";

const config = loadConfig();

const fastify = Fastify({ logger: true });
await fastify.register(websocket);

const sessions = new Map<string, AgentSession>();

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

fastify.get("/ws/agent", { websocket: true }, (socket, request) => {
  const remote = request.ip;
  fastify.log.info({ remote }, "agent connected, awaiting hello");

  let session: AgentSession | null = null;
  let helloSeen = false;

  const beginSession = (hello: HelloMessage): void => {
    session = new AgentSession(socket, hello, config.heartbeatTimeoutMs);
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
    }
  });
});

const shutdown = async (signal: string): Promise<void> => {
  fastify.log.info({ signal }, "shutting down");
  for (const session of sessions.values()) {
    session.close(1001, "server shutdown");
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
export const PROTOCOL_VERSION = 1;

export interface HelloMessage {
  v: number;
  type: "hello";
  token: string;
  deviceId: string;
  agentVersion: string;
  os: string;
}

export interface HeartbeatMessage {
  v: number;
  type: "heartbeat";
  seq: number;
}

export interface HelloAckMessage {
  v: number;
  type: "hello_ack";
}

export interface HeartbeatAckMessage {
  v: number;
  type: "heartbeat_ack";
  seq: number;
}

export type AgentMessage = HelloMessage | HeartbeatMessage;
export type ServerMessage = HelloAckMessage | HeartbeatAckMessage;

export function isAgentMessage(value: unknown): value is AgentMessage {
  if (typeof value !== "object" || value === null) {
    return false;
  }
  const message = value as Record<string, unknown>;
  if (message.v !== PROTOCOL_VERSION) {
    return false;
  }
  if (message.type === "hello") {
    return (
      typeof message.token === "string" &&
      typeof message.deviceId === "string" &&
      typeof message.agentVersion === "string" &&
      typeof message.os === "string"
    );
  }
  if (message.type === "heartbeat") {
    return typeof message.seq === "number" && Number.isInteger(message.seq);
  }
  return false;
}

export function serialize(message: ServerMessage): string {
  return JSON.stringify(message);
}
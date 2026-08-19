import type { WebSocket } from "ws";

import { HelloMessage, serialize } from "./protocol.js";

const CLOSE_UNSUPPORTED = 1003;
const CLOSE_UNAUTHORIZED = 4001;
const CLOSE_TIMEOUT = 4002;

/**
 * Tracks one connected agent: identity, traffic watchdog, close handling.
 * The server closes the socket on heartbeat timeout; the agent reconnects
 * with backoff per protocol v1.
 */
export class AgentSession {
  readonly deviceId: string;
  readonly agentVersion: string;
  readonly os: string;
  readonly connectedAt: Date;

  private watchdog: NodeJS.Timeout;
  private closed = false;

  constructor(
    private readonly socket: WebSocket,
    private readonly hello: HelloMessage,
    private readonly timeoutMs: number,
    readonly remote: string,
  ) {
    this.deviceId = hello.deviceId;
    this.agentVersion = hello.agentVersion;
    this.os = hello.os;
    this.connectedAt = new Date();
    this.watchdog = setTimeout(() => this.close(CLOSE_TIMEOUT, "heartbeat timeout"), timeoutMs);
  }

  /** Any valid traffic resets the watchdog. */
  markActivity(): void {
    clearTimeout(this.watchdog);
    this.watchdog = setTimeout(() => this.close(CLOSE_TIMEOUT, "heartbeat timeout"), this.timeoutMs);
  }

  send(message: string): void {
    if (!this.closed && this.socket.readyState === this.socket.OPEN) {
      this.socket.send(message);
    }
  }

  sendHelloAck(): void {
    this.send(serialize({ v: 1, type: "hello_ack" }));
  }

  close(code: number, reason: string): void {
    if (this.closed) {
      return;
    }
    this.closed = true;
    clearTimeout(this.watchdog);
    this.socket.close(code, reason);
  }

  closeUnauthorized(): void {
    this.close(CLOSE_UNAUTHORIZED, "unauthorized");
  }

  closeUnsupported(): void {
    this.close(CLOSE_UNSUPPORTED, "unsupported data");
  }

  isClosed(): boolean {
    return this.closed;
  }
}
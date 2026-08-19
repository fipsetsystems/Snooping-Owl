export interface ServerConfig {
  host: string;
  port: number;
  agentToken: string;
  heartbeatIntervalMs: number;
  heartbeatTimeoutMs: number;
}

function intFromEnv(name: string, fallback: number): number {
  const raw = process.env[name];
  if (raw === undefined) {
    return fallback;
  }
  const value = Number.parseInt(raw, 10);
  return Number.isFinite(value) ? value : fallback;
}

export function loadConfig(): ServerConfig {
  return {
    host: process.env.HOST ?? "0.0.0.0",
    port: intFromEnv("PORT", 8080),
    agentToken: process.env.AGENT_TOKEN ?? "dev-token",
    heartbeatIntervalMs: intFromEnv("HEARTBEAT_INTERVAL_MS", 15_000),
    heartbeatTimeoutMs: intFromEnv("HEARTBEAT_TIMEOUT_MS", 45_000),
  };
}
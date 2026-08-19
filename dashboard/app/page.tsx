"use client";

import { useCallback, useEffect, useRef, useState } from "react";

interface AgentView {
  deviceId: string;
  agentVersion: string;
  os: string;
  remote: string;
  connectedAt: string;
}

type LiveMessage =
  | { type: "snapshot"; agents: AgentView[] }
  | { type: "agent_joined"; agent: AgentView }
  | { type: "agent_left"; deviceId: string }
  | { type: "tick" };

type LinkState = "connecting" | "connected" | "disconnected";

const DEFAULT_SERVER = "ws://127.0.0.1:8080";
const RECONNECT_MS = 3000;

function liveUrl(): string {
  const base = process.env.NEXT_PUBLIC_SERVER_URL ?? DEFAULT_SERVER;
  return new URL("live", base.endsWith("/") ? base : `${base}/`).toString();
}

function shortDeviceId(id: string): string {
  if (id.length <= 16) {
    return id;
  }
  return `${id.slice(0, 10)}…${id.slice(-6)}`;
}

function relativeTime(iso: string, now: number): string {
  const seconds = Math.max(0, Math.floor((now - Date.parse(iso)) / 1000));
  if (seconds < 5) {
    return "just now";
  }
  if (seconds < 60) {
    return `${seconds}s ago`;
  }
  const minutes = Math.floor(seconds / 60);
  if (minutes < 60) {
    return `${minutes}m ago`;
  }
  const hours = Math.floor(minutes / 60);
  if (hours < 24) {
    return `${hours}h ago`;
  }
  return `${Math.floor(hours / 24)}d ago`;
}

export default function Page() {
  const [link, setLink] = useState<LinkState>("connecting");
  const [agents, setAgents] = useState<AgentView[]>([]);
  const [now, setNow] = useState(() => Date.now());
  const reconnectTimer = useRef<ReturnType<typeof setTimeout> | null>(null);

  const connect = useCallback(() => {
    setLink("connecting");
    const socket = new WebSocket(liveUrl());

    socket.onopen = () => setLink("connected");
    socket.onclose = () => {
      setLink("disconnected");
      reconnectTimer.current = setTimeout(connect, RECONNECT_MS);
    };
    socket.onerror = () => socket.close();

    socket.onmessage = (event) => {
      const message = JSON.parse(event.data as string) as LiveMessage;
      if (message.type === "snapshot") {
        setAgents([...message.agents].sort((a, b) =>
          b.connectedAt.localeCompare(a.connectedAt),
        ));
      } else if (message.type === "agent_joined") {
        setAgents((current) =>
          [message.agent, ...current.filter((a) => a.deviceId !== message.agent.deviceId)],
        );
      } else if (message.type === "agent_left") {
        setAgents((current) =>
          current.filter((a) => a.deviceId !== message.deviceId),
        );
      }
    };
  }, []);

  useEffect(() => {
    connect();
    const clock = setInterval(() => setNow(Date.now()), 30_000);
    return () => {
      if (reconnectTimer.current) {
        clearTimeout(reconnectTimer.current);
      }
      clearInterval(clock);
    };
  }, [connect]);

  const online = agents.length;

  return (
    <div className="shell">
      <header className="topbar">
        <div className="brand">
          <span className="brand-mark" aria-hidden="true" />
          <div>
            <h1>OWL</h1>
            <p>Workstation operations</p>
          </div>
        </div>
        <div className="status" data-link={link}>
          <span className="status-dot" aria-hidden="true" />
          <span className="status-text">
            {link === "connected"
              ? `${online} online`
              : link === "connecting"
                ? "connecting"
                : "disconnected"}
          </span>
        </div>
      </header>

      <main className="content">
        <div className="table-wrap">
          <table>
            <thead>
              <tr>
                <th>Device</th>
                <th>OS</th>
                <th>Version</th>
                <th>Address</th>
                <th>Connected</th>
                <th>Status</th>
              </tr>
            </thead>
            <tbody>
              {agents.map((agent) => (
                <tr key={agent.deviceId}>
                  <td className="mono">{shortDeviceId(agent.deviceId)}</td>
                  <td>{agent.os}</td>
                  <td className="mono">{agent.agentVersion}</td>
                  <td className="mono faint">{agent.remote}</td>
                  <td>{relativeTime(agent.connectedAt, now)}</td>
                  <td>
                    <span className="pill ok">Online</span>
                  </td>
                </tr>
              ))}
              {agents.length === 0 && (
                <tr>
                  <td colSpan={6} className="empty">
                    No agents connected yet.
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
        <p className="footnote">
          Live state only — nothing is recorded or stored.
        </p>
      </main>
    </div>
  );
}
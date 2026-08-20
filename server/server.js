#!/usr/bin/env node
import Fastify from 'fastify';
import FastifyWebsocket from '@fastify/websocket';

const fastify = Fastify({ logger: false });

await fastify.register(FastifyWebsocket);

const agents = new Set();

fastify.get('/health', async () => ({ status: 'ok' }));

// Force-update signal: admin triggers it, server broadcasts to all agents.
// Agent receives {"type":"update-available"} over WSS and updates immediately.
fastify.post('/force-update', async (request, reply) => {
  const message = JSON.stringify({ type: 'update-available' });
  let sent = 0;
  for (const socket of agents) {
    if (socket.readyState === 1) {
      socket.send(message);
      sent++;
    }
  }
  console.log(`Force-update broadcast to ${sent} agent(s)`);
  return { broadcast: sent };
});

fastify.get('/ws', { websocket: true }, (connection, request) => {
  agents.add(connection.socket);

  connection.socket.on('message', (msg) => {
    const text = msg.toString();
    try {
      const data = JSON.parse(text);
      if (data.type === 'agent-connect') {
        console.log(`Agent connected: ${data.machineId || 'unknown'}`);
      }
      if (data.type === 'heartbeat') {
        console.log('Heartbeat received');
      }
    } catch (e) {
      // malformed JSON - ignore
    }
  });

  connection.socket.on('close', () => {
    agents.delete(connection.socket);
    console.log('Agent disconnected');
  });
});

const PORT = parseInt(process.env.PORT || '8432', 10);

fastify.listen({ port: PORT, host: '0.0.0.0' }).then(() => {
  console.log(`SnoopingOwl WSS server listening on :${PORT}`);
}).catch(err => {
  fastify.log.error(err);
  process.exit(1);
});

process.on('SIGTERM', async () => {
  console.log('SIGTERM received => shutting down');
  for (const socket of agents) {
    socket.close();
  }
  await fastify.close();
  process.exit(0);
});
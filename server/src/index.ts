import Fastify from 'fastify';
import FastifyWebsocket from '@fastify/websocket';

const fastify = Fastify({ logger: false });

await fastify.register(FastifyWebsocket);

const agents = new Set();

fastify.get('/health', async () => ({ status: 'ok' }));

fastify.get('/ws', { websocket: true }, (connection, request) => {
  agents.add(connection.socket);

  connection.socket.on('message', (msg) => {
    const text = msg.toString();
    try {
      const data = JSON.parse(text);
      // Handle agent events: connect, heartbeat, events
      if (data.type === 'agent-connect') {
        console.log(`Agent connected: ${data.machineId || 'unknown'}`);
      }
      if (data.type === 'heartbeat') {
        // keepalive - just log
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

// Graceful shutdown
process.on('SIGTERM', async () => {
  console.log('SIGTERM received => shutting down');
  for (const socket of agents) {
    socket.close();
  }
  await fastify.close();
  process.exit(0);
});
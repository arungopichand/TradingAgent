const readStdin = async () => {
  let data = '';
  for await (const chunk of process.stdin) {
    data += chunk;
  }
  return data;
};

const main = async () => {
  const raw = await readStdin();
  const config = JSON.parse(raw || '{}');
  const socket = new WebSocket(config.url);
  let subscribed = false;

  const sendSubscribe = () => {
    if (subscribed) {
      return;
    }

    subscribed = true;
    socket.send(JSON.stringify({
      action: 'subscribe',
      trades: config.trades || [],
      bars: config.bars || [],
      updatedBars: config.updatedBars || [],
    }));
  };

  socket.addEventListener('open', () => {
    socket.send(JSON.stringify({
      action: 'auth',
      key: config.key || '',
      secret: config.secret || '',
    }));
  });

  socket.addEventListener('message', (event) => {
    const text = typeof event.data === 'string' ? event.data : String(event.data ?? '');
    process.stdout.write(`${text}\n`);

    try {
      const payload = JSON.parse(text);
      if (!Array.isArray(payload)) {
        return;
      }

      for (const item of payload) {
        if (item?.T === 'success' && item?.msg === 'authenticated') {
          sendSubscribe();
        }
      }
    } catch {
      // Let the parent process decide how to handle malformed payloads.
    }
  });

  socket.addEventListener('error', (event) => {
    process.stderr.write(`${event?.message || 'Node Alpaca stream bridge error'}\n`);
  });

  socket.addEventListener('close', (event) => {
    if (event.code !== 1000) {
      process.stderr.write(`WebSocket closed: ${event.code} ${event.reason || ''}\n`);
      process.exit(1);
      return;
    }

    process.exit(0);
  });

  const shutdown = () => {
    try {
      socket.close(1000, 'shutdown');
    } catch {
      process.exit(0);
    }
  };

  process.on('SIGTERM', shutdown);
  process.on('SIGINT', shutdown);
};

main().catch((error) => {
  process.stderr.write(`${error?.stack || String(error)}\n`);
  process.exit(1);
});

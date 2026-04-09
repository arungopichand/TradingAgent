import http from 'node:http';
import https from 'node:https';
import zlib from 'node:zlib';

const readStdin = async () => {
  let data = '';
  for await (const chunk of process.stdin) {
    data += chunk;
  }
  return data;
};

const main = async () => {
  const raw = await readStdin();
  const request = JSON.parse(raw || '{}');
  const target = new URL(request.url);
  const transport = target.protocol === 'https:' ? https : http;

  const options = {
    method: request.method || 'GET',
    headers: request.headers || {},
    timeout: request.timeoutMs || 30000,
  };

  const response = await new Promise((resolve, reject) => {
    const req = transport.request(target, options, (res) => {
      const chunks = [];
      res.on('data', (chunk) => {
        chunks.push(Buffer.isBuffer(chunk) ? chunk : Buffer.from(chunk));
      });
      res.on('end', async () => {
        const headers = {};
        for (const [key, value] of Object.entries(res.headers)) {
          headers[key] = Array.isArray(value) ? value.join(', ') : String(value ?? '');
        }

        try {
          const body = await decodeBody(Buffer.concat(chunks), headers['content-encoding']);
          resolve({
            statusCode: res.statusCode || 0,
            headers,
            body,
          });
        } catch (error) {
          reject(error);
        }
      });
    });

    req.on('timeout', () => {
      req.destroy(new Error(`Request timed out after ${options.timeout}ms`));
    });

    req.on('error', reject);

    if (request.body) {
      req.write(request.body);
    }

    req.end();
  });

  process.stdout.write(`${JSON.stringify(response)}\n`);
};

const decodeBody = async (buffer, encoding) => {
  const normalizedEncoding = String(encoding || '').toLowerCase();
  if (!normalizedEncoding) {
    return buffer.toString('utf8');
  }

  const unzip = (handler) =>
    new Promise((resolve, reject) => {
      handler(buffer, (error, decoded) => {
        if (error) {
          reject(error);
          return;
        }

        resolve(decoded.toString('utf8'));
      });
    });

  if (normalizedEncoding.includes('gzip')) {
    return unzip(zlib.gunzip);
  }

  if (normalizedEncoding.includes('br')) {
    return unzip(zlib.brotliDecompress);
  }

  if (normalizedEncoding.includes('deflate')) {
    return unzip(zlib.inflate);
  }

  return buffer.toString('utf8');
};

main().catch((error) => {
  process.stderr.write(`${error?.stack || String(error)}\n`);
  process.exit(1);
});

const net = require('node:net');
const crypto = require('node:crypto');
const fs = require('node:fs');
const path = require('node:path');
const { spawn } = require('node:child_process');
const { normalizeEvent } = require('./compatibility.cjs');
const args = process.argv.slice(2);
const portIndex = args.indexOf('-port') + 1;
if (!portIndex) throw new Error('Missing -port');
const log = (text) => {
  const file = path.join(__dirname, 'bridge.log');
  try {
    if (fs.existsSync(file) && fs.statSync(file).size > 1024 * 1024) fs.renameSync(file, file + '.previous');
    fs.appendFileSync(file, new Date().toISOString() + ' ' + text + '\n');
  } catch { /* Logging must not interrupt button delivery. */ }
};
function frame(text) {
  const data = Buffer.from(text);
  const header = Buffer.alloc(data.length < 126 ? 2 : data.length <= 65535 ? 4 : 10);
  header[0] = 0x81;
  if (data.length < 126) header[1] = data.length;
  else if (data.length <= 65535) { header[1] = 126; header.writeUInt16BE(data.length, 2); }
  else { header[1] = 127; header.writeBigUInt64BE(BigInt(data.length), 2); }
  return Buffer.concat([header, data]);
}
let child, upstream, queue = [];
const server = net.createServer(socket => {
  if (upstream) return socket.destroy();
  let pending = Buffer.alloc(0), handshake = false, fragment = '';
  socket.on('error', e => log('local socket ' + e.code));
  socket.on('close', () => { upstream?.close(); child?.kill(); server.close(); });
  socket.on('data', data => {
    pending = Buffer.concat([pending, data]);
    if (!handshake) {
      const end = pending.indexOf('\r\n\r\n');
      if (end < 0) return;
      const key = /Sec-WebSocket-Key: (.*)/i.exec(pending.subarray(0, end).toString());
      if (!key) return socket.destroy();
      socket.write('HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: ' + crypto.createHash('sha1').update(key[1].trim() + '258EAFA5-E914-47DA-95CA-C5AB0DC85B11').digest('base64') + '\r\n\r\n');
      pending = pending.subarray(end + 4); handshake = true;
      upstream = new WebSocket('ws://127.0.0.1:' + args[portIndex]);
      upstream.onopen = () => { queue.forEach(x => upstream.send(x)); queue = []; };
      upstream.onerror = () => log('upstream error');
      upstream.onclose = () => { socket.end(); child?.kill(); server.close(); };
      upstream.onmessage = event => {
        const message = JSON.parse(event.data);
        normalizeEvent(message);
        log('host ' + message.event + ' action=' + (message.action || '') + ' controller=' + (message.payload?.controller || '') + ' state=' + message.payload?.state + ' context=' + message.context + ' device=' + message.device);
        socket.write(frame(JSON.stringify(message)));
      };
    }
    while (pending.length >= 2) {
      const opcode = pending[0] & 15, fin = pending[0] & 128, masked = pending[1] & 128;
      let length = pending[1] & 127, offset = 2;
      if (length === 126) { if (pending.length < 4) return; length = pending.readUInt16BE(2); offset = 4; }
      if (length === 127) { if (pending.length < 10) return; length = Number(pending.readBigUInt64BE(2)); offset = 10; }
      if (length > 32 * 1024 * 1024) return socket.destroy();
      const maskOffset = offset; if (masked) offset += 4;
      if (pending.length < offset + length) return;
      const payload = Buffer.from(pending.subarray(offset, offset + length));
      if (masked) for (let i = 0; i < length; i++) payload[i] ^= pending[maskOffset + i % 4];
      pending = pending.subarray(offset + length);
      if (opcode === 8) { socket.end(); upstream?.close(); return; }
      if (opcode === 9) { const pong = frame(payload); pong[0] = 0x8a; socket.write(pong); continue; }
      if (opcode !== 1 && opcode !== 0) continue;
      fragment += payload.toString(); if (!fin) continue;
      const message = JSON.parse(fragment);
      if (message.event === 'logMessage') {
        const value = JSON.stringify(message.payload || {});
        const tags = value.match(/\b(?:ERROR|INVALID_SCOPE|AUTHENTICATE|AUTHORIZE|GET_VOICE_SETTINGS|SET_VOICE_SETTINGS|ECONNREFUSED|ECONNRESET|[45]\d\d\d?|error|failed|connect|disconnect|scope|invalid|timeout|closed|ready|succeeded|success)\b/gi) || [];
        log('diagnostic ' + tags.join(' ').slice(0, 600));
      }
      log('plugin ' + message.event + ' state=' + message.payload?.state + ' context=' + message.context + ' disabled=' + !!message.payload?.disabled + ' unauthorized=' + !!message.payload?.unauthorized + ' underauthorized=' + !!message.payload?.underauthorized);
      if (upstream?.readyState === 1) upstream.send(fragment); else queue.push(fragment);
      fragment = '';
    }
  });
});
server.listen(0, '127.0.0.1', () => {
  const childArgs = args.slice(); childArgs[portIndex] = String(server.address().port);
  const infoIndex = childArgs.indexOf('-info') + 1;
  if (infoIndex) {
    const info = JSON.parse(childArgs[infoIndex]);
    if (info.application) info.application.language = 'ru';
    childArgs[infoIndex] = JSON.stringify(info);
  }
  child = spawn(path.join(__dirname, 'ESDDiscord.exe'), childArgs, { cwd: __dirname, windowsHide: true });
  child.on('error', e => { log('launch ' + e.code); process.exit(1); });
  child.on('exit', code => { log('exit ' + code); process.exit(code || 0); });
});

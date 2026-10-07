// Scratch diagnostic: raw Modbus TCP probes with PROPER MBAP header (no CRC).
// Usage: node probe_slaves.cjs <ip> <port> <slave> [list of "fc:start:qty"]
const net = require('net');

const ip = process.argv[2] || '10.10.100.254';
const port = Number(process.argv[3] || 502);
const slave = Number(process.argv[4] || 6);
const wins = process.argv.slice(5).length ? process.argv.slice(5) : ['3:0:2', '4:42:2', '4:58:2', '4:64:2', '3:30000:2', '4:30000:2', '3:90:2'];
let txId = 1;

function probe(winStr, timeoutMs) {
  return new Promise((resolve) => {
    const [fc, start, qty] = winStr.split(':').map(Number);
    const tx = txId++ & 0xFFFF;
    const mbap = Buffer.from([(tx >> 8) & 0xFF, tx & 0xFF, 0, 0, 0, 6, slave]);
    const pdu = Buffer.from([fc, (start >> 8) & 0xFF, start & 0xFF, (qty >> 8) & 0xFF, qty & 0xFF]);
    const frame = Buffer.concat([mbap, pdu]);
    const client = new net.Socket();
    let buf = Buffer.alloc(0);
    const done = (txt) => { try { client.destroy(); } catch {} resolve(txt); };
    client.setTimeout(timeoutMs);
    client.connect(port, ip, () => client.write(frame));
    client.on('data', (d) => {
      buf = Buffer.concat([buf, d]);
      // A full MBAP response: header(7) + pdu; need length field
      if (buf.length >= 7) {
        const len = buf.readUInt16BE(4);
        if (buf.length >= 6 + len) {
          const rtx = buf.readUInt16BE(0);
          const rslave = buf[6], rfc = buf[7];
          const body = buf.slice(7).toString('hex');
          done(`${winStr} -> tx=${rtx} slave=${rslave} fc=0x${rfc.toString(16)} len=${len} hex=${body}${rfc & 0x80 ? ' (EXCEPTION code=0x' + buf[8].toString(16) + ')' : ''}`);
        }
      }
    });
    client.on('timeout', () => done(`${winStr} -> TIMEOUT (${timeoutMs}ms)`));
    client.on('error', (e) => done(`${winStr} -> ERROR ${e.message}`));
  });
}

(async () => {
  for (const w of wins) {
    console.log(await probe(w, 3000));
    await new Promise(r => setTimeout(r, 250));
  }
  console.log('--- repeat first window twice on fresh connections ---');
  console.log(await probe(wins[0], 3000));
  console.log(await probe(wins[0], 3000));
})();

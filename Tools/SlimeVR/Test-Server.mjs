// Node.js 22+. Exercises the packaged JAR through the public UDP and WebSocket interfaces.
import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import dgram from 'node:dgram';
import { once } from 'node:events';
import { access, mkdir, mkdtemp, rm, writeFile } from 'node:fs/promises';
import net from 'node:net';
import os from 'node:os';
import path from 'node:path';
import { setTimeout as sleep } from 'node:timers/promises';
import { fileURLToPath } from 'node:url';

const type = 'blacktheon.feet.v1';
const dist = fileURLToPath(new URL('../../Builds/SlimeVR/', import.meta.url));
const args = process.argv.slice(2);
const stockOnly = args[0] === '--stock';
if (stockOnly) args.shift();
const jar = path.resolve(args[0] ?? path.join(dist, stockOnly ? 'Windows' : 'Paired', 'slimevr.jar'));
const java = path.resolve(args[1] ?? path.join(path.dirname(jar), 'jre', 'bin', 'java.exe'));
const roles = ['chest', 'hip', 'left_upper_leg', 'right_upper_leg', 'left_lower_leg', 'right_lower_leg'];
const labels = ['P1-L', 'P1-R', 'P2-L', 'P2-R', 'P3-L', 'P3-R'];
const accelerations = [1.25, -2, 3.5, -4.25, 5, -6.5];
const macs = roles.map((_, index) => '02:00:00:00:00:' + String(index + 11));
// The last board uses an extension sensor. ID zero is not interchangeable with ID one.
const sensorIds = [0, 0, 0, 0, 0, 1];
const trackerIds = macs.map((mac, index) => 'udp://' + mac + '/' + sensorIds[index]);

async function until(predicate, description, ms = 5000) {
  const end = Date.now() + ms;
  while (!await predicate()) {
    assert.ok(Date.now() < end, description);
    await sleep(10);
  }
}

function sendPacket(board, kind, payload, port) {
  const header = Buffer.alloc(12);
  header.writeInt32BE(kind);
  header.writeBigUInt64BE(++board.sequence, 4);
  board.socket.send(Buffer.concat([header, payload]), port, '127.0.0.1');
}

function handshake(board, port) {
  const info = Buffer.alloc(28);
  info.writeInt32BE(4, 0);
  info.writeInt32BE(1, 4);
  info.writeInt32BE(1, 8);
  info.writeInt32BE(22, 24);
  const firmware = Buffer.from('feet-fixture\0');
  const mac = Buffer.from(board.mac.replaceAll(':', ''), 'hex');
  sendPacket(board, 3, Buffer.concat([info, Buffer.from([firmware.length]), firmware, mac]), port);
}

function sensorInfo(board, status, port) {
  sendPacket(board, 15, Buffer.from([board.sensorId, status, 1]), port);
}

function feed(board, port) {
  if (!board.online) return;
  const x = Math.sin(board.angle / 2);
  const w = Math.cos(board.angle / 2);
  if (board.combined && board.sendAcceleration) {
    const combined = Buffer.alloc(15);
    combined[0] = board.sensorId;
    combined.writeInt16BE(Math.round(x * 32767), 1);
    combined.writeInt16BE(Math.round(w * 32767), 7);
    combined.writeInt16BE(Math.round(board.ay * 128), 13);
    sendPacket(board, 23, combined, port);
    return;
  }
  const rotation = Buffer.alloc(19);
  rotation[0] = board.sensorId;
  rotation[1] = 1;
  rotation.writeFloatBE(x, 2);
  rotation.writeFloatBE(w, 14);
  rotation[18] = 3;
  sendPacket(board, 17, rotation, port);
  if (board.sendAcceleration) {
    const acceleration = Buffer.alloc(13);
    // With identity firmware rotation local +Z is world +Y in the stock server.
    acceleration.writeFloatBE(board.ay, 8);
    acceleration[12] = board.sensorId;
    sendPacket(board, 4, acceleration, port);
  }
}

async function connect(clients) {
  const socket = new WebSocket('ws://127.0.0.1:21110');
  const client = { socket, configs: new Map(), poses: new Map(), replies: [] };
  clients.push(client);
  socket.addEventListener('message', ({ data }) => {
    if (typeof data !== 'string') return;
    const msg = JSON.parse(data);
    if (msg.type === 'config') client.configs.set(msg.tracker_id, msg.location);
    if (msg.type === 'pos') client.poses.set(client.configs.get(msg.tracker_id), msg);
    if (msg.type === type) client.replies.push(msg);
  });
  await until(() => socket.readyState === WebSocket.OPEN && [...client.configs.values()].includes('left_foot'),
    'No stock computed tracker registrations after connecting');
  return client;
}

async function poll(client, slot) {
  client.socket.send(JSON.stringify({ type, slot }));
  await until(() => client.replies.length > 0, 'No ' + type + ' reply for slot ' + slot, 8000);
  const msg = client.replies.shift();
  assert.equal(msg.type, type);
  assert.equal(msg.slot, Number.isSafeInteger(slot) ? slot : 0);
  assert.equal(typeof msg.error, 'string');
  for (const foot of [msg.left, msg.right]) {
    assert.equal(typeof foot.id, 'string');
    assert.equal(typeof foot.valid, 'boolean');
    assert.ok(Number.isFinite(foot.ay));
    assert.ok(Number.isSafeInteger(foot.t) && foot.t >= 0);
  }
  return msg;
}

function assertFoot(foot, index) {
  assert.equal(foot.id, trackerIds[index]);
  assert.equal(foot.valid, true);
  assert.ok(Math.abs(foot.ay - accelerations[index]) < 0.002, 'Unexpected vertical acceleration ' + foot.ay);
  assert.ok(foot.t > 0);
}

function assertDenied(reply, code) {
  assert.equal(reply.error, code);
  for (const foot of [reply.left, reply.right]) {
    assert.deepEqual(foot, { id: '', valid: false, ay: 0, t: 0 });
  }
}

function sendHead(client, x = 0) {
  client.socket.send(JSON.stringify({ type: 'pos', tracker_id: 0, x, y: 1.45, z: 0, qx: 0, qy: 0, qz: 0, qw: 1 }));
}

async function checkSolver(ctx) {
  const solo = await connect(ctx.clients);
  async function solvedFoot() {
    for (let i = 0; i < 18; ++i) { sendHead(solo); await sleep(20); }
    for (const joint of ['hmd', 'chest', 'waist', 'left_knee', 'right_knee', 'left_foot', 'right_foot']) {
      const p = solo.poses.get(joint);
      assert.ok(p && [p.x, p.y, p.z, p.qx, p.qy, p.qz, p.qw].every(Number.isFinite), 'Missing pose for ' + joint);
    }
    return solo.poses.get('left_foot');
  }
  const before = await solvedFoot();
  ctx.boards[2].angle = 1.05;
  const after = await solvedFoot();
  const distance = Math.hypot(after.x - before.x, after.y - before.y, after.z - before.z);
  assert.ok(distance > 0.05, 'Rotating the left thigh did not move the stock solver foot');
  ctx.boards[2].angle = 0;
  console.log('PASS stock six-IMU body solver, left foot moved ' + distance.toFixed(3) + ' m, announced roles ' +
    [...solo.configs.values()].join(', '));
}

async function checkPairs(ctx) {
  const [one, two, three] = await Promise.all([connect(ctx.clients), connect(ctx.clients), connect(ctx.clients)]);
  const initial = await Promise.all([poll(one, 1), poll(two, 2), poll(three, 3)]);
  for (const [i, msg] of initial.entries()) {
    assert.equal(msg.error, '');
    assertFoot(msg.left, 2 * i);
    assertFoot(msg.right, 2 * i + 1);
  }
  const stranger = await connect(ctx.clients);
  assertDenied(await poll(stranger, 2), 'slot_in_use');
  assertDenied(await poll(one, 3), 'slot_locked');
  for (const bad of [0, 4, '2', 1.5, undefined]) assertDenied(await poll(stranger, bad), 'invalid_slot');
  sendHead(one, 50);
  sendHead(stranger, 100);
  await sleep(60);
  assert.equal(one.poses.size + stranger.poses.size, 0, 'A paired socket reached the shared HMD handler');
  console.log('PASS three simultaneous pairs, six hardware IDs, world-space signed acceleration, slot conflicts and HMD isolation');

  const a = ctx.boards[0];
  a.sendAcceleration = false;
  await sleep(30);
  const first = (await poll(one, 1)).left;
  assertFoot(first, 0);
  a.angle = 1.2;
  await sleep(40);
  assert.deepEqual((await poll(one, 1)).left, first, 'A rotation or request rewrote the last acceleration sample');
  await sleep(320);
  const stale = await poll(one, 1);
  assert.equal(stale.left.valid, false);
  assert.equal(stale.left.t, first.t);
  assertFoot(stale.right, 1);
  a.angle = 0;
  a.sendAcceleration = true;
  await until(async () => (await poll(one, 1)).left.t > first.t, 'Fresh acceleration did not recover');
  const b = ctx.boards[1];
  b.online = false;
  b.sendAcceleration = false;
  sensorInfo(b, 0, ctx.port);
  await until(async () => !(await poll(one, 1)).right.valid, 'Explicit sensor disconnect was ignored', 180);
  assertFoot((await poll(one, 1)).left, 0);
  sensorInfo(b, 1, ctx.port);
  b.online = true;
  await sleep(40);
  assert.equal((await poll(one, 1)).right.valid, false, 'A reconnect with only rotation revived pre-disconnect acceleration');
  b.sendAcceleration = true;
  await until(async () => (await poll(one, 1)).right.valid, 'Sensor did not recover after reconnect');
  console.log('PASS last-measurement timestamps, stale acceleration during live rotations, separate foot validity and tracker reconnect');

  const closed = once(two.socket, 'close');
  two.socket.close();
  await closed;
  await until(async () => (await poll(stranger, 2)).error === '', 'Disconnected headset did not release its slot');
  assertFoot((await poll(stranger, 2)).left, 2);
  const [contender, another] = await Promise.all([connect(ctx.clients), connect(ctx.clients)]);
  assertDenied(await poll(contender, 2), 'slot_in_use');
  await sleep(3250);
  const competing = await Promise.all([poll(contender, 2), poll(another, 2)]);
  assert.equal(competing.filter(reply => reply.error === '').length, 1);
  assertFoot(competing.find(reply => reply.error === '').right, 3);
  assertDenied(competing.find(reply => reply.error !== ''), 'slot_in_use');
  assertDenied(await poll(stranger, 2), 'slot_in_use');
  console.log('PASS immediate slot release, silent-owner expiry and exclusive reclaim');
}

async function checkEditedMapping(ctx) {
  const a = await connect(ctx.clients);
  const b = await connect(ctx.clients);
  const c = await connect(ctx.clients);
  const first = await poll(a, 1);
  assert.equal(first.error, '');
  assert.equal(first.left.valid, false, 'Two sensors named P1-L must be ambiguous');
  assert.equal(first.right.valid, false, 'Missing P1-R must be unavailable');
  const second = await poll(b, 2);
  assertFoot(second.left, 4);
  assertFoot(second.right, 5);
  const third = await poll(c, 3);
  assert.equal(third.left.valid, false, 'A stored duplicate on an unplugged board must remain ambiguous');
  assertFoot(third.right, 3);
  console.log('PASS persistent reassignment, unknown and duplicate labels, a configured offline duplicate, tracking without a body assignment');
}

async function runFixture(reassigned, exercise) {
  const dir = await mkdtemp(path.join(os.tmpdir(), 'slimevr-feet-'));
  const probe = dgram.createSocket('udp4');
  probe.bind(0, '127.0.0.1');
  await once(probe, 'listening');
  const port = probe.address().port;
  probe.close();
  const names = reassigned ? ['P1-L', 'P1-L', 'P3-L', 'P3-R', 'P2-L', 'P2-R'] : labels;
  const trackers = Object.fromEntries(trackerIds.map((id, i) =>
    [id, { customName: names[i], ...(reassigned ? {} : { designation: 'body:' + roles[i] }) }]));
  if (reassigned) trackers['udp://02:00:00:00:00:42/0'] = { customName: 'P3-L' };
  await mkdir(path.join(dir, 'config'));
  await writeFile(path.join(dir, 'vrconfig.yml'), JSON.stringify({ version: '15', server: { trackerPort: port }, knownDevices: macs, trackers }));
  const server = spawn(java, ['-Djava.awt.headless=true', '-jar', jar, 'run'], {
    cwd: dir, windowsHide: true, env: { ...process.env, APPDATA: dir, LOCALAPPDATA: dir },
  });
  let log = '';
  server.stdout.on('data', bytes => { log += bytes; });
  server.stderr.on('data', bytes => { log += bytes; });
  const exit = once(server, 'exit');
  const boards = macs.map((mac, i) => ({
    mac, socket: dgram.createSocket('udp4'), sequence: 0n, sensorId: sensorIds[i],
    angle: 0, ay: accelerations[i], online: true, sendAcceleration: true, combined: i % 2 === 1,
  }));
  const clients = [];
  let ticker, passed = false;
  try {
    await until(() => /Web Socket VR Bridge started on port 21110/i.test(log), 'Server failed to start', 12000);
    for (const board of boards.toReversed()) handshake(board, port);
    await until(() => (log.match(/Tracker \d+ connected from/g) ?? []).length >= 6, 'Six tracker boards did not connect');
    for (const board of boards) sensorInfo(board, 1, port);
    await until(() => (log.match(/Added sensor \d+ for udp:/g) ?? []).length >= 6, 'Six IMUs did not register');
    ticker = setInterval(() => boards.forEach(board => feed(board, port)), 16);
    await sleep(100);
    await exercise({ boards, clients, port });
    passed = true;
  } finally {
    clearInterval(ticker);
    for (const client of clients) client.socket.close();
    for (const board of boards) board.socket.close();
    if (server.exitCode === null) {
      server.stdin.end('exit\n');
      const timer = setTimeout(() => server.kill(), 2500);
      try { await exit; } finally { clearTimeout(timer); }
    }
    if (passed) {
      await rm(dir, { recursive: true });
    } else {
      await writeFile(path.join(dir, 'server.log'), log);
      console.error('Server log retained at ' + path.join(dir, 'server.log'));
    }
  }
}

await access(jar);
await access(java);
// The upstream WebSocket port is fixed. Only send to a server started by this test.
const guard = net.createServer();
guard.listen({ port: 21110, host: '0.0.0.0', exclusive: true });
await once(guard, 'listening');
await new Promise(resolve => guard.close(resolve));
if (stockOnly) {
  await runFixture(false, checkSolver);
} else {
  await runFixture(false, async ctx => { await checkPairs(ctx); await checkSolver(ctx); });
  await runFixture(true, checkEditedMapping);
}

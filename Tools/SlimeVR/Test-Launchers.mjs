// Boot the released CMD files with the real Electron main and bundled JRE in a disposable profile.
import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { once } from 'node:events';
import { cp, mkdir, mkdtemp, readFile, rename, rm, stat, writeFile } from 'node:fs/promises';
import net from 'node:net';
import os from 'node:os';
import path from 'node:path';
import { setTimeout as sleep } from 'node:timers/promises';
import { fileURLToPath } from 'node:url';

const release = path.resolve(process.argv[2] ?? fileURLToPath(new URL('../../Builds/SlimeVR/Paired', import.meta.url)));
const guard = net.createServer();
guard.listen({ port: 21110, host: '0.0.0.0', exclusive: true });
await once(guard, 'listening');
await new Promise(resolve => guard.close(resolve));
const scratch = await mkdtemp(path.join(os.tmpdir(), 'feet-launcher-'));
const appDir = path.join(scratch, 'Portable GUI');
const resources = path.join(appDir, 'resources');

async function until(test, why, ms = 20000) {
  const deadline = Date.now() + ms;
  while (!await test()) {
    assert.ok(Date.now() < deadline, why);
    await sleep(75);
  }
}

async function hasListener() {
  return new Promise(resolve => {
    const probe = net.createConnection(21110, '127.0.0.1');
    probe.once('error', () => resolve(false));
    probe.once('connect', () => { probe.destroy(); resolve(true); });
  });
}

async function isolateProfile() {
  await cp(release, appDir, { recursive: true });
  const upstream = path.join(resources, 'upstream');
  await mkdir(upstream);
  // Keep the official ASAR untouched. A test bootstrap sets appData before loading it.
  const upstreamAsar = path.join(upstream, 'app.asar');
  assert.ok(path.resolve(upstreamAsar).startsWith(scratch + path.sep));
  await rename(path.join(resources, 'app.asar'), upstreamAsar);
  await cp(path.join(resources, 'app.asar.unpacked'), path.join(upstream, 'app.asar.unpacked'), { recursive: true });
  const wrapper = path.join(resources, 'app');
  await mkdir(wrapper);
  await writeFile(path.join(wrapper, 'package.json'), JSON.stringify({ name: 'launcher-smoke', version: '1.0.0', main: 'main.cjs' }));
  await writeFile(path.join(wrapper, 'main.cjs'), `
const { app, dialog } = require('electron');
const fs = require('node:fs');
app.setPath('appData', process.env.FEET_TEST_PROFILE);
app.commandLine.appendSwitch('headless');
app.commandLine.appendSwitch('disable-gpu');
app.on('browser-window-created', (_event, win) => { win.setSkipTaskbar(true); win.on('show', () => win.hide()); });
const fail = error => { fs.writeFileSync(process.env.FEET_TEST_ERROR, String(error.stack || error)); app.exit(1); };
process.on('uncaughtException', fail);
process.on('unhandledRejection', fail);
dialog.showErrorBox = (title, message) => fail(new Error(title + ' ' + message));
const quit = setInterval(() => {
  if (fs.existsSync(process.env.FEET_TEST_QUIT)) { clearInterval(quit); app.quit(); }
}, 100);
setTimeout(() => app.quit(), 45000).unref();
const main = require('node:path').resolve(__dirname, '../upstream/app.asar/out/main/index.js');
import(require('node:url').pathToFileURL(main).href).catch(fail);
`);
}

async function runLauncher(name, paired) {
  const profile = path.join(scratch, name + '.profile');
  const user = path.join(scratch, name + '.user');
  const local = path.join(user, 'AppData', 'Local');
  const quit = path.join(scratch, name + '.quit');
  const errorFile = path.join(scratch, name + '.error');
  await mkdir(profile);
  await mkdir(local, { recursive: true });
  const cwd = paired ? appDir : path.join(appDir, 'Stock');
  await mkdir(path.join(cwd, 'config'));
  await writeFile(path.join(cwd, 'vrconfig.yml'), JSON.stringify({ version: '15', server: { trackerPort: 0 } }));
  const env = { ...process.env, APPDATA: profile, LOCALAPPDATA: local, USERPROFILE: user,
    FEET_TEST_PROFILE: profile, FEET_TEST_QUIT: quit, FEET_TEST_ERROR: errorFile };
  delete env.JAVA_HOME;
  const command = spawn(process.env.ComSpec, ['/d', '/c', name], { cwd: appDir, env, windowsHide: true, stdio: 'ignore' });
  assert.equal((await once(command, 'exit'))[0], 0);
  let socket;
  try {
    await until(async () => {
      if (await stat(errorFile).catch(() => false)) throw new Error(await readFile(errorFile, 'utf8'));
      return hasListener();
    }, name + ' failed to start the JAR');
    socket = new WebSocket('ws://127.0.0.1:21110');
    let hasHead = false, hasPose = false, feet;
    socket.addEventListener('message', ({ data }) => {
      if (typeof data !== 'string') return;
      const p = JSON.parse(data);
      if (p.location === 'hmd') hasHead = true;
      if (p.type === 'pos') hasPose = true;
      if (p.type === 'blacktheon.feet.v1') feet = p;
    });
    await until(() => hasHead, name + ' did not advertise the stock HMD');
    socket.send(JSON.stringify({ type: 'blacktheon.feet.v1', slot: 1 }));
    if (paired) {
      await until(() => feet, name + ' selected a stock JAR');
      assert.equal(feet.error, '');
      assert.equal(feet.left.valid, false);
    } else {
      socket.send(JSON.stringify({ type: 'pos', tracker_id: 0, x: 0, y: 1.45, z: 0, qx: 0, qy: 0, qz: 0, qw: 1 }));
      await until(() => hasPose, name + ' did not respond to a stock HMD pose');
      assert.equal(feet, undefined, name + ' selected a patched JAR');
    }
    console.log('PASS bundled Electron GUI and JRE launched the expected server via ' + name);
  } finally {
    socket?.close();
    await writeFile(quit, 'quit');
    await until(async () => !await hasListener(), name + ' did not stop its server');
  }
}

try {
  await isolateProfile();
  await runLauncher('Start-Stock-Solo.cmd', false);
  await runLauncher('Start-Paired.cmd', true);
  assert.ok(path.dirname(scratch) === os.tmpdir() && path.basename(scratch).startsWith('feet-launcher-'));
  await rm(scratch, { recursive: true, maxRetries: 10, retryDelay: 250 });
} catch (error) {
  console.error('Disposable GUI profile retained at ' + scratch);
  throw error;
}

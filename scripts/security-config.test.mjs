import assert from 'node:assert/strict';
import { readFile, readdir } from 'node:fs/promises';
import test from 'node:test';

const root = new URL('../', import.meta.url);
const tracked = [
  ...await filesUnder('BACKEND/API'),
  ...await filesUnder('APPWEB/src'),
  ...await filesUnder('APPMOBILE/src'),
  ...await filesUnder('APPMOBILE/public'),
  'APPWEB/.env.example',
  'BACKEND/API/appsettings.json',
  'BACKEND/API/appsettings.Development.example.json',
].filter((path, index, all) => all.indexOf(path) === index);

test('tracked application sources and configuration contain no private PEM key', async () => {
  const targets = tracked.filter((path) => /^(BACKEND\/API\/.*\.(cs|json)|APPWEB\/src\/|APPMOBILE\/src\/|APPMOBILE\/public\/|APPWEB\/\.env\.example)/.test(path));
  for (const path of targets) {
    const content = await readFile(new URL(path, root), 'utf8');
    assert.doesNotMatch(content, /-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----/, `${path} contains a private key block`);
  }
});

test('sample and local application settings do not store operational credential values', async () => {
  const targets = tracked.filter((path) => /(?:appsettings(?:\.[^/]*)?\.json|\.env(?:\.[^/]*)?\.example)$/.test(path));
  const placeholders = /(?:REEMPLAZA_|TU_CLAVE|example|placeholder|your[-_ ]|test[-_ ]|qa[-_ ]|\$\{|^$)/i;
  const credentialAssignment = /(?:password|api[_-]?key|signingsecret|bootstrapsecret|jwt__key|smtp__password|sms__apikey)\s*["']?\s*[:=]\s*["']?([^"'\s,;}]+)/ig;
  for (const path of targets) {
    const content = await readFile(new URL(path, root), 'utf8');
    for (const match of content.matchAll(credentialAssignment)) {
      assert.match(match[1], placeholders, `${path} has a non-placeholder credential assignment`);
    }
  }
});

test('frontend build configuration exposes public URLs only, never server credentials', async () => {
  const targets = tracked.filter((path) => /^(\.env\.example|vite\.config\.[^/]+|APPMOBILE\/vite\.config\.js|APPMOBILE\/\.env\.example)$/.test(path));
  const forbidden = /VITE_[A-Z0-9_]*(?:JWT|SIGNING|QR_SECRET|SMTP|PASSWORD|API_KEY|SMS_SECRET|BOOTSTRAP)/i;
  for (const path of targets) {
    const content = await readFile(new URL(path, root), 'utf8');
    assert.doesNotMatch(content, forbidden, `${path} exposes a server secret through Vite`);
  }
});

test('gitignore excludes env files and private certificate/key files while keeping the example template', async () => {
  const ignore = await readFile(new URL('../.gitignore', import.meta.url), 'utf8');
  for (const rule of ['.env', '.env.*', '!.env.example', '*.pfx', '*.p12', '*.key', '*.pem', 'secrets/']) assert.ok(ignore.includes(rule), `Missing ignore rule ${rule}`);
  assert.equal(isIgnored(ignore, '.env.example'), false, '.env.example must remain trackable');
  for (const filename of ['.env', '.env.production', 'qa.pfx', 'qa.p12', 'tls.key', 'tls.pem']) assert.equal(isIgnored(ignore, filename), true, `${filename} must be ignored`);
});

async function filesUnder(directory) {
  const results = [];
  for (const entry of await readdir(new URL(`../${directory}`, import.meta.url), { withFileTypes: true })) {
    if (entry.isDirectory() && ['bin', 'obj', 'node_modules', 'dist', 'test-results', '.git'].includes(entry.name)) continue;
    const relative = `${directory}/${entry.name}`;
    if (entry.isDirectory()) results.push(...await filesUnder(relative));
    else if (entry.isFile()) results.push(relative);
  }
  return results;
}

function isIgnored(ignore, filename) {
  let ignored = false;
  for (const raw of ignore.split(/\r?\n/)) {
    const rule = raw.trim();
    if (!rule || rule.startsWith('#')) continue;
    if (rule === filename || (rule === '.env.*' && /^\.env\..+$/.test(filename)) || (rule.startsWith('*.') && filename.endsWith(rule.slice(1)))) ignored = !rule.startsWith('!');
    if (rule.startsWith('!') && rule.slice(1) === filename) ignored = false;
  }
  return ignored;
}

import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { runInNewContext } from 'node:vm';
import test from 'node:test';

const source = await readFile(new URL('./sw.js', import.meta.url), 'utf8');

async function dispatchFetch(request, { offline = false, cached = { source: 'cache' }, cacheControl = '' } = {}) {
  let fetchHandler;
  let cacheLookups = 0;
  const cacheWrites = [];
  const waitUntilPromises = [];
  const networkRequests = [];
  const cache = {
    addAll: async () => {},
    put: async (key, value) => { cacheWrites.push({ key, value }); },
  };
  const self = {
    location: { origin: 'http://localhost:5174' },
    addEventListener: (name, handler) => { if (name === 'fetch') fetchHandler = handler; },
    skipWaiting: () => {},
    clients: { claim: async () => {} },
  };
  const caches = {
    match: async () => { cacheLookups++; return cached; },
    open: async () => cache,
    keys: async () => [],
    delete: async () => true,
  };
  const fetch = async (value) => {
    networkRequests.push(value);
    if (offline) throw new TypeError('offline');
    return {
      source: 'network', ok: true, type: 'basic',
      headers: { get: () => cacheControl }, clone() { return this; },
    };
  };
  runInNewContext(source, { self, caches, fetch, URL, Promise });
  let response;
  fetchHandler({
    request,
    respondWith: (promise) => { response = promise; },
    waitUntil: (promise) => waitUntilPromises.push(promise),
  });
  const result = await response;
  await Promise.all(waitUntilPromises);
  return { result, cacheLookups, cacheWrites, networkRequests };
}

test('service worker never reads or serves API responses from Cache API', async () => {
  for (const pathname of ['/api', '/api/login', '/api/tickets', '/api/login/refresh']) {
    const request = { method: 'GET', url: `http://localhost:5174${pathname}`, headers: new Headers() };
    const result = await dispatchFetch(request);
    assert.equal(result.result.source, 'network');
    assert.equal(result.cacheLookups, 0);
    assert.equal(result.cacheWrites.length, 0);
    assert.deepEqual(result.networkRequests, [request]);
  }
});

test('service worker never caches writes, including authenticated dispatch requests', async () => {
  const request = { method: 'POST', url: 'http://localhost:5174/api/despachos', headers: new Headers({ Authorization: 'Bearer TEST ONLY' }) };
  const result = await dispatchFetch(request);
  assert.equal(result.result.source, 'network');
  assert.equal(result.cacheLookups, 0);
  assert.equal(result.cacheWrites.length, 0);
});

test('service worker does not cache cross-origin requests', async () => {
  const request = { method: 'GET', url: 'https://api.example.test/private', headers: new Headers() };
  const result = await dispatchFetch(request);
  assert.equal(result.result.source, 'network');
  assert.equal(result.cacheLookups, 0);
  assert.equal(result.cacheWrites.length, 0);
});

test('service worker fetches static GET assets before updating their cache', async () => {
  const request = { method: 'GET', url: 'http://localhost:5174/index.html', headers: new Headers() };
  const result = await dispatchFetch(request);
  assert.equal(result.result.source, 'network');
  assert.equal(result.cacheLookups, 0);
  assert.equal(result.cacheWrites.length, 1);
  assert.deepEqual(result.networkRequests, [request]);
});

test('service worker retains cached static assets when the network is offline', async () => {
  const request = { method: 'GET', url: 'http://localhost:5174/index.html', headers: new Headers() };
  const result = await dispatchFetch(request, { offline: true });
  assert.equal(result.result.source, 'cache');
  assert.equal(result.cacheLookups, 1);
  assert.deepEqual(result.networkRequests, [request]);
});

test('service worker does not cache authenticated static-looking requests', async () => {
  const request = { method: 'GET', url: 'http://localhost:5174/private.json', headers: new Headers({ Authorization: 'Bearer TEST ONLY' }) };
  const result = await dispatchFetch(request);
  assert.equal(result.result.source, 'network');
  assert.equal(result.cacheLookups, 0);
  assert.equal(result.cacheWrites.length, 0);
});

test('service worker does not cache private static responses', async () => {
  const request = { method: 'GET', url: 'http://localhost:5174/index.html', headers: new Headers() };
  const result = await dispatchFetch(request, { cacheControl: 'private, max-age=60' });
  assert.equal(result.result.source, 'network');
  assert.equal(result.cacheWrites.length, 0);
});

test('service worker activates immediately and removes legacy static caches', async () => {
  const handlers = new Map();
  const deleted = [];
  let skippedWaiting = false;
  let claimedClients = false;
  const self = {
    location: { origin: 'http://localhost:5174' },
    addEventListener: (name, handler) => handlers.set(name, handler),
    skipWaiting: async () => { skippedWaiting = true; },
    clients: { claim: async () => { claimedClients = true; } },
  };
  const caches = {
    open: async () => ({ addAll: async () => {} }),
    keys: async () => ['despacho-cache-v1', 'despacho-static-v1', 'unrelated-cache'],
    delete: async (key) => { deleted.push(key); },
  };
  runInNewContext(source, { self, caches, URL, Promise });

  let installPromise;
  handlers.get('install')({ waitUntil: (promise) => { installPromise = promise; } });
  await installPromise;
  assert.equal(skippedWaiting, true);

  let activatePromise;
  handlers.get('activate')({ waitUntil: (promise) => { activatePromise = promise; } });
  await activatePromise;
  assert.deepEqual(deleted, ['despacho-cache-v1', 'despacho-static-v1']);
  assert.equal(claimedClients, true);
});

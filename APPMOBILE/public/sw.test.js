import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { runInNewContext } from 'node:vm';
import test from 'node:test';

const source = await readFile(new URL('./sw.js', import.meta.url), 'utf8');

async function dispatchFetch(request) {
  let fetchHandler;
  let cacheLookups = 0;
  const networkRequests = [];
  const cached = { source: 'cache' };
  const self = {
    location: { origin: 'http://localhost:5174' },
    addEventListener: (name, handler) => { if (name === 'fetch') fetchHandler = handler; },
    skipWaiting: () => {},
    clients: { claim: async () => {} },
  };
  const caches = {
    match: async () => { cacheLookups++; return cached; },
    open: async () => ({ addAll: async () => {} }),
    keys: async () => [],
    delete: async () => true,
  };
  const fetch = async (value) => { networkRequests.push(value); return { source: 'network' }; };
  runInNewContext(source, { self, caches, fetch, URL, Promise });
  let response;
  fetchHandler({ request, respondWith: (promise) => { response = promise; } });
  return { result: await response, cacheLookups, networkRequests };
}

test('service worker never reads or serves API responses from Cache API', async () => {
  for (const pathname of ['/api/login', '/api/tickets', '/api/login/refresh']) {
    const request = { method: 'GET', url: `http://localhost:5174${pathname}` };
    const result = await dispatchFetch(request);
    assert.equal(result.result.source, 'network');
    assert.equal(result.cacheLookups, 0);
    assert.deepEqual(result.networkRequests, [request]);
  }
});

test('service worker never caches writes, including authenticated dispatch requests', async () => {
  const request = { method: 'POST', url: 'http://localhost:5174/api/despachos', headers: { Authorization: 'Bearer TEST ONLY' } };
  const result = await dispatchFetch(request);
  assert.equal(result.result.source, 'network');
  assert.equal(result.cacheLookups, 0);
});

test('service worker does not cache cross-origin requests', async () => {
  const request = { method: 'GET', url: 'https://api.example.test/private' };
  const result = await dispatchFetch(request);
  assert.equal(result.result.source, 'network');
  assert.equal(result.cacheLookups, 0);
});

test('service worker can serve static GET assets from cache', async () => {
  const request = { method: 'GET', url: 'http://localhost:5174/index.html' };
  const result = await dispatchFetch(request);
  assert.equal(result.result.source, 'cache');
  assert.equal(result.cacheLookups, 1);
  assert.deepEqual(result.networkRequests, []);
});

import { beforeEach, test } from 'node:test';
import assert from 'node:assert/strict';
import { escapeHtml, safeClassName } from './html.js';

const storage = new Map();
globalThis.localStorage = {
  getItem: (key) => storage.get(key) ?? null,
  setItem: (key, value) => storage.set(key, value),
  removeItem: (key) => storage.delete(key),
  clear: () => storage.clear(),
};
const sessionStore = new Map();
globalThis.sessionStorage = {
  getItem: (key) => sessionStore.get(key) ?? null,
  setItem: (key, value) => sessionStore.set(key, value),
  removeItem: (key) => sessionStore.delete(key),
  clear: () => sessionStore.clear(),
};
globalThis.window = { dispatchEvent: () => {} };

const { consultarTickets, registrarDespacho, validarTicket } = await import('./ticketService.js');
const { SESSION_KEY, login, logout } = await import('./sessionService.js');

function guardarSesion({ token = 'qa-token', refreshToken = 'qa-refresh', expiresAt = '2035-01-01T00:00:00Z' } = {}) {
  sessionStorage.setItem(SESSION_KEY, JSON.stringify({
    token, refreshToken, expiresAt, id: 42, nombre: 'Operador QA', rol: 'DESPACHADOR',
  }));
}

beforeEach(() => {
  storage.clear();
  sessionStorage.clear();
  guardarSesion();
});

test('el despacho móvil confirma identidad y envía el contrato requerido por la API', async () => {
  const previousFetch = globalThis.fetch;
  let request;
  globalThis.fetch = async (url, options) => {
    request = { url, options };
    return new Response(JSON.stringify({ ok: true, mensaje: 'Despacho registrado' }), { status: 200 });
  };
  try {
    const result = await registrarDespacho('COM-2026-000001', 5, 'QA', true);
    assert.equal(result.ok, true);
    assert.equal(request.url, '/api/despachos');
    assert.deepEqual(JSON.parse(request.options.body), {
      ticketId: 'COM-2026-000001', galonesServidos: 5, observaciones: 'QA', identidadConfirmada: true, tanqueId: null,
    });
  } finally { globalThis.fetch = previousFetch; }
});

test('el servicio móvil muestra el conflicto HTTP devuelto por el API', async () => {
  const previousFetch = globalThis.fetch;
  globalThis.fetch = async () => new Response('El ticket ya fue despachado.', { status: 409 });
  try {
    await assert.rejects(registrarDespacho('COM-2026-000001', 5, '', true), /El ticket ya fue despachado/);
  } finally { globalThis.fetch = previousFetch; }
});

test('la lista móvil conserva el formato actual de tickets de la API', async () => {
  const previousFetch = globalThis.fetch;
  globalThis.fetch = async () => new Response(JSON.stringify([{
    id: 'a431ba46-68c8-49b3-9fa3-cfa3b506ea4a', numeroSecuencial: 'COM-2026-000001', estado: 'CREADO',
    vehiculo: 'QA-0001', cantidadAutorizadaGalones: 8, fechaVencimiento: '2026-10-01T00:00:00',
  }]), { status: 200 });
  try {
    const [ticket] = await consultarTickets();
    assert.equal(ticket.id, 'a431ba46-68c8-49b3-9fa3-cfa3b506ea4a');
    assert.equal(ticket.numeroSecuencial, 'COM-2026-000001');
    assert.equal(ticket.cantidadAutorizadaGalones, 8);
  } finally { globalThis.fetch = previousFetch; }
});

test('el login móvil envía credenciales y devuelve la sesión de la API', async () => {
  const previousFetch = globalThis.fetch;
  let request;
  globalThis.fetch = async (url, options) => {
    request = { url, options };
    return new Response(JSON.stringify({
      token: 'signed-token', refreshToken: 'signed-refresh', expiresAt: '2035-01-01T00:00:00Z',
      id: 73, nombre: 'Operador de prueba', rol: 'DESPACHADOR',
    }), { status: 200 });
  };
  try {
    const sesion = {
      token: 'signed-token', refreshToken: 'signed-refresh', expiresAt: '2035-01-01T00:00:00Z',
      id: 73, nombre: 'Operador de prueba', rol: 'DESPACHADOR',
    };
    assert.deepEqual(await login('operador.qa', 'secreto'), sesion);
    assert.equal(request.url, '/api/login');
    assert.deepEqual(JSON.parse(request.options.body), { usuario: 'operador.qa', contrasena: 'secreto' });
    assert.deepEqual(JSON.parse(sessionStorage.getItem(SESSION_KEY)), sesion);
  } finally { globalThis.fetch = previousFetch; }
});

test('la PWA renueva el token y reintenta una sola vez la operación original', async () => {
  const previousFetch = globalThis.fetch;
  guardarSesion({ token: 'old-access', refreshToken: 'old-refresh' });
  const calls = [];
  globalThis.fetch = async (url, options) => {
    calls.push({ url, options });
    if (url === '/api/tickets') return calls.filter((call) => call.url === url).length === 1
      ? new Response('', { status: 401 })
      : new Response('[]', { status: 200 });
    return new Response(JSON.stringify({ token: 'new-access', refreshToken: 'new-refresh', expiresAt: '2035-01-01T00:00:00Z' }), { status: 200 });
  };
  try {
    assert.deepEqual(await consultarTickets(), []);
    assert.equal(calls.filter((call) => call.url === '/api/login/refresh').length, 1);
    assert.equal(calls[2].options.headers.get('Authorization'), 'Bearer new-access');
    assert.equal(JSON.parse(sessionStorage.getItem(SESSION_KEY)).refreshToken, 'new-refresh');
  } finally { globalThis.fetch = previousFetch; }
});

test('la PWA comparte un refresh entre solicitudes concurrentes del mismo cliente', async () => {
  const previousFetch = globalThis.fetch;
  guardarSesion({ token: 'concurrent-old-access', refreshToken: 'concurrent-old-refresh' });
  let protectedCalls = 0;
  let refreshCalls = 0;
  globalThis.fetch = async (url) => {
    if (url === '/api/login/refresh') {
      refreshCalls++;
      await Promise.resolve();
      return new Response(JSON.stringify({ token: 'concurrent-new-access', refreshToken: 'concurrent-new-refresh', expiresAt: '2035-01-01T00:00:00Z' }), { status: 200 });
    }
    protectedCalls++;
    return protectedCalls <= 2 ? new Response('', { status: 401 }) : new Response('[]', { status: 200 });
  };
  try {
    assert.deepEqual(await Promise.all([consultarTickets(), consultarTickets()]), [[], []]);
    assert.equal(refreshCalls, 1);
    assert.equal(protectedCalls, 4);
  } finally { globalThis.fetch = previousFetch; }
});

test('refresh fallido limpia la sesión móvil y no repite la operación protegida', async () => {
  const previousFetch = globalThis.fetch;
  guardarSesion({ token: 'old-access', refreshToken: 'revoked' });
  let protectedCalls = 0;
  globalThis.fetch = async (url) => {
    if (url === '/api/tickets') { protectedCalls++; return new Response('', { status: 401 }); }
    return new Response(JSON.stringify({ mensaje: 'sesión inválida' }), { status: 401 });
  };
  try {
    await assert.rejects(consultarTickets(), /Sesión inválida o expirada/i);
    assert.equal(protectedCalls, 1);
    assert.equal(sessionStorage.getItem(SESSION_KEY), null);
  } finally { globalThis.fetch = previousFetch; }
});

test('logout PWA revoca en servidor y elimina solo los campos de sesión', async () => {
  const previousFetch = globalThis.fetch;
  guardarSesion({ token: 'logout-access', refreshToken: 'logout-refresh' });
  storage.set('nombre', 'Operador');
  storage.set('rol', 'DESPACHADOR');
  storage.set('preferencia-ui', 'compacta');
  let request;
  globalThis.fetch = async (url, options) => { request = { url, options }; return new Response(null, { status: 204 }); };
  try {
    await logout();
    assert.equal(request.url, '/api/login/logout');
    assert.deepEqual(JSON.parse(request.options.body), { refreshToken: 'logout-refresh' });
    assert.equal(sessionStorage.getItem(SESSION_KEY), null);
    assert.equal(storage.get('preferencia-ui'), 'compacta');
  } finally { globalThis.fetch = previousFetch; }
});

test('los valores del API quedan como texto inerte antes de insertarse en HTML móvil', () => {
  assert.equal(escapeHtml('<script>alert("x")</script> & \'test\''), '&lt;script&gt;alert(&quot;x&quot;)&lt;/script&gt; &amp; &#39;test&#39;');
  assert.equal(safeClassName('CREADO" onmouseover="alert(1)'), 'creadoonmouseoveralert1');
});

test('la validación móvil reenvía el QR y conserva los estados inválido, consumido, vencido y anulado', async (t) => {
  const previousFetch = globalThis.fetch;
  const outcomes = [
    { valido: false, estado: 'INVALIDO' },
    { valido: false, estado: 'CONSUMIDO' },
    { valido: false, estado: 'VENCIDO' },
    { valido: false, estado: 'ANULADO' },
  ];
  globalThis.fetch = async (_url, options) => {
    assert.deepEqual(JSON.parse(options.body), { qrData: 'qr.payload' });
    return new Response(JSON.stringify(outcomes.shift()), { status: 200 });
  };
  try {
    for (const estado of ['INVALIDO', 'CONSUMIDO', 'VENCIDO', 'ANULADO']) {
      await t.test(`preserva ${estado}`, async () => {
        const result = await validarTicket('qr.payload');
        assert.equal(result.valido, false);
        assert.equal(result.estado, estado);
      });
    }
  } finally { globalThis.fetch = previousFetch; }
});

test('el servicio móvil traduce JSON de error HTTP y fallos de red', async (t) => {
  const previousFetch = globalThis.fetch;
  try {
    globalThis.fetch = async () => new Response(JSON.stringify({ title: 'Solicitud inválida' }), { status: 400 });
    await t.test('error HTTP JSON', async () => {
      await assert.rejects(validarTicket('qr'), /Solicitud inválida/);
    });

    globalThis.fetch = async () => { throw new TypeError('Failed to fetch'); };
    await t.test('error de red', async () => {
      await assert.rejects(consultarTickets(), /Failed to fetch/);
    });
  } finally { globalThis.fetch = previousFetch; }
});

test('el despacho bloquea una identidad no confirmada antes de llamar al API', async () => {
  const previousFetch = globalThis.fetch;
  let llamadas = 0;
  globalThis.fetch = async () => { llamadas++; return new Response(null, { status: 400 }); };
  try {
    await assert.rejects(registrarDespacho('COM-2026-000001', 5, '', false), /Confirma que has verificado la identidad/);
    assert.equal(llamadas, 0);
  } finally { globalThis.fetch = previousFetch; }
});

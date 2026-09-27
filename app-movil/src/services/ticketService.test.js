import test from 'node:test';
import assert from 'node:assert/strict';
import { consultarTickets, login, registrarDespacho, validarTicket } from './ticketService.js';

globalThis.localStorage = { getItem: (key) => key === 'token' ? 'qa-token' : null };

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
      ticketId: 'COM-2026-000001', galonesServidos: 5, observaciones: 'QA', identidadConfirmada: true,
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

test('la lista móvil adapta el formato de tickets de la API web', async () => {
  const previousFetch = globalThis.fetch;
  globalThis.fetch = async () => new Response(JSON.stringify([{
    id: 'a431ba46-68c8-49b3-9fa3-cfa3b506ea4a', numeroSecuencial: 'COM-2026-000001', estado: 'CREADO',
    vehiculo: 'QA-0001', cantidadAutorizadaGalones: 8, fechaVencimiento: '2026-10-01T00:00:00',
  }]), { status: 200 });
  try {
    const [ticket] = await consultarTickets();
    assert.equal(ticket.id, 'COM-2026-000001');
    assert.equal(ticket.uuid, 'a431ba46-68c8-49b3-9fa3-cfa3b506ea4a');
    assert.equal(ticket.cantidadAutorizada, 8);
  } finally { globalThis.fetch = previousFetch; }
});

test('el login móvil envía credenciales y devuelve la sesión de la API', async () => {
  const previousFetch = globalThis.fetch;
  let request;
  globalThis.fetch = async (url, options) => {
    request = { url, options };
    return new Response(JSON.stringify({ token: 'signed-token', rol: 'DESPACHADOR' }), { status: 200 });
  };
  try {
    assert.deepEqual(await login('operador.qa', 'secreto'), { token: 'signed-token', rol: 'DESPACHADOR' });
    assert.equal(request.url, '/api/login');
    assert.deepEqual(JSON.parse(request.options.body), { usuario: 'operador.qa', contrasena: 'secreto' });
  } finally { globalThis.fetch = previousFetch; }
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
    globalThis.fetch = async () => new Response(JSON.stringify({ title: 'No autorizado' }), { status: 401 });
    await t.test('error HTTP JSON', async () => {
      await assert.rejects(validarTicket('qr'), /No autorizado/);
    });

    globalThis.fetch = async () => { throw new TypeError('Failed to fetch'); };
    await t.test('error de red', async () => {
      await assert.rejects(consultarTickets(), /Failed to fetch/);
    });
  } finally { globalThis.fetch = previousFetch; }
});

test('el despacho transmite identidad no confirmada sin alterarla', async () => {
  const previousFetch = globalThis.fetch;
  let payload;
  globalThis.fetch = async (_url, options) => {
    payload = JSON.parse(options.body);
    return new Response(JSON.stringify({ title: 'La identidad debe confirmarse.' }), { status: 400 });
  };
  try {
    await assert.rejects(registrarDespacho('COM-2026-000001', 5, '', false), /La identidad debe confirmarse/);
    assert.equal(payload.identidadConfirmada, false);
  } finally { globalThis.fetch = previousFetch; }
});

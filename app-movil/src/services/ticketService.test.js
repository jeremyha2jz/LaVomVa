import test from 'node:test';
import assert from 'node:assert/strict';
import { consultarTickets, registrarDespacho } from './ticketService.js';

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

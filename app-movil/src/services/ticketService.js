// src/services/ticketService.js
import { fetchConSesion } from './sessionService.js';



import { API_URL } from './apiConfig.js';
export { login } from './sessionService.js';

// Lee la respuesta de forma segura: si es JSON la parsea, si no, usa el texto
// plano como mensaje de error. Sin esto, un 409/400 en texto rompía con
// "Unexpected token" al intentar res.json() directo.
async function leerRespuesta(res) {
  const texto = await res.text();
  let datos;
  try {
    datos = texto ? JSON.parse(texto) : {};
  } catch {
    datos = { mensaje: texto || "Error desconocido del servidor" };
  }

  if (!res.ok) {
    throw Object.assign(new Error(datos?.mensajeError || datos?.mensaje || datos?.error || datos?.detail || datos?.title || `Error ${res.status}`), { status: res.status });
  }
  return datos;
}

export async function validarTicket(qrData) {
  const res = await fetchConSesion(`${API_URL}/tickets/validar`, {
    method: "POST",
    headers: {
      "Content-Type": "application/json"
    },
    body: JSON.stringify({ qrData })
  });
  const resultado = await leerRespuesta(res);
  if (typeof resultado?.valido !== 'boolean' || (resultado.valido && !resultado.ticket)) {
    throw new Error('Respuesta de validación incompleta del servidor');
  }
  // Conservar el resultado completo, incluido ticketUuid, sin transformar el QR.
  return resultado;
}

export async function obtenerTanquesDespacho(ticket) {
  const detalle = ticket.tipoCombustibleId != null ? ticket :
    ticket.ticketUuid ? await leerRespuesta(await fetchConSesion(
      API_URL + '/tickets/' + encodeURIComponent(ticket.ticketUuid)
    )) : null;
  const combustibleId = detalle?.tipoCombustibleId;
  if (combustibleId == null) throw new Error('No se pudo determinar el combustible del ticket. Vuelve a escanear el QR.');
  const tanques = await leerRespuesta(await fetchConSesion(API_URL + '/catalogos/tanques'));
  if (!Array.isArray(tanques)) throw new Error('Respuesta de tanques incompleta del servidor');
  return tanques.filter(t => t.activo === true &&
    String(t.tipoCombustibleId) === String(combustibleId) && Number.isSafeInteger(t.id) && t.id > 0);
}

export async function registrarDespacho(ticketId, galonesServidos, observaciones, identidadConfirmada, tanqueId) {
  if (identidadConfirmada !== true) throw new Error('Confirma que has verificado la identidad.');
  if (!Number.isFinite(galonesServidos) || galonesServidos <= 0) throw new Error('La cantidad de galones debe ser mayor a 0');
  const res = await fetchConSesion(API_URL + '/despachos', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ ticketId, galonesServidos, identidadConfirmada,
      observaciones: observaciones?.trim() || null, tanqueId: tanqueId ?? null })
  });
  const resultado = await leerRespuesta(res);
  if (resultado?.ok !== true) throw new Error(resultado?.mensajeError || resultado?.mensaje || 'El servidor no confirm? el registro del despacho.');
  return resultado;
}

export async function consultarTickets() {
  const res = await fetchConSesion(API_URL + '/tickets');
  const tickets = await leerRespuesta(res);
  if (!Array.isArray(tickets)) throw new Error('Respuesta de tickets incompleta del servidor');
  return tickets;
}

export async function consultarDetalleTicket(id) {
  if (!id) throw new Error('El ticket no tiene un identificador disponible.');
  const res = await fetchConSesion(API_URL + '/tickets/' + encodeURIComponent(id));
  const ticket = await leerRespuesta(res);
  if (!ticket || typeof ticket !== 'object' || Array.isArray(ticket) || !ticket.id) {
    throw new Error('Respuesta de detalle incompleta del servidor');
  }
  return ticket;
}

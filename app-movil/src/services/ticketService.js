// src/services/ticketService.js
import { usuarioMock, ticketsMock, listaTicketsMock } from './mockData.js';

const USE_MOCK = false;

// La PWA y la API se publican juntas mediante el proxy de Vite.
const API_URL = "/api";

export async function login(usuario, contrasena) {
  if (USE_MOCK) {
    await simularRetraso();
    if (usuario === usuarioMock.usuario && contrasena === usuarioMock.contrasena) {
      return { token: usuarioMock.token, nombre: usuarioMock.nombre, rol: usuarioMock.rol };
    }
    throw new Error("Usuario o contraseña incorrectos");
  }

  const res = await fetch(`${API_URL}/login`, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      "ngrok-skip-browser-warning": "true"
    },
    body: JSON.stringify({ usuario, contrasena })
  });
  return leerRespuesta(res);
}

export async function validarTicket(qrData) {
  if (USE_MOCK) {
    await simularRetraso();
    const indice = Math.floor(Math.random() * ticketsMock.length);
    return ticketsMock[indice];
  }

  const res = await fetch(`${API_URL}/tickets/validar`, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      "Authorization": `Bearer ${localStorage.getItem("token")}`,
      "ngrok-skip-browser-warning": "true"
    },
    body: JSON.stringify({ qrData })
  });
  return leerRespuesta(res);
}

export async function registrarDespacho(ticketId, galonesServidos, observaciones, identidadConfirmada) {
  if (USE_MOCK) {
    await simularRetraso();
    console.log("Despacho simulado:", { ticketId, galonesServidos, observaciones });
    return { ok: true, mensaje: "Despacho registrado" };
  }

  const res = await fetch(`${API_URL}/despachos`, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      "Authorization": `Bearer ${localStorage.getItem("token")}`,
      "ngrok-skip-browser-warning": "true"
    },
    body: JSON.stringify({ ticketId, galonesServidos, observaciones, identidadConfirmada })
  });
  return leerRespuesta(res);
}

export async function consultarTickets() {
  if (USE_MOCK) {
    await simularRetraso();
    return listaTicketsMock;
  }

  const res = await fetch(`${API_URL}/tickets`, {
    headers: {
      "Authorization": `Bearer ${localStorage.getItem("token")}`,
      "ngrok-skip-browser-warning": "true"
    }
  });
  const tickets = await leerRespuesta(res);
  return tickets.map(ticket => ({
    ...ticket,
    id: ticket.numeroSecuencial ?? ticket.id,
    uuid: ticket.id,
    cantidadAutorizada: ticket.cantidadAutorizadaGalones ?? ticket.cantidadAutorizada,
  }));
}

async function leerRespuesta(res) {
  const raw = await res.text();
  let body;
  try { body = raw ? JSON.parse(raw) : null; }
  catch { body = raw; }
  if (!res.ok) {
    const message = typeof body === "string" ? body : body?.mensaje ?? body?.title ?? body?.error;
    throw new Error(message || `Error HTTP ${res.status}`);
  }
  return body;
}

function simularRetraso() {
  return new Promise(resolve => setTimeout(resolve, 300));
}

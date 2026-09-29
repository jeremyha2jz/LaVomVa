// src/services/ticketService.js
import { listaTicketsMock } from './mockData.js';
import { fetchConSesion } from './sessionService.js';

const USE_MOCK = true;

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

export async function registrarDespacho(ticketId, galonesServidos, observaciones) {
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
    body: JSON.stringify({ ticketId, galonesServidos, observaciones })
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
  return leerRespuesta(res);
}

function simularRetraso() {
  return new Promise(resolve => setTimeout(resolve, 300));
}

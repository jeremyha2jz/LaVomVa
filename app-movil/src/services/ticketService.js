// src/services/ticketService.js
import { usuarioMock, ticketsMock, listaTicketsMock } from './mockData.js';

const USE_MOCK = true;

const API_URL = "https://tributary-irritate-subtype.ngrok-free.dev/api";

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
    throw new Error(datos.mensaje || datos.error || `Error ${res.status}`);
  }
  return datos;
}

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
// src/services/ticketService.js
import { usuarioMock, ticketsMock, listaTicketsMock } from './mockData.js';

// Bandera central: true = usa datos falsos, false = llamaría al backend real.
// El día que el backend esté listo, cambias esto a false y ajustas
// las funciones de abajo para que hagan fetch() en vez de devolver el mock.
const USE_MOCK = true;

// URL base del backend real (la usarás cuando USE_MOCK sea false)
const API_URL = "https://tu-backend-aqui.com/api";

export async function login(usuario, contrasena) {
  if (USE_MOCK) {
    // Simula la espera de una red real, para que tu UI de "cargando" se pruebe bien
    await simularRetraso();
    if (usuario === usuarioMock.usuario && contrasena === usuarioMock.contrasena) {
      return { token: usuarioMock.token, nombre: usuarioMock.nombre, rol: usuarioMock.rol };
    }
    throw new Error("Usuario o contraseña incorrectos");
  }

  const res = await fetch(`${API_URL}/login`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ usuario, contrasena })
  });
  if (!res.ok) throw new Error("Usuario o contraseña incorrectos");
  return res.json();
}

export async function validarTicket(qrData) {
  if (USE_MOCK) {
    await simularRetraso();
    // Elige uno al azar de los 3 casos que armamos (válido, vencido, consumido)
    // para que puedas probar cómo reacciona tu UI a cada estado
    const indice = 0; // fuerza el caso "válido" mientras pruebas
    return ticketsMock[indice];
  }

  const res = await fetch(`${API_URL}/tickets/validar`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ qrData })
  });
  return res.json();
}

export async function registrarDespacho(ticketId, galonesServidos, observaciones) {
  if (USE_MOCK) {
    await simularRetraso();
    console.log("Despacho simulado:", { ticketId, galonesServidos, observaciones });
    return { ok: true, mensaje: "Despacho registrado" };
  }

  const res = await fetch(`${API_URL}/despachos`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ ticketId, galonesServidos, observaciones })
  });
  return res.json();
}

export async function consultarTickets() {
  if (USE_MOCK) {
    await simularRetraso();
    return listaTicketsMock;
  }

  const res = await fetch(`${API_URL}/tickets`);
  return res.json();
}

// Función interna, no se exporta — simula la latencia de una red real (300ms)
function simularRetraso() {
  return new Promise(resolve => setTimeout(resolve, 300));
}
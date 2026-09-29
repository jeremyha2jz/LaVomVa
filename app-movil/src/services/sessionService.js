import { API_URL } from './apiConfig.js';
export const SESSION_KEY = 'lavomva.session';
// No reutilizar credenciales persistidas por versiones anteriores.
[SESSION_KEY, 'token', 'refreshToken', 'expiresAt', 'id', 'nombre', 'rol'].forEach(key => localStorage.removeItem(key));
let renovacion;
let cierre;
export function obtenerSesion() {
  try { return JSON.parse(sessionStorage.getItem(SESSION_KEY)); } catch { return null; }
}
export function eliminarSesion() {
  sessionStorage.removeItem(SESSION_KEY);
  sessionStorage.removeItem('lavomva.ultima-actividad');
  localStorage.removeItem(SESSION_KEY);
  // Claves de sesión de las versiones anteriores de LaVomVa.
  ['token', 'refreshToken', 'expiresAt', 'id', 'nombre', 'rol'].forEach(key => localStorage.removeItem(key));
  window.dispatchEvent(new Event('lavomva:sesion-cerrada'));
}
function tokensValidos(datos) {
  return typeof datos?.token === 'string' && datos.token.length > 0 &&
    typeof datos.refreshToken === 'string' && datos.refreshToken.length > 0 && Date.parse(datos.expiresAt) > Date.now();
}
function vigente(sesion) {
  return Number.isFinite(sesion?.id) && typeof sesion.nombre === 'string' && tokensValidos(sesion) && Date.parse(sesion.expiresAt) > Date.now() + 30_000;
}
async function post(ruta, body) {
  const res = await fetch(`${API_URL}/login${ruta}`, {
    method: 'POST', headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body), signal: AbortSignal.timeout(15_000),
  });
  const texto = await res.text();
  let datos;
  try { datos = texto ? JSON.parse(texto) : {}; } catch { datos = { mensaje: texto }; }
  if (!res.ok) {
    const mensaje = res.status === 401 ? 'Sesión inválida o expirada. Inicia sesión nuevamente.' :
      res.status === 403 ? 'Usuario sin permisos para acceder.' : `Error ${res.status}`;
    throw Object.assign(new Error(datos?.mensaje || datos?.error || mensaje), { status: res.status });
  }
  return datos;
}
// Serializa también entre pestañas del mismo origen cuando Web Locks está disponible.
function conBloqueo(tarea) {
  return globalThis.navigator?.locks ? navigator.locks.request('lavomva.session', tarea) : tarea();
}
export async function login(usuario, contrasena) {
  return conBloqueo(async () => {
    let datos;
    try { datos = await post('', { usuario, contrasena }); }
    catch (error) {
      if (error.status === 401) error.message = 'Usuario o contraseña incorrectos';
      throw error;
    }
    if (!tokensValidos(datos) || !Number.isFinite(datos.id) || typeof datos.nombre !== 'string' || typeof datos.rol !== 'string') {
      throw new Error('Respuesta de sesión incompleta del servidor');
    }
    if (datos.rol !== 'DESPACHADOR') {
      try { await post('/logout', { refreshToken: datos.refreshToken }); } catch { /* No guardar una sesión sin permisos. */ }
      throw Object.assign(new Error('Usuario sin permisos para acceder.'), { status: 403 });
    }
    const { token, refreshToken, expiresAt, id, nombre, rol } = datos;
    sessionStorage.setItem(SESSION_KEY, JSON.stringify({ token, refreshToken, expiresAt, id, nombre, rol }));
    return obtenerSesion();
  });
}
export async function asegurarSesion(tokenRechazado) {
  if (cierre) return null;
  if (renovacion) return renovacion;
  const sesion = obtenerSesion();
  if (sesion?.rol === 'DESPACHADOR' && vigente(sesion) && sesion.token !== tokenRechazado) return sesion;
  renovacion = conBloqueo(async () => {
    const actual = obtenerSesion();
    if (!actual?.refreshToken || actual.rol !== 'DESPACHADOR' || !Number.isFinite(actual.id) || typeof actual.nombre !== 'string') {
      eliminarSesion();
      return null;
    }
    if (vigente(actual) && actual.token !== tokenRechazado) return actual;
    try {
      const datos = await post('/refresh', { refreshToken: actual.refreshToken });
      if (!tokensValidos(datos)) throw new Error('Respuesta de renovación inválida');
      if (obtenerSesion()?.refreshToken !== actual.refreshToken) return null;
      const nueva = { ...actual, token: datos.token, refreshToken: datos.refreshToken, expiresAt: datos.expiresAt };
      sessionStorage.setItem(SESSION_KEY, JSON.stringify(nueva));
      return nueva;
    } catch {
      eliminarSesion();
      return null;
    }
  });
  try { return await renovacion; } finally { renovacion = undefined; }
}
// Peticiones protegidas con renovación de sesión ante un 401.
export async function fetchConSesion(url, opciones = {}) {
  const sesion = await asegurarSesion();
  if (!sesion) throw Object.assign(new Error('Sesión inválida o expirada'), { status: 401 });
  const enviar = token => {
    const headers = new Headers(opciones.headers);
    headers.set('Authorization', `Bearer ${token}`);
    return fetch(url, { ...opciones, headers });
  };
  let res = await enviar(sesion.token);
  if (res.status === 401) {
    const nueva = await asegurarSesion(sesion.token);
    if (!nueva) throw Object.assign(new Error('Sesión inválida o expirada'), { status: 401 });
    res = await enviar(nueva.token);
    if (res.status === 401) eliminarSesion();
  }
  // Un 403 no renueva ni elimina la sesión.
  return res;
}
export async function logout({ inmediato = false } = {}) {
  if (cierre) return cierre;
  const tokenAlCerrar = inmediato ? obtenerSesion()?.refreshToken : null;
  if (inmediato) eliminarSesion();
  cierre = (async () => {
    if (renovacion) await renovacion;
    await conBloqueo(async () => {
      try {
        const refreshToken = inmediato ? tokenAlCerrar : obtenerSesion()?.refreshToken;
        if (refreshToken) await post('/logout', { refreshToken });
      } catch { /* El cierre local también funciona sin conexión. */ }
      finally { if (!inmediato) eliminarSesion(); }
    });
  })();
  try { await cierre; } finally { cierre = undefined; }
}

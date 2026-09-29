// src/main.js
import './style.css';
import { renderLogin } from './pages/login.js';
import { renderEscaner } from './pages/escaner.js';
import { asegurarSesion, obtenerSesion, logout, SESSION_KEY } from './services/sessionService.js';

const app = document.querySelector('#app');

function mostrarLogin() {
  clearTimeout(temporizadorInactividad);
  renderLogin(app, () => {
    reiniciarTemporizador();
    renderEscaner(app);
  });
}
window.addEventListener('lavomva:sesion-cerrada', mostrarLogin);
window.addEventListener('storage', (evento) => {
  if (evento.key === SESSION_KEY && !evento.newValue) mostrarLogin();
});

if ('serviceWorker' in navigator) {
  navigator.serviceWorker.register('/sw.js');
}

// Indicador de conexión: se crea una sola vez y vive fuera del "app" que
// las pantallas van reemplazando, así se ve sin importar en cuál estés.
const avisoConexion = document.createElement('div');
avisoConexion.id = 'aviso-sin-conexion';
avisoConexion.textContent = '⚠️ Sin conexión a internet';
avisoConexion.style.display = 'none';
document.body.prepend(avisoConexion);

function mostrarAviso(sinConexion) {
  avisoConexion.style.display = sinConexion ? 'block' : 'none';
}

async function verificarConexionReal() {
  try {
    const controlador = new AbortController();
    const timeout = setTimeout(() => controlador.abort(), 4000);
    await fetch('/favicon.svg', { method: 'HEAD', cache: 'no-store', signal: controlador.signal });
    clearTimeout(timeout);
    mostrarAviso(false);
  } catch (err) {
    mostrarAviso(true);
  }
}

window.addEventListener('online', verificarConexionReal);
window.addEventListener('offline', () => mostrarAviso(true));

verificarConexionReal();
setInterval(verificarConexionReal, 5000);

// Cierre de sesión automático por inactividad: si pasan 15 minutos sin
// clics, teclas ni toques en pantalla, se cierra la sesión sola.
const TIEMPO_INACTIVIDAD = 15 * 60 * 1000; // 15 minutos en milisegundos
let temporizadorInactividad;

function cerrarSesionPorInactividad() {
  if (obtenerSesion()) void logout();
}

function reiniciarTemporizador() {
  clearTimeout(temporizadorInactividad);
  if (!obtenerSesion()) return;
  temporizadorInactividad = setTimeout(cerrarSesionPorInactividad, TIEMPO_INACTIVIDAD);
}

['click', 'keydown', 'touchstart'].forEach((evento) => {
  document.addEventListener(evento, reiniciarTemporizador);
});

// La expiración del JWT se comprueba independientemente de la actividad.
async function iniciarSesion() {
  const sesion = await asegurarSesion();
  if (sesion) {
    reiniciarTemporizador();
    renderEscaner(app);
  } else mostrarLogin();
}
void iniciarSesion();
setInterval(() => {
  if (obtenerSesion()) void asegurarSesion();
}, 30_000);
document.addEventListener('visibilitychange', () => {
  if (!document.hidden && obtenerSesion()) void asegurarSesion();
});
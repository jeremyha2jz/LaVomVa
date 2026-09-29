// src/main.js
import './style.css';
import { renderLogin } from './pages/login.js';
import { renderEscaner } from './pages/escaner.js';
import { asegurarSesion, obtenerSesion, logout, SESSION_KEY } from './services/sessionService.js';

const app = document.querySelector('#app');

function mostrarLogin() {
  clearTimeout(temporizadorInactividad);
  clearTimeout(temporizadorAviso);
  avisoInactividad.hidden = true;
  renderLogin(app, () => {
    reiniciarTemporizador();
    renderEscaner(app);
  });
}
window.addEventListener('lavomva:sesion-cerrada', mostrarLogin);
window.addEventListener('storage', (evento) => {
  if (evento.storageArea === sessionStorage && evento.key === SESSION_KEY && !evento.newValue) mostrarLogin();
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

const TIEMPO_AVISO = 4 * 60 * 1000;
const TIEMPO_INACTIVIDAD = 5 * 60 * 1000;
const ACTIVIDAD_KEY = 'lavomva.ultima-actividad';
let temporizadorInactividad;
let temporizadorAviso;
const avisoInactividad = document.createElement('div');
avisoInactividad.setAttribute('role', 'status');
avisoInactividad.setAttribute('aria-live', 'polite');
avisoInactividad.textContent = 'Se está detectando inactividad. La sesión se cerrará pronto.';
avisoInactividad.hidden = true;
avisoInactividad.style.cssText = 'position:fixed;z-index:100;top:16px;left:50%;transform:translateX(-50%);width:max-content;max-width:calc(100% - 32px);box-sizing:border-box;padding:12px 16px;border-radius:14px;background:#fff8e1;color:#493b13;box-shadow:0 4px 20px #0002;font:14px/1.4 Poppins,system-ui,sans-serif;pointer-events:none';
document.body.append(avisoInactividad);

function comprobarInactividad() {
  clearTimeout(temporizadorInactividad);
  clearTimeout(temporizadorAviso);
  if (!obtenerSesion()) { avisoInactividad.hidden = true; return false; }
  const ultima = Number(sessionStorage.getItem(ACTIVIDAD_KEY));
  const transcurrido = ultima ? Date.now() - ultima : 0;
  if (transcurrido >= TIEMPO_INACTIVIDAD) {
    void logout({ inmediato: true });
    return false;
  }
  avisoInactividad.hidden = transcurrido < TIEMPO_AVISO;
  if (transcurrido < TIEMPO_AVISO) temporizadorAviso = setTimeout(comprobarInactividad, TIEMPO_AVISO - transcurrido);
  temporizadorInactividad = setTimeout(comprobarInactividad, TIEMPO_INACTIVIDAD - transcurrido);
  return true;
}

function reiniciarTemporizador() {
  if (!comprobarInactividad()) return;
  sessionStorage.setItem(ACTIVIDAD_KEY, String(Date.now()));
  comprobarInactividad();
}

['click', 'pointerdown', 'touchstart', 'keydown', 'input', 'change'].forEach(evento => {
  document.addEventListener(evento, reiniciarTemporizador, { capture: true, passive: true });
});
// Una navegación real (incluido el resultado de un escaneo) reemplaza la página.
// No observar vídeo, contadores ni cambios internos de los formularios.
new MutationObserver(reiniciarTemporizador).observe(app, { childList: true });

async function iniciarSesion() {
  const sesion = await asegurarSesion();
  if (sesion) {
    if (!sessionStorage.getItem(ACTIVIDAD_KEY)) sessionStorage.setItem(ACTIVIDAD_KEY, String(Date.now()));
    if (comprobarInactividad()) renderEscaner(app);
  } else mostrarLogin();
}
void iniciarSesion();
setInterval(() => {
  if (comprobarInactividad()) void asegurarSesion();
}, 30_000);
document.addEventListener('visibilitychange', () => {
  if (!document.hidden && comprobarInactividad()) void asegurarSesion();
});

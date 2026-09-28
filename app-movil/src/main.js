// src/main.js
import './style.css';
import { renderLogin } from './pages/login.js';
import { renderEscaner } from './pages/escaner.js';

const app = document.querySelector('#app');

// Si ya hay un token guardado, saltamos el login e iniciamos directo en el escáner
if (localStorage.getItem('token')) {
  renderEscaner(app);
} else {
  renderLogin(app, () => {
    renderEscaner(app);
  });
}
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
  if (!localStorage.getItem('token')) return; // nada que cerrar si no hay sesión
  localStorage.clear();
  renderLogin(app, () => {
    renderEscaner(app);
  });
}

function reiniciarTemporizador() {
  clearTimeout(temporizadorInactividad);
  temporizadorInactividad = setTimeout(cerrarSesionPorInactividad, TIEMPO_INACTIVIDAD);
}

['click', 'keydown', 'touchstart'].forEach((evento) => {
  document.addEventListener(evento, reiniciarTemporizador);
});

reiniciarTemporizador(); // arranca el temporizador apenas carga la app
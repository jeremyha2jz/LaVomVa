import { registrarPantalla } from '../navigation.js';
import { renderPerfil } from './perfil.js';
// src/pages/escaner.js
import { Html5Qrcode } from 'html5-qrcode';
import { validarTicket } from '../services/ticketService.js';
import { renderTicket } from './ticket.js';
import { renderConsultaTickets } from './consultaTickets.js';
import { logout } from '../services/sessionService.js';
import logoUrl from '../assets/lavomva-marca-blanco.png';

export function renderEscaner(container) {
  registrarPantalla('escaner', () => renderEscaner(container), async () => { if (procesando || saliendo) return false; let listo = false; await salir(() => { listo = true; }); return listo; });
  container.innerHTML = `
<div class="escaner-page" lang="es">
<header class="header"><div class="brand">
  <img class="brand-logo" src="${logoUrl}" alt="" width="260" height="199">
  <p class="brand-title">LaVomVa</p>
  <p class="brand-subtitle">Despacho de combustible</p>
</div></header>
<main class="main">
  <div class="content">
    <h1 class="screen-title">Escanear ticket</h1>
    <section class="camera" id="cameraBox" aria-label="Visor de cámara para escanear código QR">
      <div class="reader-surface"><div id="qr-reader"></div></div>
      <div class="camera-shade" aria-hidden="true"></div>
      <div class="camera-state" id="cameraState" role="status" aria-live="polite">
        <div class="camera-spinner" aria-hidden="true"></div>
        <span id="cameraStateText">Iniciando cámara…</span>
        <button class="camera-retry" id="reintentar-camara" type="button">Reintentar</button>
      </div>
      <div class="scan-box" aria-hidden="true"><i class="tl"></i><i class="tr"></i><i class="bl"></i><i class="br"></i></div>
      <button class="flash-btn" id="toggle-linterna" type="button" aria-label="Activar linterna" disabled><svg viewBox="0 0 20 25" fill="none" aria-hidden="true"><path d="M11.5 1.5H4.4L2.2 13h5.1L6.1 23.5 17.8 9.8h-6.1l-.2-8.3Z" stroke="currentColor" stroke-width="2.3" stroke-linejoin="round"/></svg></button>
    </section>

    <div class="scan-hint"><svg viewBox="0 0 24 24" fill="none" aria-hidden="true"><path d="M8 3H5a2 2 0 0 0-2 2v3M16 3h3a2 2 0 0 1 2 2v3M8 21H5a2 2 0 0 1-2-2v-3M16 21h3a2 2 0 0 0 2-2v-3" stroke="currentColor" stroke-width="2.3" stroke-linecap="round"/></svg><span>Apunta al código QR del ticket</span></div>

    <section class="manual-card" aria-labelledby="manualTicketLabel">
      <div class="manual-eyebrow">¿No funciona la cámara?</div>
      <div class="manual-label" id="manualTicketLabel">Ingresar token QR manualmente</div>
      <div class="manual-row">
        <label class="ticket-input-wrap" aria-label="Token QR"><svg class="ticket-icon" viewBox="0 0 24 24" fill="none" aria-hidden="true"><path d="M20.59 13.41l-7.17 7.17a2 2 0 0 1-2.83 0L2 12V2h10l8.59 8.59a2 2 0 0 1 0 2.82z" stroke="currentColor" stroke-width="2" stroke-linejoin="round"/><path d="M7 7h.01" stroke="currentColor" stroke-width="2.8" stroke-linecap="round"/></svg>
          <input id="ticket-manual" class="ticket-input" type="text" autocomplete="off" autocapitalize="characters" spellcheck="false" placeholder="Pega aquí el token del QR">
        </label>
        <button class="submit-btn" id="validar-manual" type="button" aria-label="Consultar ticket"><svg viewBox="0 0 24 24" fill="none" aria-hidden="true"><path d="M5 12h13M13 7l5 5-5 5" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"/></svg></button>
      </div>
    </section>
    <p id="escaner-estado" role="status" aria-live="polite"></p>
    <button id="cerrar-sesion" type="button" hidden>Cerrar sesión</button>
  </div>
  <svg class="watermark" viewBox="0 0 120 120" fill="none" aria-hidden="true">
  <path d="M25 16h53v88H25z" stroke="currentColor" stroke-width="10" stroke-linejoin="round"/>
  <rect x="34" y="27" width="35" height="26" rx="3" stroke="currentColor" stroke-width="8"/>
  <path d="M78 29h9c10 0 17 8 17 18v30c0 8 5 13 11 13 6 0 10-5 10-11V51" stroke="currentColor" stroke-width="9" stroke-linecap="round"/>
  <path d="M91 32l17 16" stroke="currentColor" stroke-width="9" stroke-linecap="round"/>
</svg>
  <div class="bottom-art" aria-hidden="true"><svg viewBox="0 0 430 145" preserveAspectRatio="none">
  <path class="a1" d="M0 30C88 91 172 121 277 124C325 126 374 116 430 87V145H0Z"/>
  <path class="a2" d="M0 48C91 106 181 133 286 134C340 135 389 123 430 101V145H0Z"/>
  <path class="a3" d="M0 62C94 112 179 136 275 145H0Z"/>
  <path class="a4" d="M264 145C327 136 381 121 430 93V145Z"/>
  <path class="a5" d="M320 145C365 132 401 119 430 103V145Z"/>
</svg></div>
</main>
<div class="nav-wrap"><nav class="nav" aria-label="Navegación principal"><button type="button" class="nav-item active" aria-current="page"><svg viewBox="0 0 24 24" fill="none" aria-hidden="true"><path d="M8 3H5a2 2 0 0 0-2 2v3M16 3h3a2 2 0 0 1 2 2v3M8 21H5a2 2 0 0 1-2-2v-3M16 21h3a2 2 0 0 0 2-2v-3" stroke="currentColor" stroke-width="2.3" stroke-linecap="round"/></svg><span>Escanear</span></button><button type="button" class="nav-item" id="ir-consulta"><svg viewBox="0 0 24 24" fill="none" aria-hidden="true"><path d="M7 4h10M7 8h10M7 12h7M6 2.8c.8 0 1.2.9 2 .9s1.2-.9 2-.9 1.2.9 2 .9 1.2-.9 2-.9 1.2.9 2 .9 1.2-.9 2-.9V21c-.8 0-1.2-.9-2-.9s-1.2.9-2 .9-1.2-.9-2-.9-1.2.9-2 .9-1.2-.9-2-.9-1.2.9-2 .9V2.8Z" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"/></svg><span>Tickets</span></button><button type="button" class="nav-item" id="ir-perfil"><svg viewBox="0 0 24 24" fill="none" aria-hidden="true"><circle cx="12" cy="8" r="4" stroke="currentColor" stroke-width="2"/><path d="M4.5 21c.9-4.3 3.5-6.5 7.5-6.5s6.6 2.2 7.5 6.5" stroke="currentColor" stroke-width="2" stroke-linecap="round"/></svg><span>Perfil</span></button></nav></div>

</div>
`;

  const estadoMsg = container.querySelector('#escaner-estado');
  const btnLinterna = container.querySelector('#toggle-linterna');
  const pantalla = container.querySelector('.escaner-page');
  const visor = container.querySelector('#cameraBox');
  const mensajeCamara = container.querySelector('#cameraStateText');
  const btnReintentar = container.querySelector('#reintentar-camara');
  const btnManual = container.querySelector('#validar-manual');
  let html5QrCode = new Html5Qrcode("qr-reader");
  let linternaEncendida = false;
  let inicioCamara;
  let cierreCamara;
  let procesando = false;
  let saliendo = false;

  // Esperar también un inicio pendiente antes de liberar el video.
  function detenerCamara() {
    if (cierreCamara) return cierreCamara;
    cierreCamara = (async () => {
      await inicioCamara;
      if (html5QrCode.isScanning) await html5QrCode.stop();
      visor.classList.remove('is-live');
      btnLinterna.disabled = true;
      btnLinterna.classList.remove('is-on');
      btnLinterna.setAttribute('aria-pressed', 'false');
      linternaEncendida = false;
    })().finally(() => { cierreCamara = null; });
    return cierreCamara;
  }

  async function salir(navegar) {
    if (saliendo) return;
    saliendo = true;
    try {
      await detenerCamara();
      observador.disconnect();
      window.removeEventListener('pagehide', liberarAlSalir);
      document.removeEventListener('visibilitychange', sincronizarVisibilidad);
      navegar();
    } catch {
      saliendo = false;
      estadoMsg.textContent = 'No se pudo detener la cámara. Intenta nuevamente.';
    }
  }

  function liberarAlSalir() {
    saliendo = true;
    observador.disconnect();
    window.removeEventListener('pagehide', liberarAlSalir);
    document.removeEventListener('visibilitychange', sincronizarVisibilidad);
    void detenerCamara().catch(() => {});
  }

  // main.js puede reemplazar la pantalla al cerrar sesión por inactividad.
  const observador = new MutationObserver(() => {
    if (!container.contains(pantalla)) liberarAlSalir();
  });
  observador.observe(container, { childList: true });
  window.addEventListener('pagehide', liberarAlSalir);
  document.addEventListener('visibilitychange', sincronizarVisibilidad);

  // Vibración corta (si el dispositivo lo soporta) + beep simple con la
  // Web Audio API. Es solo feedback de "leí un QR", no indica si es válido.
  function avisarLecturaExitosa() {
    if (navigator.vibrate) {
      navigator.vibrate(150);
    }
    try {
      const ctx = new (window.AudioContext || window.webkitAudioContext)();
      const osc = ctx.createOscillator();
      osc.frequency.value = 880; // tono agudo simple, tipo "beep" de scanner
      osc.connect(ctx.destination);
      osc.start();
      osc.stop(ctx.currentTime + 0.1); // beep corto, 100ms
    } catch {
      // Si el navegador bloquea el audio (requiere interacción previa), no pasa nada grave
    }
  }

  async function procesarResultado(qrCodeMessage) {
    if (procesando || saliendo) return;
    procesando = true;
    btnManual.disabled = true;
    estadoMsg.textContent = "Validando ticket...";
    let camaraDetenida = false;
    try {
      await detenerCamara();
      camaraDetenida = true;
      if (saliendo) return;
      visor.classList.add('is-stopped');
      mensajeCamara.textContent = 'Validando ticket...';
      avisarLecturaExitosa();
      const resultado = await validarTicket(qrCodeMessage);
      if (!saliendo && container.contains(pantalla)) await salir(() => renderTicket(container, resultado));
    } catch (err) {
      if (saliendo || !container.contains(pantalla)) return;
      if (!camaraDetenida) {
        estadoMsg.textContent = 'No se pudo detener la cámara. Intenta nuevamente.';
        mostrarErrorCamara(err);
        return;
      }
      const contexto = {
        400: 'Solicitud de validación rechazada (400)',
        401: 'Sesión inválida o expirada (401)',
        403: 'Sin permisos para validar tickets (403)',
        500: 'Error del servidor (500)'
      };
      estadoMsg.textContent = err.status
        ? `${contexto[err.status] || `Error HTTP ${err.status}`}: ${err.message}`
        : err instanceof TypeError
          ? 'No se pudo conectar con el servidor. Comprueba la conexión e intenta nuevamente.'
          : err.message || 'No se pudo validar el ticket. Intenta nuevamente.';
      visor.classList.remove('has-error', 'is-live');
      visor.classList.add('is-stopped');
      mensajeCamara.textContent = 'Validación no completada. Puedes volver a escanear o usar la entrada manual.';
      btnReintentar.style.display = 'inline-flex';
    } finally {
      procesando = false;
      btnManual.disabled = false;
    }
  }

  function iniciarCamara() {
    if (saliendo || procesando || inicioCamara || cierreCamara) return;
    estadoMsg.textContent = "";
    mensajeCamara.textContent = 'Iniciando cámara…';
    btnReintentar.style.display = '';
    visor.classList.remove('has-error', 'is-live', 'is-stopped');
    btnReintentar.disabled = true;
    btnLinterna.disabled = true;

    inicioCamara = Promise.resolve().then(() => html5QrCode.start(
      { facingMode: "environment" },
      { fps: 15, qrbox: (ancho, alto) => {
        // La región de lectura coincide con la guía y cabe en pantallas pequeñas.
        const lado = Math.floor(Math.min(142, ancho * 0.38, alto * 0.75, visor.clientHeight * 0.75));
        visor.style.setProperty('--qr-size', `${lado}px`);
        return { width: lado, height: lado };
      } },
      (qrCodeMessage) => { void procesarResultado(qrCodeMessage); },
      (errorMessage) => {}
    )).then(() => {
      if (saliendo || procesando) return;
      visor.classList.add('is-live');
      verificarLinterna();
    }).catch((err) => {
      if (!saliendo && !procesando) mostrarErrorCamara(err);
    }).finally(() => {
      inicioCamara = null;
      btnReintentar.disabled = false;
    });
  }

  function sincronizarVisibilidad() {
    if (document.hidden) {
      linternaEncendida = false;
      btnLinterna.classList.remove('is-on');
      btnLinterna.setAttribute('aria-pressed', 'false');
      btnLinterna.setAttribute('aria-label', 'Encender linterna');
    }
  }

  function verificarLinterna() {
    try {
      const capacidades = html5QrCode.getRunningTrackCapabilities();
      btnLinterna.disabled = !capacidades?.torch;
      btnLinterna.setAttribute('aria-label', capacidades?.torch ? 'Encender linterna' : 'Linterna no disponible');
    } catch {
      btnLinterna.disabled = true;
    }
  }

  async function toggleLinterna() {
    if (btnLinterna.disabled || saliendo || procesando) return;
    btnLinterna.disabled = true;
    linternaEncendida = !linternaEncendida;
    try {
      await html5QrCode.applyVideoConstraints({
        advanced: [{ torch: linternaEncendida }]
      });
      btnLinterna.classList.toggle('is-on', linternaEncendida);
      btnLinterna.setAttribute('aria-label', linternaEncendida ? 'Apagar linterna' : 'Encender linterna');
      btnLinterna.setAttribute('aria-pressed', String(linternaEncendida));
    } catch (err) {
      estadoMsg.textContent = "No se pudo controlar la linterna en este dispositivo";
      linternaEncendida = !linternaEncendida;
    } finally {
      btnLinterna.disabled = saliendo || procesando || !html5QrCode.isScanning;
    }
  }
  function mostrarErrorCamara(err) {
    btnLinterna.disabled = true;
    visor.classList.remove('is-live');
    visor.classList.add('has-error');
    const detalle = String(err?.message || err);
    mensajeCamara.textContent = /NotAllowed|Permission|denied|Security/i.test(detalle)
      ? 'Permite el acceso a la cámara para escanear el ticket. También puedes pegar el token QR manualmente.'
      : 'No se pudo abrir la cámara. Puedes pegar el token QR manualmente.';
    mensajeCamara.title = detalle;
  }

  btnReintentar.addEventListener('click', iniciarCamara);
  iniciarCamara();

  btnLinterna.addEventListener('click', toggleLinterna);

  container.querySelector('#validar-manual').addEventListener('click', () => {
    const idManual = container.querySelector('#ticket-manual').value.trim();
    if (!idManual) {
      estadoMsg.textContent = "Pega el token QR antes de validar";
      return;
    }
    void procesarResultado(idManual);
  });

  container.querySelector('#ir-perfil').addEventListener('click', () => {
    void salir(() => renderPerfil(container));
  });

  container.querySelector('#ir-consulta').addEventListener('click', () => {
    void salir(() => renderConsultaTickets(container));
  });

  container.querySelector('#cerrar-sesion').addEventListener('click', () => {
    void salir(() => { void logout(); });
  });
}

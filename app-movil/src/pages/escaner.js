// src/pages/escaner.js
import { Html5Qrcode } from 'html5-qrcode';
import { validarTicket } from '../services/ticketService.js';
import { renderTicket } from './ticket.js';
import { renderConsultaTickets } from './consultaTickets.js';
import { renderLogin } from './login.js';

export function renderEscaner(container) {
  container.innerHTML = `
  <div class="escaner-page">
    <h1>Escanear ticket</h1>
    <div id="qr-reader" style="width: 100%; max-width: 400px;"></div>
    <button id="toggle-linterna" style="display: none;">🔦 Encender linterna</button>
    <p id="escaner-estado"></p>

    <div class="entrada-manual">
      <label>¿No funciona la cámara? Escribe el ID del ticket:</label>
      <input type="text" id="ticket-manual" placeholder="COM-2026-000001" />
      <button id="validar-manual">Validar manualmente</button>
    </div>

    <button id="ir-consulta">Ver tickets</button>
    <button id="cerrar-sesion">Cerrar sesión</button>
  </div>
`;

  const estadoMsg = container.querySelector('#escaner-estado');
  const btnLinterna = container.querySelector('#toggle-linterna');
  let html5QrCode = new Html5Qrcode("qr-reader");
  let linternaEncendida = false;

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
    estadoMsg.textContent = "Validando ticket...";
    try {
      const resultado = await validarTicket(qrCodeMessage);
      renderTicket(container, resultado);
    } catch (err) {
      estadoMsg.textContent = "Error al validar el ticket";
    }
  }

  function iniciarCamara() {
    estadoMsg.textContent = "";

    html5QrCode.start(
      { facingMode: "environment" },
      { fps: 15, qrbox: 280 },
      async (qrCodeMessage) => {
        await html5QrCode.stop();
        btnLinterna.style.display = "none";
        avisarLecturaExitosa();
        procesarResultado(qrCodeMessage);
      },
      (errorMessage) => {}
    ).then(() => {
      verificarLinterna();
    }).catch((err) => {
      mostrarErrorCamara(err);
    });
  }

  function verificarLinterna() {
    try {
      const capacidades = html5QrCode.getRunningTrackCapabilities();
      if (capacidades && capacidades.torch) {
        btnLinterna.style.display = "inline-block";
      } else {
        btnLinterna.style.display = "none";
      }
    } catch {
      btnLinterna.style.display = "none";
    }
  }

  async function toggleLinterna() {
    linternaEncendida = !linternaEncendida;
    try {
      await html5QrCode.applyVideoConstraints({
        advanced: [{ torch: linternaEncendida }]
      });
      btnLinterna.textContent = linternaEncendida ? "🔦 Apagar linterna" : "🔦 Encender linterna";
    } catch (err) {
      estadoMsg.textContent = "No se pudo controlar la linterna en este dispositivo";
      linternaEncendida = !linternaEncendida;
    }
  }

  function mostrarErrorCamara(err) {
    btnLinterna.style.display = "none";
    estadoMsg.innerHTML = `
      No se pudo acceder a la cámara: ${err}<br>
      <button id="reintentar-camara">Reintentar</button>
    `;
    container.querySelector('#reintentar-camara').addEventListener('click', () => {
      html5QrCode = new Html5Qrcode("qr-reader");
      iniciarCamara();
    });
  }

  iniciarCamara();

  btnLinterna.addEventListener('click', toggleLinterna);

  container.querySelector('#validar-manual').addEventListener('click', () => {
    const idManual = container.querySelector('#ticket-manual').value.trim();
    if (!idManual) {
      estadoMsg.textContent = "Escribe un ID de ticket antes de validar";
      return;
    }
    html5QrCode.stop().catch(() => {});
    avisarLecturaExitosa();
    procesarResultado(idManual);
  });

  container.querySelector('#ir-consulta').addEventListener('click', () => {
    html5QrCode.stop().catch(() => {});
    renderConsultaTickets(container);
  });

  container.querySelector('#cerrar-sesion').addEventListener('click', () => {
    html5QrCode.stop().catch(() => {});
    localStorage.clear();
    renderLogin(container, () => {
      renderEscaner(container);
    });
  });
}
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
    <p id="escaner-estado"></p>
    <button id="ir-consulta">Ver tickets</button>
    <button id="cerrar-sesion">Cerrar sesión</button>
  </div>
`;

  const estadoMsg = container.querySelector('#escaner-estado');
  let html5QrCode = new Html5Qrcode("qr-reader");

  function iniciarCamara() {
    estadoMsg.textContent = "";

    html5QrCode.start(
      { facingMode: "environment" }, // pide la cámara trasera directamente
      { fps: 10, qrbox: 250 },
      async (qrCodeMessage) => {
        await html5QrCode.stop();
        estadoMsg.textContent = "Validando ticket...";

        try {
          const resultado = await validarTicket(qrCodeMessage);
          renderTicket(container, resultado);
        } catch (err) {
          estadoMsg.textContent = "Error al validar el ticket";
        }
      },
      (errorMessage) => {}
    ).catch((err) => {
      mostrarErrorCamara(err);
    });
  }

  function mostrarErrorCamara(err) {
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
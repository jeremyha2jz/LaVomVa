// src/pages/despacho.js
import { registrarDespacho } from '../services/ticketService.js';
import { renderEscaner } from './escaner.js';

export function renderDespacho(container, ticket) {
  container.innerHTML = `
    <div class="despacho-page">
      <h1>Registrar despacho</h1>
      <p><strong>Ticket:</strong> ${ticket.id}</p>
      <form id="despacho-form">
        <label>Galones servidos</label>
        <input type="number" id="galones" step="0.1" min="0.1" max="${ticket.cantidadAutorizada}" required />
        <label>Observaciones</label>
        <textarea id="observaciones" placeholder="Opcional"></textarea>
        <button type="submit">Confirmar despacho</button>
        <button type="button" id="cancelar-despacho">Cancelar</button>
      </form>
      <p id="despacho-estado"></p>
    </div>

    <div id="modal-confirmacion" class="modal-overlay" style="display: none;">
      <div class="modal-caja">
        <p id="modal-texto"></p>
        <div class="modal-botones">
          <button id="modal-si">Sí, registrar</button>
          <button id="modal-no">Cancelar</button>
        </div>
      </div>
    </div>
  `;

  const form = container.querySelector('#despacho-form');
  const estadoMsg = container.querySelector('#despacho-estado');
  const modal = container.querySelector('#modal-confirmacion');
  const modalTexto = container.querySelector('#modal-texto');
  const btnSi = container.querySelector('#modal-si');
  const btnNo = container.querySelector('#modal-no');

  // Devuelve una Promise que se resuelve con true/false según el botón que
  // presione el usuario, para poder usar "await" como si fuera confirm() nativo.
  function pedirConfirmacion(mensaje) {
    return new Promise((resolve) => {
      modalTexto.textContent = mensaje;
      modal.style.display = "flex";

      function limpiar(respuesta) {
        modal.style.display = "none";
        btnSi.removeEventListener('click', onSi);
        btnNo.removeEventListener('click', onNo);
        resolve(respuesta);
      }
      function onSi() { limpiar(true); }
      function onNo() { limpiar(false); }

      btnSi.addEventListener('click', onSi);
      btnNo.addEventListener('click', onNo);
    });
  }

  form.addEventListener('submit', async (e) => {
    e.preventDefault();
    const galones = parseFloat(container.querySelector('#galones').value);
    const observaciones = container.querySelector('#observaciones').value;

    if (isNaN(galones) || galones <= 0) {
      estadoMsg.textContent = "La cantidad de galones debe ser mayor a 0";
      return;
    }
    if (galones > ticket.cantidadAutorizada) {
      estadoMsg.textContent = "No puedes servir más de lo autorizado";
      return;
    }

    const confirmado = await pedirConfirmacion(
      `¿Confirmas registrar ${galones} galones para el ticket ${ticket.id}?`
    );
    if (!confirmado) return;

    try {
      const resultado = await registrarDespacho(ticket.id, galones, observaciones);
      estadoMsg.textContent = resultado.mensaje;

      form.style.display = "none";
      const btnVolver = document.createElement('button');
      btnVolver.id = "volver-escaner";
      btnVolver.textContent = "Volver a escanear";
      estadoMsg.after(btnVolver);

      btnVolver.addEventListener('click', () => {
        renderEscaner(container);
      });
    } catch (err) {
      estadoMsg.textContent = err.message || "Error al registrar el despacho";
    }
  });

  container.querySelector('#cancelar-despacho').addEventListener('click', () => {
    renderEscaner(container);
  });
}
// src/pages/ticket.js
import { renderDespacho } from './despacho.js';
import { renderEscaner } from './escaner.js';
import { escapeHtml } from '../services/html.js';

export function renderTicket(container, resultado) {
  if (!resultado.valido) {
    container.innerHTML = `
      <div class="ticket-page">
        <h1>Ticket no válido</h1>
        <p class="error">${escapeHtml(resultado.mensajeError)}</p>
        <button id="volver">Volver a escanear</button>
      </div>
    `;
    container.querySelector('#volver').addEventListener('click', () => {
      renderEscaner(container);
    });
    return;
  }

  const t = resultado.ticket;
  const ticketId = escapeHtml(t.id);
  container.innerHTML = `
    <div class="ticket-page">
      <h1>Ticket válido</h1>
      <p><strong>ID:</strong> ${ticketId}</p>
      <p><strong>Empleado:</strong> ${escapeHtml(t.empleado.nombre)}</p>
      <p><strong>Vehículo:</strong> ${escapeHtml(t.vehiculo.placa)}</p>
      <p><strong>Cantidad autorizada:</strong> ${escapeHtml(t.cantidadAutorizada)} galones</p>
      <p><strong>Combustible:</strong> ${escapeHtml(t.tipoCombustible)}</p>
      <button id="confirmar-despacho">Registrar despacho</button>
    </div>
  `;

  container.querySelector('#confirmar-despacho').addEventListener('click', () => {
    renderDespacho(container, t);
  });
}

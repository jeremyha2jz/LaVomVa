// src/pages/consultaTickets.js
import { consultarTickets } from '../services/ticketService.js';
import { renderEscaner } from './escaner.js';
import { escapeHtml, safeClassName } from '../services/html.js';

export function renderConsultaTickets(container) {
  container.innerHTML = `
    <div class="consulta-page">
      <h1>Consultar tickets</h1>
      <p id="consulta-estado">Cargando tickets...</p>
      <ul id="lista-tickets"></ul>
      <button id="volver-escaner">Volver al escáner</button>
    </div>
  `;

  const estadoMsg = container.querySelector('#consulta-estado');
  const lista = container.querySelector('#lista-tickets');

  consultarTickets().then((tickets) => {
    estadoMsg.textContent = "";

    if (!tickets.length) {
      estadoMsg.textContent = "No hay tickets registrados";
      return;
    }

    lista.innerHTML = tickets.map(t => `
      <li class="ticket-item estado-${safeClassName(t.estado)}">
        <strong>${escapeHtml(t.id)}</strong> — ${escapeHtml(t.estado)}<br>
        Vehículo: ${escapeHtml(t.vehiculo)} | Autorizado: ${escapeHtml(t.cantidadAutorizada)} gal | Vence: ${escapeHtml(t.fechaVencimiento)}
      </li>
    `).join('');
  }).catch((err) => {
    estadoMsg.textContent = "Error al cargar tickets";
  });

  container.querySelector('#volver-escaner').addEventListener('click', () => {
    renderEscaner(container);
  });
}

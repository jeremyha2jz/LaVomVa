// src/pages/despacho.js
import { registrarDespacho } from '../services/ticketService.js';

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
      </form>
      <p id="despacho-estado"></p>
    </div>
  `;

  const form = container.querySelector('#despacho-form');
  const estadoMsg = container.querySelector('#despacho-estado');

  form.addEventListener('submit', async (e) => {
    e.preventDefault();
    const galones = parseFloat(container.querySelector('#galones').value);
    const observaciones = container.querySelector('#observaciones').value;

    // Validación extra en JS: el "min" del HTML se puede saltar editando el DOM,
    // así que revisamos también aquí antes de mandar el dato.
    if (isNaN(galones) || galones <= 0) {
      estadoMsg.textContent = "La cantidad de galones debe ser mayor a 0";
      return;
    }
    if (galones > ticket.cantidadAutorizada) {
      estadoMsg.textContent = "No puedes servir más de lo autorizado";
      return;
    }

    try {
      const resultado = await registrarDespacho(ticket.id, galones, observaciones);
      estadoMsg.textContent = resultado.mensaje;
    } catch (err) {
      estadoMsg.textContent = "Error al registrar el despacho";
    }
  });
}
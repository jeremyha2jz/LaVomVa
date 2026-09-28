// src/pages/ticket.js
import { renderDespacho } from './despacho.js';
import { renderEscaner } from './escaner.js';

export function renderTicket(container, resultado) {
  if (!resultado.valido) {
    container.innerHTML = `
      <div class="ticket-page">
        <h1>Ticket no válido</h1>
        <p class="error">${resultado.mensajeError}</p>
        <button id="volver">Volver a escanear</button>
      </div>
    `;
    container.querySelector('#volver').addEventListener('click', () => {
      renderEscaner(container);
    });
    return;
  }

  const t = resultado.ticket;
  container.innerHTML = `
    <div class="ticket-page">
      <h1>Ticket válido</h1>
      <p><strong>ID:</strong> ${t.id}</p>
      <p><strong>Empleado:</strong> ${t.empleado.nombre}</p>
      <p><strong>Vehículo:</strong> ${t.vehiculo.placa}</p>
      <p><strong>Departamento:</strong> ${t.departamento}</p>
      <p><strong>Cantidad autorizada:</strong> ${t.cantidadAutorizada} galones</p>
      <p><strong>Combustible:</strong> ${t.tipoCombustible}</p>
      <p><strong>Vence:</strong> ${t.fechaVencimiento}</p>
      <button id="confirmar-despacho">Registrar despacho</button>
      <button id="cancelar-ticket">Cancelar</button>
    </div>
  `;

  container.querySelector('#confirmar-despacho').addEventListener('click', () => {
    renderDespacho(container, t);
  });

  container.querySelector('#cancelar-ticket').addEventListener('click', () => {
    renderEscaner(container);
  });
}
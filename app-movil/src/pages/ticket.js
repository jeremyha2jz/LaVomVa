import { renderPerfil } from './perfil.js';
// src/pages/ticket.js
import { renderDespacho } from './despacho.js';
import { renderEscaner } from './escaner.js';
import { renderConsultaTickets } from './consultaTickets.js';
import logoUrl from '../assets/lavomva-marca-blanco.png';

export function renderTicket(container, resultado) {
  if (!resultado.valido) {
    const idTicket = resultado.ticket?.id ?? resultado.ticketId ?? resultado.id;
    container.innerHTML = `
<div class="ticket-page" lang="es">
<header class="header"><div class="brand">
  <img class="brand-logo" src="${logoUrl}" alt="" width="260" height="199">
  <p class="brand-title">LaVomVa</p>
  <p class="brand-subtitle">Despacho de combustible</p>
</div></header>
<main class="main">
  <div class="content">
    <section class="card error-shell">
      <div class="error-badge" aria-hidden="true"><svg viewBox="0 0 48 48" fill="none" aria-hidden="true"><path d="M24 12v16M24 35h.01" stroke="currentColor" stroke-width="6" stroke-linecap="round"/></svg></div>
      <h1 class="card-title">Ticket no válido</h1>
      <p class="card-message" id="reason">${escaparTexto(resultado.mensajeError ?? resultado.mensaje ?? resultado.estado ?? "")}</p>
      ${idTicket != null && idTicket !== "" ? filaDato("ID del ticket", idTicket, "tag", "error-id") : ""}
      <button class="action action-danger" id="volver" type="button"><svg viewBox="0 0 24 24" fill="none" aria-hidden="true"><path d="M8 3H5a2 2 0 0 0-2 2v3M16 3h3a2 2 0 0 1 2 2v3M8 21H5a2 2 0 0 1-2-2v-3M16 21h3a2 2 0 0 0 2-2v-3" stroke="currentColor" stroke-width="2.3" stroke-linecap="round"/></svg>Volver a escanear</button>
    </section>
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
<div class="nav-wrap"><nav class="nav" aria-label="Navegación principal"><button type="button" class="nav-item active" aria-current="page" id="ticket-ir-escaner"><svg viewBox="0 0 24 24" fill="none" aria-hidden="true"><path d="M8 3H5a2 2 0 0 0-2 2v3M16 3h3a2 2 0 0 1 2 2v3M8 21H5a2 2 0 0 1-2-2v-3M16 21h3a2 2 0 0 0 2-2v-3" stroke="currentColor" stroke-width="2.3" stroke-linecap="round"/></svg><span>Escanear</span></button><button type="button" class="nav-item" id="ticket-ir-consulta"><svg viewBox="0 0 24 24" fill="none" aria-hidden="true"><path d="M7 4h10M7 8h10M7 12h7M6 2.8c.8 0 1.2.9 2 .9s1.2-.9 2-.9 1.2.9 2 .9 1.2-.9 2-.9 1.2.9 2 .9 1.2-.9 2-.9V21c-.8 0-1.2-.9-2-.9s-1.2.9-2 .9-1.2-.9-2-.9-1.2.9-2 .9-1.2-.9-2-.9-1.2.9-2 .9V2.8Z" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"/></svg><span>Tickets</span></button><button type="button" class="nav-item" id="ir-perfil"><svg viewBox="0 0 24 24" fill="none" aria-hidden="true"><circle cx="12" cy="8" r="4" stroke="currentColor" stroke-width="2"/><path d="M4.5 21c.9-4.3 3.5-6.5 7.5-6.5s6.6 2.2 7.5 6.5" stroke="currentColor" stroke-width="2" stroke-linecap="round"/></svg><span>Perfil</span></button></nav></div>

</div>
`;
    conectarNavegacion(container);
    container.querySelector('#volver').addEventListener('click', () => {
      renderEscaner(container);
    });
    return;
  }

  const t = resultado.ticket;
  container.innerHTML = `
<div class="ticket-page" lang="es">
<header class="header"><div class="brand">
  <img class="brand-logo" src="${logoUrl}" alt="" width="260" height="199">
  <p class="brand-title">LaVomVa</p>
  <p class="brand-subtitle">Despacho de combustible</p>
</div></header>
<main class="main">
  <div class="content">
    <section class="card success-card">
      <div class="success-badge" aria-hidden="true"><svg viewBox="0 0 48 48" fill="none" aria-hidden="true"><path d="m12 25 8 8 16-18" stroke="currentColor" stroke-width="6" stroke-linecap="round" stroke-linejoin="round"/></svg></div>
      <h1 class="card-title">Ticket válido</h1>
      <p class="card-message">El ticket puede ser utilizado para despacho.</p>
      <div class="data-list">${filasTicket(t)}</div>
      <div class="stack">
        <button class="action action-primary" id="confirmar-despacho" type="button"><svg viewBox="0 0 32 32" fill="none" aria-hidden="true"><path d="M8 5.5h12v21H8z" fill="currentColor"/><path d="M20 9.5h2.1c2 0 3.4 1.7 3.4 3.7v7.1c0 1.4 1 2.5 2.3 2.5 1.2 0 2.2-1 2.2-2.3v-7" stroke="currentColor" stroke-width="2" stroke-linecap="round"/></svg>Registrar despacho</button>
        <button class="action action-secondary" id="cancelar-ticket" type="button"><svg viewBox="0 0 24 24" fill="none" aria-hidden="true"><path d="M6 6l12 12M18 6L6 18" stroke="currentColor" stroke-width="2.4" stroke-linecap="round"/></svg>Cancelar</button>
      </div>
    </section>
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
<div class="nav-wrap"><nav class="nav" aria-label="Navegación principal"><button type="button" class="nav-item active" aria-current="page" id="ticket-ir-escaner"><svg viewBox="0 0 24 24" fill="none" aria-hidden="true"><path d="M8 3H5a2 2 0 0 0-2 2v3M16 3h3a2 2 0 0 1 2 2v3M8 21H5a2 2 0 0 1-2-2v-3M16 21h3a2 2 0 0 0 2-2v-3" stroke="currentColor" stroke-width="2.3" stroke-linecap="round"/></svg><span>Escanear</span></button><button type="button" class="nav-item" id="ticket-ir-consulta"><svg viewBox="0 0 24 24" fill="none" aria-hidden="true"><path d="M7 4h10M7 8h10M7 12h7M6 2.8c.8 0 1.2.9 2 .9s1.2-.9 2-.9 1.2.9 2 .9 1.2-.9 2-.9 1.2.9 2 .9 1.2-.9 2-.9V21c-.8 0-1.2-.9-2-.9s-1.2.9-2 .9-1.2-.9-2-.9-1.2.9-2 .9-1.2-.9-2-.9-1.2.9-2 .9V2.8Z" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"/></svg><span>Tickets</span></button><button type="button" class="nav-item" id="ir-perfil"><svg viewBox="0 0 24 24" fill="none" aria-hidden="true"><circle cx="12" cy="8" r="4" stroke="currentColor" stroke-width="2"/><path d="M4.5 21c.9-4.3 3.5-6.5 7.5-6.5s6.6 2.2 7.5 6.5" stroke="currentColor" stroke-width="2" stroke-linecap="round"/></svg><span>Perfil</span></button></nav></div>

</div>
`;
  conectarNavegacion(container);

  container.querySelector('#confirmar-despacho').addEventListener('click', () => {
    renderDespacho(container, t);
  });

  container.querySelector('#cancelar-ticket').addEventListener('click', () => {
    renderEscaner(container);
  });
}
const ICONOS = {
  "tag": "<path d=\"M20.59 13.41l-7.17 7.17a2 2 0 0 1-2.83 0L2 12V2h10l8.59 8.59a2 2 0 0 1 0 2.82z\" stroke=\"currentColor\" stroke-width=\"2\" stroke-linejoin=\"round\"/><path d=\"M7 7h.01\" stroke=\"currentColor\" stroke-width=\"2.8\" stroke-linecap=\"round\"/>",
  "user": "<circle cx=\"12\" cy=\"8\" r=\"4\" stroke=\"currentColor\" stroke-width=\"2\"/><path d=\"M4.5 21c.9-4.3 3.5-6.5 7.5-6.5s6.6 2.2 7.5 6.5\" stroke=\"currentColor\" stroke-width=\"2\" stroke-linecap=\"round\"/>",
  "car": "<path d=\"M5 16v-5l2-5h10l2 5v5M7 16v2M17 16v2M5 12h14M8 12h.01M16 12h.01\" stroke=\"currentColor\" stroke-width=\"2\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/>",
  "building": "<path d=\"M5 21V5h10v16M15 10h4v11M8 8h2M8 12h2M8 16h2\" stroke=\"currentColor\" stroke-width=\"2\" stroke-linecap=\"round\"/>",
  "pump": "<path d=\"M7 4h9v16H7zM16 7h2c2 0 3 1 3 3v6\" stroke=\"currentColor\" stroke-width=\"2\" stroke-linecap=\"round\"/>",
  "drop": "<path d=\"M12 3c4 5 6 8 6 11a6 6 0 1 1-12 0c0-3 2-6 6-11Z\" stroke=\"currentColor\" stroke-width=\"2\"/>",
  "cal": "<path d=\"M5 6h14v14H5zM8 3v6M16 3v6M5 10h14\" stroke=\"currentColor\" stroke-width=\"2\" stroke-linecap=\"round\"/>"
};

function escaparTexto(valor) {
  return String(valor ?? '').replace(/[&<>"']/g, caracter => ({
    '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;'
  }[caracter]));
}

function filaDato(etiqueta, valor, icono, clase = 'info-row') {
  return `<div class="${clase}">
    <svg class="info-icon" viewBox="0 0 24 24" fill="none" aria-hidden="true">${ICONOS[icono]}</svg>
    <div><div class="info-label">${etiqueta}</div><div class="info-value">${escaparTexto(valor)}</div></div>
  </div>`;
}

function filasTicket(t) {
  return [
    filaDato('ID del ticket', t.id, 'tag'),
    filaDato('Empleado', t.empleado.nombre, 'user'),
    filaDato('Vehículo', t.vehiculo.placa, 'car'),
    filaDato('Departamento', t.departamento, 'building'),
    filaDato('Cantidad autorizada', t.cantidadAutorizada + ' galones', 'pump'),
    filaDato('Tipo de combustible', t.tipoCombustible, 'drop'),
    filaDato('Fecha de vencimiento', t.fechaVencimiento, 'cal')
  ].join('');
}

function conectarNavegacion(container) {
  container.querySelector('#ir-perfil').addEventListener('click', () => {
    renderPerfil(container);
  });
  container.querySelector('#ticket-ir-escaner').addEventListener('click', () => {
    renderEscaner(container);
  });
  container.querySelector('#ticket-ir-consulta').addEventListener('click', () => {
    renderConsultaTickets(container);
  });
}
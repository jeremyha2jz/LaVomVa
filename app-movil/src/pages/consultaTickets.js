// src/pages/consultaTickets.js
import { consultarTickets } from '../services/ticketService.js';
import { renderEscaner } from './escaner.js';
import logoUrl from '../assets/lavomva-marca-blanco.png';

export function renderConsultaTickets(container) {
  container.innerHTML = `
<div class="consulta-page" lang="es">
<header class="header"><div class="brand">
  <img class="brand-logo" src="${logoUrl}" alt="" width="260" height="199">
  <p class="brand-title">LaVomVa</p>
  <p class="brand-subtitle">Despacho de combustible</p>
</div></header>
<main class="main">
  <div class="content">
    <h1 class="screen-title">Mis tickets</h1>
    <p class="screen-subtitle">Consulta el estado de tus tickets de combustible.</p>
    <p id="consulta-estado" role="status" aria-live="polite">Cargando tickets...</p><ul class="tickets-list" id="lista-tickets" aria-label="Tickets de combustible"></ul>
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
<div class="nav-wrap"><nav class="nav" aria-label="Navegación principal"><button type="button" class="nav-item" id="volver-escaner"><svg viewBox="0 0 24 24" fill="none" aria-hidden="true"><path d="M8 3H5a2 2 0 0 0-2 2v3M16 3h3a2 2 0 0 1 2 2v3M8 21H5a2 2 0 0 1-2-2v-3M16 21h3a2 2 0 0 0 2-2v-3" stroke="currentColor" stroke-width="2.3" stroke-linecap="round"/></svg><span>Escanear</span></button><button type="button" class="nav-item active" aria-current="page"><svg viewBox="0 0 24 24" fill="none" aria-hidden="true"><path d="M7 4h10M7 8h10M7 12h7M6 2.8c.8 0 1.2.9 2 .9s1.2-.9 2-.9 1.2.9 2 .9 1.2-.9 2-.9 1.2.9 2 .9 1.2-.9 2-.9V21c-.8 0-1.2-.9-2-.9s-1.2.9-2 .9-1.2-.9-2-.9-1.2.9-2 .9-1.2-.9-2-.9-1.2.9-2 .9V2.8Z" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"/></svg><span>Tickets</span></button><button type="button" class="nav-item" disabled title="Perfil aún no disponible"><svg viewBox="0 0 24 24" fill="none" aria-hidden="true"><circle cx="12" cy="8" r="4" stroke="currentColor" stroke-width="2"/><path d="M4.5 21c.9-4.3 3.5-6.5 7.5-6.5s6.6 2.2 7.5 6.5" stroke="currentColor" stroke-width="2" stroke-linecap="round"/></svg><span>Perfil</span></button></nav></div>

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

    lista.innerHTML = tickets.map(t => {
      const estado = estadoVisual(t.estado);
      return `
        <li class="ticket-item ticket-card state-${estado}">
          <div class="ticket-top">
            <div class="ticket-id-wrap">${icono('tag')}
              <div><div class="info-label">ID del ticket</div><div class="ticket-id">${escaparTexto(t.id)}</div></div>
            </div>
            <span class="status ${estado}">${escaparTexto(t.estado)}</span>
          </div>
          <div class="ticket-meta">
            <div class="meta-item"><div class="meta-label">${icono('car')}Vehículo</div><div class="meta-value">${escaparTexto(t.vehiculo)}</div></div>
            <div class="meta-item"><div class="meta-label">${icono('pump')}Autorizado</div><div class="meta-value">${escaparTexto(t.cantidadAutorizada)} galones</div></div>
            <div class="meta-item"><div class="meta-label">${icono('cal')}Vence el</div><div class="meta-value">${escaparTexto(t.fechaVencimiento)}</div></div>
          </div>
        </li>`;
    }).join('');
  }).catch((err) => {
    estadoMsg.textContent = "Error al cargar tickets";
    estadoMsg.classList.add('is-error');
  });

  container.querySelector('#volver-escaner').addEventListener('click', () => {
    renderEscaner(container);
  });
}
const ICONOS = {"tag": "<path d=\"M20.59 13.41l-7.17 7.17a2 2 0 0 1-2.83 0L2 12V2h10l8.59 8.59a2 2 0 0 1 0 2.82z\" stroke=\"currentColor\" stroke-width=\"2\" stroke-linejoin=\"round\"/><path d=\"M7 7h.01\" stroke=\"currentColor\" stroke-width=\"2.8\" stroke-linecap=\"round\"/>", "car": "<path d=\"M5 16v-5l2-5h10l2 5v5M7 16v2M17 16v2M5 12h14M8 12h.01M16 12h.01\" stroke=\"currentColor\" stroke-width=\"2\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/>", "pump": "<path d=\"M7 4h9v16H7zM16 7h2c2 0 3 1 3 3v6\" stroke=\"currentColor\" stroke-width=\"2\" stroke-linecap=\"round\"/>", "cal": "<path d=\"M5 6h14v14H5zM8 3v6M16 3v6M5 10h14\" stroke=\"currentColor\" stroke-width=\"2\" stroke-linecap=\"round\"/>"};

function icono(nombre) {
  return `<svg viewBox="0 0 24 24" fill="none" aria-hidden="true">${ICONOS[nombre]}</svg>`;
}

function escaparTexto(valor) {
  return String(valor ?? '').replace(/[&<>"']/g, caracter => ({
    '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;'
  }[caracter]));
}

function estadoVisual(estado) {
  switch (String(estado ?? '').trim().toLowerCase()) {
    case 'pendiente': return 'pending';
    case 'vencido': return 'expired';
    default: return 'used';
  }
}

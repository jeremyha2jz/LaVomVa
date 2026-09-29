import { renderPerfil } from './perfil.js';
// src/pages/consultaTickets.js
import { consultarTickets, consultarDetalleTicket } from '../services/ticketService.js';
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
    <section id="consulta-listado">
    <label class="ticket-buscador">
      <svg viewBox="0 0 24 24" fill="none" aria-hidden="true"><circle cx="10" cy="10" r="6" stroke="currentColor" stroke-width="2"/><path d="m15 15 6 6" stroke="currentColor" stroke-width="2"/></svg>
      <input id="buscar-tickets" type="search" aria-label="Buscar por correlativo, empleado o vehículo" placeholder="Buscar ticket, empleado o vehículo">
    </label>
    <div class="ticket-filtros" role="group" aria-label="Filtrar tickets por estado">
      ${['TODOS', 'CREADO', 'ENVIADO', 'PENDIENTE', 'PROXIMO_A_VENCER', 'VENCIDO', 'CONSUMIDO', 'ANULADO'].map(estado => `<button type="button" class="ticket-filtro" data-estado="${estado}" aria-pressed="${estado === 'TODOS'}">${estado === 'TODOS' ? 'Todos' : textoEstado(estado)}</button>`).join('')}
    </div>
    <p id="consulta-estado" role="status" aria-live="polite">Cargando tickets...</p><ul class="tickets-list" id="lista-tickets" aria-label="Tickets de combustible"></ul>
    </section>
    <section id="consulta-detalle" hidden aria-label="Detalle del ticket">
      <button type="button" class="ticket-atras">← Atrás</button>
      <h2 tabindex="-1">Detalle del ticket</h2>
      <p class="detalle-estado" role="status" aria-live="polite"></p>
      <div class="detalle-contenido"></div>
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
<div class="nav-wrap"><nav class="nav" aria-label="Navegación principal"><button type="button" class="nav-item" id="volver-escaner"><svg viewBox="0 0 24 24" fill="none" aria-hidden="true"><path d="M8 3H5a2 2 0 0 0-2 2v3M16 3h3a2 2 0 0 1 2 2v3M8 21H5a2 2 0 0 1-2-2v-3M16 21h3a2 2 0 0 0 2-2v-3" stroke="currentColor" stroke-width="2.3" stroke-linecap="round"/></svg><span>Escanear</span></button><button type="button" class="nav-item active" aria-current="page"><svg viewBox="0 0 24 24" fill="none" aria-hidden="true"><path d="M7 4h10M7 8h10M7 12h7M6 2.8c.8 0 1.2.9 2 .9s1.2-.9 2-.9 1.2.9 2 .9 1.2-.9 2-.9 1.2.9 2 .9 1.2-.9 2-.9V21c-.8 0-1.2-.9-2-.9s-1.2.9-2 .9-1.2-.9-2-.9-1.2.9-2 .9-1.2-.9-2-.9-1.2.9-2 .9V2.8Z" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"/></svg><span>Tickets</span></button><button type="button" class="nav-item" id="ir-perfil"><svg viewBox="0 0 24 24" fill="none" aria-hidden="true"><circle cx="12" cy="8" r="4" stroke="currentColor" stroke-width="2"/><path d="M4.5 21c.9-4.3 3.5-6.5 7.5-6.5s6.6 2.2 7.5 6.5" stroke="currentColor" stroke-width="2" stroke-linecap="round"/></svg><span>Perfil</span></button></nav></div>

</div>
`;

  const estadoMsg = container.querySelector('#consulta-estado');
  const lista = container.querySelector('#lista-tickets');
  const filtros = container.querySelectorAll('.ticket-filtro');
  let ticketsRecibidos = null;
  let filtroActivo = 'TODOS';
  const buscador = container.querySelector('#buscar-tickets');
  const listado = container.querySelector('#consulta-listado');
  const detalle = container.querySelector('#consulta-detalle');
  const detalleEstado = detalle.querySelector('.detalle-estado');
  const detalleContenido = detalle.querySelector('.detalle-contenido');
  let solicitudDetalle = 0;
  let tarjetaOrigen;
  let posicionLista = 0;

  function mostrarTickets() {
    // Mantener carga/error mientras todavía no haya una respuesta válida.
    if (ticketsRecibidos === null) return;
    const busqueda = normalizarBusqueda(buscador.value);
    const tickets = ticketsRecibidos.filter(t =>
      (filtroActivo === 'TODOS' || normalizarEstado(t.estado) === filtroActivo) &&
      [t.numeroSecuencial, textoDescriptivo(t.empleado), textoDescriptivo(t.vehiculo)].some(valor => normalizarBusqueda(valor).includes(busqueda)));
    estadoMsg.textContent = "";
    lista.innerHTML = '';

    if (!tickets.length) {
      estadoMsg.textContent = ticketsRecibidos.length && busqueda
        ? 'No hay tickets que coincidan con la búsqueda.' : filtroActivo === 'TODOS'
        ? 'No hay tickets registrados' : 'No hay tickets con este estado.';
      return;
    }

    lista.innerHTML = tickets.map(t => {
      const estado = estadoVisual(t.estado);
      return `
        <li class="ticket-item ticket-card state-${estado}" role="button" tabindex="0" data-ticket-id="${escaparTexto(t.id)}" aria-label="Ver detalle de ${escaparTexto(t.numeroSecuencial)}">
          <div class="ticket-top">
            <div class="ticket-id-wrap">${icono('tag')}
              <div><div class="info-label">ID del ticket</div><div class="ticket-id">${escaparTexto(t.numeroSecuencial)}</div></div>
            </div>
            <span class="status ${estado}">${escaparTexto(textoEstado(t.estado))}</span>
          </div>
          <div class="ticket-meta">
            <div class="meta-item"><div class="meta-label">${icono('car')}Vehículo</div><div class="meta-value">${escaparTexto(t.vehiculo)}</div></div>
            <div class="meta-item"><div class="meta-label">${icono('pump')}Autorizado</div><div class="meta-value">${escaparTexto(t.cantidadAutorizadaGalones)} galones</div></div>
            <div class="meta-item"><div class="meta-label">${icono('cal')}Vence el</div><div class="meta-value">${escaparTexto(formatearFecha(t.fechaVencimiento))}</div></div>
          </div>
        </li>`;
    }).join('');
  }

  buscador.addEventListener('input', mostrarTickets);

  async function abrirDetalle(tarjeta) {
    tarjetaOrigen = tarjeta;
    posicionLista = window.scrollY;
    const solicitud = ++solicitudDetalle;
    listado.hidden = true;
    detalle.hidden = false;
    detalleEstado.classList.remove('is-error');
    detalleEstado.textContent = 'Cargando detalle...';
    detalleContenido.innerHTML = '';
    detalle.querySelector('h2').focus();
    try {
      const seleccionado = ticketsRecibidos.find(t => String(t.id) === tarjeta.dataset.ticketId);
      const respuesta = await consultarDetalleTicket(tarjeta.dataset.ticketId);
      if (solicitud !== solicitudDetalle || !detalle.isConnected) return;
      const ticket = { ...seleccionado, ...respuesta };
      // El detalle puede contener solo IDs; conservar los nombres del listado.
      for (const campo of ['empleado', 'vehiculo', 'departamento', 'tipoCombustible']) {
        ticket[campo] = textoDescriptivo(respuesta[campo]) || textoDescriptivo(seleccionado?.[campo]);
      }
      const campos = [
        ['Correlativo', ticket.numeroSecuencial], ['Estado', textoEstado(ticket.estado)],
        ['Empleado', ticket.empleado], ['Vehículo', ticket.vehiculo],
        ['Departamento', ticket.departamento], ['Combustible', ticket.tipoCombustible],
        ['Galones autorizados', ticket.cantidadAutorizadaGalones],
        ['Fecha de creación', ticket.fechaCreacion ? formatearFecha(ticket.fechaCreacion) : null],
        ['Fecha de vencimiento', ticket.fechaVencimiento ? formatearFecha(ticket.fechaVencimiento) : null],
        ['Motivo de anulación', ticket.motivoAnulacion]
      ];
      detalleContenido.innerHTML = `<dl class="ticket-detalle-datos">${campos
        .filter(([, valor]) => valor != null && valor !== '')
        .map(([nombre, valor]) => `<div><dt>${nombre}</dt><dd>${escaparTexto(valor)}</dd></div>`).join('')}</dl>`;
      detalleEstado.textContent = '';
    } catch (err) {
      if (solicitud !== solicitudDetalle || !detalle.isConnected) return;
      const mensajes = { 401: 'Sesión inválida o expirada.', 403: 'No tienes permisos para consultar este ticket.', 404: 'El ticket no está disponible.', 500: 'No se pudo cargar el detalle por un error del servidor.' };
      detalleEstado.textContent = mensajes[err.status] || (err instanceof TypeError
        ? 'No se pudo conectar con el servidor. Comprueba tu conexión e inténtalo nuevamente.'
        : err.message || 'No se pudo cargar el detalle.');
      detalleEstado.classList.add('is-error');
    }
  }

  lista.addEventListener('click', event => {
    const tarjeta = event.target.closest('[data-ticket-id]');
    if (tarjeta) abrirDetalle(tarjeta);
  });
  lista.addEventListener('keydown', event => {
    const tarjeta = event.target.closest('[data-ticket-id]');
    if (tarjeta && (event.key === 'Enter' || event.key === ' ')) {
      event.preventDefault();
      abrirDetalle(tarjeta);
    }
  });
  detalle.querySelector('.ticket-atras').addEventListener('click', () => {
    solicitudDetalle++;
    detalle.hidden = true;
    listado.hidden = false;
    tarjetaOrigen?.focus({ preventScroll: true });
    window.scrollTo(0, posicionLista);
  });

  filtros.forEach(boton => {
    boton.addEventListener('click', () => {
      filtroActivo = boton.dataset.estado;
      filtros.forEach(filtro => filtro.setAttribute('aria-pressed', String(filtro.dataset.estado === filtroActivo)));
      mostrarTickets();
    });
  });

  consultarTickets().then((tickets) => {
    ticketsRecibidos = tickets;
    mostrarTickets();
  }).catch((err) => {
    const contexto = { 401: 'Sesión inválida o expirada', 403: 'Sin permisos para consultar tickets', 500: 'Error del servidor al cargar tickets' };
    estadoMsg.textContent = err.status
      ? `${contexto[err.status] || 'Error al cargar tickets'} (${err.status}): ${err.message}`
      : err instanceof TypeError ? 'No se pudo conectar con el servidor. Comprueba tu conexión y vuelve a abrir Tickets.'
        : err.message || 'Error al cargar tickets';
    estadoMsg.classList.add('is-error');
  });

  container.querySelector('#ir-perfil').addEventListener('click', () => {
    renderPerfil(container);
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
  switch (normalizarEstado(estado)) {
    case 'CREADO':
    case 'ENVIADO':
    case 'PENDIENTE': return 'pending';
    case 'PROXIMO_A_VENCER':
    case 'VENCIDO':
    case 'ANULADO': return 'expired';
    case 'CONSUMIDO': return 'used';
    default: return 'used';
  }
}

function normalizarEstado(estado) {
  return String(estado ?? '').trim().normalize('NFD').replace(/[\u0300-\u036f]/g, '').toUpperCase().replace(/\s+/g, '_');
}

function normalizarBusqueda(valor) {
  return String(valor ?? '').trim().normalize('NFD').replace(/[\u0300-\u036f]/g, '').toLowerCase();
}

function textoDescriptivo(valor) {
  if (typeof valor === 'string') return valor.trim();
  if (!valor || typeof valor !== 'object') return '';
  // No convertir objetos a "[object Object]" ni presentar IDs como nombres.
  return typeof valor.nombre === 'string' ? valor.nombre.trim()
    : typeof valor.placa === 'string' ? valor.placa.trim() : '';
}

function textoEstado(estado) {
  const textos = new Map([
    ['CREADO', 'Creado'], ['ENVIADO', 'Enviado'], ['PENDIENTE', 'Pendiente'],
    ['PROXIMO_A_VENCER', 'Próximo a vencer'], ['VENCIDO', 'Vencido'],
    ['CONSUMIDO', 'Consumido'], ['ANULADO', 'Anulado']
  ]);
  return textos.get(normalizarEstado(estado)) ?? estado;
}

function formatearFecha(valor) {
  if (!valor) return 'No disponible';
  // Una fecha sin hora es un día de calendario, no un instante UTC.
  const fecha = new Date(/^\d{4}-\d{2}-\d{2}$/.test(valor) ? `${valor}T00:00:00` : valor);
  return Number.isNaN(fecha.getTime()) ? 'No disponible'
    : new Intl.DateTimeFormat('es', { day: '2-digit', month: '2-digit', year: 'numeric' }).format(fecha);
}

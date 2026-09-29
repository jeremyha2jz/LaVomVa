import { renderPerfil } from './perfil.js';
// src/pages/despacho.js
import { registrarDespacho, obtenerTanquesDespacho } from '../services/ticketService.js';
import { renderEscaner } from './escaner.js';
import { renderConsultaTickets } from './consultaTickets.js';
import logoUrl from '../assets/lavomva-marca-blanco.png';

export function renderDespacho(container, ticket) {
  container.innerHTML = `
<div class="despacho-page" lang="es">
<header class="header"><div class="brand">
  <img class="brand-logo" src="${logoUrl}" alt="" width="260" height="199">
  <p class="brand-title">LaVomVa</p>
  <p class="brand-subtitle">Despacho de combustible</p>
</div></header>
<main class="main">
  <div class="content">
    <h1 class="screen-title">Registrar despacho</h1>
    <p class="screen-subtitle">Completa la información del despacho.</p>
    <div class="ticket-ref"><svg viewBox="0 0 24 24" fill="none" aria-hidden="true"><path d="M20.59 13.41l-7.17 7.17a2 2 0 0 1-2.83 0L2 12V2h10l8.59 8.59a2 2 0 0 1 0 2.82z" stroke="currentColor" stroke-width="2" stroke-linejoin="round"/><path d="M7 7h.01" stroke="currentColor" stroke-width="2.8" stroke-linecap="round"/></svg><div><div class="info-label">ID del ticket</div><div class="info-value" id="ticketId"></div></div></div>
    <form class="form-stack" id="despacho-form">
      <div class="field">
        <label class="field-label" for="galones">Galones servidos <span class="required">*</span></label>
        <div class="input-wrap"><svg viewBox="0 0 24 24" fill="none" aria-hidden="true"><path d="M7 4h9v16H7zM16 7h2c2 0 3 1 3 3v6" stroke="currentColor" stroke-width="2" stroke-linecap="round"/></svg><input id="galones" name="galones" type="number" inputmode="decimal" min="0.1" step="0.1" max="${ticket.cantidadAutorizada}" placeholder="0.0" required><span class="unit">gal</span></div>
      </div>
      <div class="field">
        <label class="field-label" for="observaciones">Observaciones <span style="font-weight:400">(opcional)</span></label>
        <div class="input-wrap textarea"><svg viewBox="0 0 24 24" fill="none" aria-hidden="true"><path d="M6 3h9l3 3v15H6zM14 3v4h4M9 12h6M9 16h5" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/></svg><textarea id="observaciones" placeholder="Escribe una observación..."></textarea></div>
      </div>
      <div class="field">
        <label class="field-label" for="tanque">Tanque</label>
        <select class="input-wrap" id="tanque" required disabled style="width:100%;font:inherit;color:inherit"><option value="">Cargando tanques...</option></select>
        <p id="tanque-estado" role="status"></p>
        <button class="action action-secondary" id="reintentar-tanques" type="button" hidden>Reintentar carga de tanques</button>
      </div>
      <label class="field-label"><input id="identidad-confirmada" type="checkbox" required> He verificado la identidad</label>
      <div class="stack">
        <button class="action action-primary" id="confirmar-despacho" type="submit"><svg viewBox="0 0 32 32" fill="none" aria-hidden="true"><path d="M8 5.5h12v21H8z" fill="currentColor"/><path d="M20 9.5h2.1c2 0 3.4 1.7 3.4 3.7v7.1c0 1.4 1 2.5 2.3 2.5 1.2 0 2.2-1 2.2-2.3v-7" stroke="currentColor" stroke-width="2" stroke-linecap="round"/></svg>Confirmar despacho</button>
        <button class="action action-secondary" id="cancelar-despacho" type="button"><svg viewBox="0 0 24 24" fill="none" aria-hidden="true"><path d="M6 6l12 12M18 6L6 18" stroke="currentColor" stroke-width="2.4" stroke-linecap="round"/></svg>Cancelar</button>
      </div>
    </form><p id="despacho-estado" role="status" aria-live="polite"></p>
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
<div class="nav-wrap"><nav class="nav" aria-label="Navegación principal"><button type="button" class="nav-item active" aria-current="page" id="despacho-ir-escaner"><svg viewBox="0 0 24 24" fill="none" aria-hidden="true"><path d="M8 3H5a2 2 0 0 0-2 2v3M16 3h3a2 2 0 0 1 2 2v3M8 21H5a2 2 0 0 1-2-2v-3M16 21h3a2 2 0 0 0 2-2v-3" stroke="currentColor" stroke-width="2.3" stroke-linecap="round"/></svg><span>Escanear</span></button><button type="button" class="nav-item" id="despacho-ir-consulta"><svg viewBox="0 0 24 24" fill="none" aria-hidden="true"><path d="M7 4h10M7 8h10M7 12h7M6 2.8c.8 0 1.2.9 2 .9s1.2-.9 2-.9 1.2.9 2 .9 1.2-.9 2-.9 1.2.9 2 .9 1.2-.9 2-.9V21c-.8 0-1.2-.9-2-.9s-1.2.9-2 .9-1.2-.9-2-.9-1.2.9-2 .9-1.2-.9-2-.9-1.2.9-2 .9V2.8Z" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"/></svg><span>Tickets</span></button><button type="button" class="nav-item" id="ir-perfil"><svg viewBox="0 0 24 24" fill="none" aria-hidden="true"><circle cx="12" cy="8" r="4" stroke="currentColor" stroke-width="2"/><path d="M4.5 21c.9-4.3 3.5-6.5 7.5-6.5s6.6 2.2 7.5 6.5" stroke="currentColor" stroke-width="2" stroke-linecap="round"/></svg><span>Perfil</span></button></nav></div>

<div class="modal-backdrop" id="modal-confirmacion" aria-hidden="true">
  <div class="modal" role="dialog" aria-modal="true" aria-labelledby="confirmTitle" aria-describedby="modal-texto">
    <div class="confirm-badge"><svg viewBox="0 0 32 32" fill="none" aria-hidden="true"><path d="M8 5.5h12v21H8z" fill="currentColor"/><path d="M20 9.5h2.1c2 0 3.4 1.7 3.4 3.7v7.1c0 1.4 1 2.5 2.3 2.5 1.2 0 2.2-1 2.2-2.3v-7" stroke="currentColor" stroke-width="2" stroke-linecap="round"/></svg></div>
    <h3 id="confirmTitle">Confirmar despacho</h3>
    <p id="modal-texto"></p>
    <div class="stack">
      <button class="action action-primary" id="modal-si" type="button"><svg viewBox="0 0 32 32" fill="none" aria-hidden="true"><path d="M8 5.5h12v21H8z" fill="currentColor"/><path d="M20 9.5h2.1c2 0 3.4 1.7 3.4 3.7v7.1c0 1.4 1 2.5 2.3 2.5 1.2 0 2.2-1 2.2-2.3v-7" stroke="currentColor" stroke-width="2" stroke-linecap="round"/></svg>Sí, registrar</button>
      <button class="action action-secondary" id="modal-no" type="button"><svg viewBox="0 0 24 24" fill="none" aria-hidden="true"><path d="M6 6l12 12M18 6L6 18" stroke="currentColor" stroke-width="2.4" stroke-linecap="round"/></svg>Cancelar</button>
    </div>
  </div>
</div>
</div>`;

  const form = container.querySelector('#despacho-form');
  const estadoMsg = container.querySelector('#despacho-estado');
  const modal = container.querySelector('#modal-confirmacion');
  const modalTexto = container.querySelector('#modal-texto');
  const btnSi = container.querySelector('#modal-si');
  const btnNo = container.querySelector('#modal-no');
  const btnConfirmar = container.querySelector('#confirmar-despacho');
  const btnCancelar = container.querySelector('#cancelar-despacho');
  const btnEscaner = container.querySelector('#despacho-ir-escaner');
  const btnTickets = container.querySelector('#despacho-ir-consulta');
  const btnPerfil = container.querySelector('#ir-perfil');
  const pantalla = container.querySelector('.despacho-page');
  const selectorTanque = container.querySelector('#tanque');
  const mensajeTanque = container.querySelector('#tanque-estado');
  const reintentarTanques = container.querySelector('#reintentar-tanques');
  const identidad = container.querySelector('#identidad-confirmada');
  let tanques = [];
  let cargandoTanques = false;
  const fondoModal = [container.querySelector('.header'), container.querySelector('.main'), container.querySelector('.nav-wrap')];
  let ocupado = false;
  let registrado = false;
  container.querySelector('#ticketId').textContent = ticket.id;
  container.querySelector('#galones').min = '0.01';
  container.querySelector('#galones').step = '0.01';

  async function cargarTanques() {
    if (cargandoTanques || ocupado || registrado) return;
    cargandoTanques = true;
    btnConfirmar.disabled = true;
    selectorTanque.disabled = true;
    reintentarTanques.hidden = true;
    mensajeTanque.textContent = 'Cargando tanques compatibles...';
    try {
      tanques = await obtenerTanquesDespacho(ticket);
      if (!container.contains(pantalla)) return;
      selectorTanque.replaceChildren();
      const opcion = document.createElement('option');
      opcion.value = '';
      opcion.textContent = 'Selecciona un tanque';
      selectorTanque.append(opcion);
      tanques.forEach(t => {
        const elemento = document.createElement('option');
        elemento.value = String(t.id);
        elemento.textContent = `${t.nombre || t.codigo || 'Tanque'} (${t.id})`;
        selectorTanque.append(elemento);
      });
      if (tanques.length === 1) selectorTanque.value = String(tanques[0].id);
      selectorTanque.disabled = tanques.length <= 1;
      mensajeTanque.textContent = tanques.length === 0
        ? 'No hay tanques activos compatibles con el combustible del ticket. No se puede registrar el despacho.'
        : tanques.length === 1 ? 'Tanque compatible seleccionado automáticamente.' : 'Selecciona el tanque del despacho.';
    } catch (err) {
      tanques = [];
      if (!container.contains(pantalla)) return;
      mensajeTanque.textContent = err.message || 'No se pudieron cargar los tanques.';
    } finally {
      cargandoTanques = false;
      if (container.contains(pantalla)) {
        btnConfirmar.disabled = !tanques.length;
        reintentarTanques.hidden = tanques.length > 0;
      }
    }
  }
  reintentarTanques.addEventListener('click', cargarTanques);
  void cargarTanques();

  // Devuelve una Promise que se resuelve con true/false según el botón que
  // presione el usuario, para poder usar "await" como si fuera confirm() nativo.
  function pedirConfirmacion(mensaje) {
    return new Promise((resolve) => {
      modalTexto.textContent = mensaje;
      modal.style.display = "flex";
      modal.setAttribute('aria-hidden', 'false');
      const focoAnterior = document.activeElement;
      fondoModal.forEach(elemento => { elemento.inert = true; });
      btnNo.focus();

      function limpiar(respuesta) {
        modal.style.display = "none";
        modal.setAttribute('aria-hidden', 'true');
        fondoModal.forEach(elemento => { elemento.inert = false; });
        btnSi.removeEventListener('click', onSi);
        btnNo.removeEventListener('click', onNo);
        modal.removeEventListener('keydown', onTecla);
        if (focoAnterior?.isConnected) focoAnterior.focus();
        resolve(respuesta);
      }
      function onSi() { limpiar(true); }
      function onNo() { limpiar(false); }
      function onTecla(evento) {
        if (evento.key === 'Escape') {
          evento.preventDefault();
          limpiar(false);
        } else if (evento.key === 'Tab') {
          evento.preventDefault();
          (document.activeElement === btnSi ? btnNo : btnSi).focus();
        }
      }

      btnSi.addEventListener('click', onSi);
      btnNo.addEventListener('click', onNo);
      modal.addEventListener('keydown', onTecla);
    });
  }

  form.addEventListener('submit', async (e) => {
    e.preventDefault();
    if (ocupado || registrado) return;
    if (cargandoTanques || !tanques.length) return;
    estadoMsg.textContent = '';
    estadoMsg.classList.remove('is-success');
    const galones = parseFloat(container.querySelector('#galones').value);
    const observaciones = container.querySelector('#observaciones').value;
    const tanqueId = Number(selectorTanque.value);
    if (!identidad.checked) {
      estadoMsg.textContent = 'Confirma que has verificado la identidad.';
      return;
    }
    if (!tanques.some(t => t.id === tanqueId)) {
      estadoMsg.textContent = 'Selecciona un tanque compatible.';
      return;
    }

    if (!Number.isFinite(galones) || galones <= 0) {
      estadoMsg.textContent = "La cantidad de galones debe ser mayor a 0";
      return;
    }
    if (galones > ticket.cantidadAutorizada) {
      estadoMsg.textContent = "No puedes servir más de lo autorizado";
      return;
    }
    if (Math.abs(galones * 100 - Math.round(galones * 100)) > 0.000001) {
      estadoMsg.textContent = 'Usa como máximo dos decimales.';
      return;
    }

    ocupado = true;
    btnConfirmar.disabled = true;
    try {
      const confirmado = await pedirConfirmacion(
        `¿Confirmas registrar ${galones} galones para el ticket ${ticket.id}?`
      );
      if (!confirmado || !container.contains(pantalla)) return;

      form.setAttribute('aria-busy', 'true');
      [btnCancelar, btnEscaner, btnTickets, btnPerfil].forEach(boton => { boton.disabled = true; });
      const resultado = await registrarDespacho(ticket.ticketUuid || ticket.id, galones, observaciones, identidad.checked, tanqueId);
      if (!container.contains(pantalla)) return;
      registrado = true;
      estadoMsg.textContent = resultado.mensaje;
      estadoMsg.classList.add('is-success');

      form.style.display = "none";
      const confirmacion = document.createElement('section');
      confirmacion.className = 'dispatch-success card';
      confirmacion.setAttribute('aria-labelledby', 'despacho-exito-titulo');
      confirmacion.innerHTML = `
        <div class="confirm-badge" aria-hidden="true">
          <svg viewBox="0 0 48 48" fill="none"><path d="m12 25 8 8 16-18" stroke="currentColor" stroke-width="6" stroke-linecap="round" stroke-linejoin="round"/></svg>
        </div>
        <h1 id="despacho-exito-titulo" tabindex="-1">Despacho registrado</h1>
        <p class="dispatch-success-message">El despacho fue registrado correctamente.</p>
        <dl class="dispatch-success-data">
          <div><dt>ID del ticket</dt><dd id="despacho-exito-ticket"></dd></div>
          <div><dt>Galones servidos</dt><dd id="despacho-exito-galones"></dd></div>
        </dl>
      `;
      const confirmadoServidor = resultado.despacho;
      confirmacion.querySelector('#despacho-exito-ticket').textContent = ticket.id;
      confirmacion.querySelector('#despacho-exito-galones').textContent = `${confirmadoServidor?.galonesServidos ?? galones} galones`;
      if (confirmadoServidor?.fechaHora) {
        const fila = document.createElement('div');
        const etiqueta = document.createElement('dt');
        etiqueta.textContent = 'Fecha y hora';
        const valor = document.createElement('dd');
        const fecha = new Date(confirmadoServidor.fechaHora);
        valor.textContent = Number.isNaN(fecha.getTime())
          ? 'Fecha no disponible'
          : new Intl.DateTimeFormat('es', {
            day: '2-digit', month: '2-digit', year: 'numeric',
            hour: '2-digit', minute: '2-digit', hour12: true
          }).format(fecha);
        fila.append(etiqueta, valor);
        confirmacion.querySelector('.dispatch-success-data').append(fila);
      }
      const btnVolver = document.createElement('button');
      btnVolver.id = "volver-escaner";
      btnVolver.type = 'button';
      btnVolver.className = 'action action-primary';
      btnVolver.textContent = "Volver a escanear";
      confirmacion.append(btnVolver);
      container.querySelector('.content').replaceChildren(confirmacion);
      confirmacion.querySelector('#despacho-exito-titulo').focus();

      btnVolver.addEventListener('click', () => {
        renderEscaner(container);
      });
    } catch (err) {
      if (!container.contains(pantalla)) return;
      const contexto = { 400: 'Solicitud rechazada', 401: 'Sesión inválida', 403: 'Sin permisos', 404: 'Recurso no encontrado', 409: 'Conflicto de despacho' };
      estadoMsg.textContent = err.status
        ? `${contexto[err.status] || 'Error del servidor'} (${err.status}): ${err.message}`
        : err.message || 'Error al registrar el despacho';
      const volver = document.createElement('button');
      volver.type = 'button';
      volver.className = 'action action-secondary';
      volver.textContent = 'Volver a escanear';
      volver.addEventListener('click', () => { if (!ocupado) renderEscaner(container); });
      estadoMsg.append(volver);
    } finally {
      ocupado = false;
      btnConfirmar.disabled = registrado;
      form.setAttribute('aria-busy', 'false');
      [btnCancelar, btnEscaner, btnTickets, btnPerfil].forEach(boton => { boton.disabled = false; });
    }
  });

  container.querySelector('#cancelar-despacho').addEventListener('click', () => {
    if (ocupado) return;
    renderEscaner(container);
  });
  btnEscaner.addEventListener('click', () => {
    if (!ocupado) renderEscaner(container);
  });
  btnPerfil.addEventListener('click', () => {
    if (!ocupado) renderPerfil(container);
  });
  btnTickets.addEventListener('click', () => {
    if (!ocupado) renderConsultaTickets(container);
  });
}

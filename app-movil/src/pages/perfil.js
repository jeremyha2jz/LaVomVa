import { registrarPantalla } from '../navigation.js';
import { renderEscaner } from './escaner.js';
import { renderConsultaTickets } from './consultaTickets.js';
import { obtenerSesion, logout } from '../services/sessionService.js';
import logoUrl from '../assets/lavomva-marca-blanco.png';

export function renderPerfil(container) {
  registrarPantalla('perfil', () => renderPerfil(container));
  container.innerHTML = `<div class="perfil-page" lang="es"><header class="header"><div class="brand">
  <img class="brand-logo" src="${logoUrl}" alt="" width="260" height="199">
  <p class="brand-title">LaVomVa</p>
  <p class="brand-subtitle">Despacho de combustible</p>
</div></header>
<main class="main">
  <div class="content">
    <h1 class="screen-title">Perfil</h1>
    <p class="screen-subtitle">Información de la sesión actual.</p>
    <section class="card profile-card">
      <div class="avatar"><svg viewBox="0 0 24 24" fill="none" aria-hidden="true"><circle cx="12" cy="8" r="4" stroke="currentColor" stroke-width="2"/><path d="M4.5 21c.9-4.3 3.5-6.5 7.5-6.5s6.6 2.2 7.5 6.5" stroke="currentColor" stroke-width="2" stroke-linecap="round"/></svg></div>
      <div class="profile-name" id="profileName">Despachador</div>
      <div class="profile-role" id="profileRole">Despachador de combustible</div>
      <div class="profile-info">
        <div class="info-row"><svg class="info-icon" viewBox="0 0 24 24" fill="none" aria-hidden="true"><circle cx="12" cy="8" r="4" stroke="currentColor" stroke-width="2"/><path d="M4.5 21c.9-4.3 3.5-6.5 7.5-6.5s6.6 2.2 7.5 6.5" stroke="currentColor" stroke-width="2" stroke-linecap="round"/></svg><div><div class="info-label">Usuario</div><div class="info-value" id="profileUser"></div></div></div>
        <div class="info-row"><svg class="info-icon" viewBox="0 0 24 24" fill="none" aria-hidden="true"><path d="M12 3l7 3v5c0 5-3 8.5-7 10-4-1.5-7-5-7-10V6l7-3Z" stroke="currentColor" stroke-width="2" stroke-linejoin="round"/><path d="M9 12l2 2 4-4" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/></svg><div><div class="info-label">Rol</div><div class="info-value" id="profileRoleValue"></div></div></div>
      </div>
      <button class="action action-secondary" id="logoutBtn" type="button"><svg viewBox="0 0 24 24" fill="none" aria-hidden="true"><path d="M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4M16 17l5-5-5-5M21 12H9" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/></svg>Cerrar sesión</button>
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
<div class="nav-wrap"><nav class="nav" aria-label="Navegación principal"><button type="button" class="nav-item" id="perfil-ir-escaner"><svg viewBox="0 0 24 24" fill="none" aria-hidden="true"><path d="M8 3H5a2 2 0 0 0-2 2v3M16 3h3a2 2 0 0 1 2 2v3M8 21H5a2 2 0 0 1-2-2v-3M16 21h3a2 2 0 0 0 2-2v-3" stroke="currentColor" stroke-width="2.3" stroke-linecap="round"/></svg><span>Escanear</span></button><button type="button" class="nav-item" id="perfil-ir-consulta"><svg viewBox="0 0 24 24" fill="none" aria-hidden="true"><path d="M7 4h10M7 8h10M7 12h7M6 2.8c.8 0 1.2.9 2 .9s1.2-.9 2-.9 1.2.9 2 .9 1.2-.9 2-.9 1.2.9 2 .9 1.2-.9 2-.9V21c-.8 0-1.2-.9-2-.9s-1.2.9-2 .9-1.2-.9-2-.9-1.2.9-2 .9-1.2-.9-2-.9-1.2.9-2 .9V2.8Z" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"/></svg><span>Tickets</span></button><button type="button" class="nav-item active" aria-current="page"><svg viewBox="0 0 24 24" fill="none" aria-hidden="true"><circle cx="12" cy="8" r="4" stroke="currentColor" stroke-width="2"/><path d="M4.5 21c.9-4.3 3.5-6.5 7.5-6.5s6.6 2.2 7.5 6.5" stroke="currentColor" stroke-width="2" stroke-linecap="round"/></svg><span>Perfil</span></button></nav></div>
<div class="logout-backdrop" id="logout-modal" hidden>
  <section class="logout-modal" role="dialog" aria-modal="true" aria-labelledby="logout-title" aria-describedby="logout-description">
    <h2 id="logout-title">¿Deseas cerrar sesión?</h2>
    <p id="logout-description">Tendrás que iniciar sesión nuevamente para continuar.</p>
    <div class="logout-actions">
      <button class="action action-danger" id="logout-confirmar" type="button">Cerrar sesión</button>
      <button class="action action-secondary" id="logout-cancelar" type="button">Cancelar</button>
    </div>
  </section>
</div>
</div>`;

  container.querySelector('#profileUser').textContent = obtenerSesion()?.nombre || '';
  container.querySelector('#profileRoleValue').textContent = (obtenerSesion()?.rol === 'DESPACHADOR' ? 'Despachador' : obtenerSesion()?.rol) || '';
  container.querySelector('#perfil-ir-escaner').addEventListener('click', () => {
    renderEscaner(container);
  });
  container.querySelector('#perfil-ir-consulta').addEventListener('click', () => {
    renderConsultaTickets(container);
  });
  const btnLogout = container.querySelector('#logoutBtn');
  const modal = container.querySelector('#logout-modal');
  const btnConfirmar = container.querySelector('#logout-confirmar');
  const btnCancelar = container.querySelector('#logout-cancelar');
  const fondo = ['.header', '.main', '.nav-wrap'].map(selector => container.querySelector(selector));

  function cancelarLogout() {
    modal.hidden = true;
    fondo.forEach(elemento => { elemento.inert = false; });
    btnLogout.focus();
  }

  btnLogout.addEventListener('click', () => {
    modal.hidden = false;
    fondo.forEach(elemento => { elemento.inert = true; });
    btnCancelar.focus();
  });
  btnCancelar.addEventListener('click', cancelarLogout);
  modal.addEventListener('keydown', (evento) => {
    if (evento.key === 'Escape') {
      evento.preventDefault();
      cancelarLogout();
    } else if (evento.key === 'Tab') {
      evento.preventDefault();
      (document.activeElement === btnConfirmar ? btnCancelar : btnConfirmar).focus();
    }
  });
  btnConfirmar.addEventListener('click', async () => {
    btnConfirmar.disabled = true;
    await logout();
  });
}

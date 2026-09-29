// src/pages/login.js
import { login } from '../services/ticketService.js';
import logoUrl from '../assets/lavomva-marca-blanco.png';

export function renderLogin(container, onLoginExitoso) {
  container.innerHTML = `
<div class="login-page" lang="es">
<header class="header"><div class="brand">
  <img class="brand-logo" src="${logoUrl}" alt="" width="260" height="199">
  <p class="brand-title">LaVomVa</p>
  <p class="brand-subtitle">Despacho de combustible</p>
</div></header>
<main class="main">
  <div class="content">
    <h1 class="screen-title">Iniciar sesión</h1>
    <p class="screen-subtitle">Ingresa tus credenciales de despachador</p>

    <form class="login-form" id="login-form">
      <label class="login-field" for="usuario">
        <span class="lf-icon"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><circle cx="12" cy="7" r="4"/><path d="M4.5 21a7.5 7.5 0 0 1 15 0"/></svg></span>
        <span class="lf-body"><span class="lf-label">Usuario</span>
          <input id="usuario" name="usuario" type="text" autocomplete="username" placeholder="Ingresa tu usuario" required></span>
      </label>
      <label class="login-field" for="contrasena">
        <span class="lf-icon"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><rect x="5" y="10" width="14" height="11" rx="2"/><path d="M8 10V7a4 4 0 0 1 8 0v3"/><circle cx="12" cy="15" r="1" fill="currentColor" stroke="none"/><path d="M12 16v2"/></svg></span>
        <span class="lf-body"><span class="lf-label">Contraseña</span>
          <input id="contrasena" name="password" type="password" autocomplete="current-password" placeholder="••••••" required></span>
        <button class="password-toggle" id="passwordToggle" type="button" aria-label="Mostrar contraseña" aria-pressed="false"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M2.5 12s3.4-6 9.5-6 9.5 6 9.5 6-3.4 6-9.5 6S2.5 12 2.5 12Z"/><circle cx="12" cy="12" r="2.7"/></svg></button>
      </label>
      <button class="login-button" id="loginButton" type="submit"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M10 17l5-5-5-5"/><path d="M15 12H3"/><path d="M14 3h5a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2h-5"/></svg><span>Entrar</span></button>
    </form>

    <p id="login-error" class="error-message" role="alert" aria-live="polite"></p>

    <div class="internal-note">
      <div class="divider"><span class="shield" aria-hidden="true"><svg viewBox="0 0 24 24" fill="currentColor"><path d="M12 2 4.8 5.4v5.1c0 4.7 3 8.9 7.2 10.5 4.2-1.6 7.2-5.8 7.2-10.5V5.4L12 2Zm2.7 11.7h-5.4V10h.8V8.8a1.9 1.9 0 1 1 3.8 0V10h.8v3.7Zm-3-3.7h.6V8.8a.3.3 0 0 0-.6 0V10Z"/></svg></span></div>
      <p>Uso interno — Acceso restringido a despachadores</p>
    </div>
  </div>
  <svg class="watermark" viewBox="0 0 120 120" fill="none" aria-hidden="true">
  <path d="M25 16h53v88H25z" stroke="currentColor" stroke-width="10" stroke-linejoin="round"/>
  <rect x="34" y="27" width="35" height="26" rx="3" stroke="currentColor" stroke-width="8"/>
  <path d="M78 29h9c10 0 17 8 17 18v30c0 8 5 13 11 13 6 0 10-5 10-11V51" stroke="currentColor" stroke-width="9" stroke-linecap="round"/>
  <path d="M91 32l17 16" stroke="currentColor" stroke-width="9" stroke-linecap="round"/>
</svg>
  <div class="bottom-art" aria-hidden="true"></div>
</main>
</div>
  `;

  const form = container.querySelector('#login-form');
  const errorMsg = container.querySelector('#login-error');

  const password = container.querySelector('#contrasena');
  const toggle = container.querySelector('#passwordToggle');
  toggle.addEventListener('click', () => {
    const mostrar = password.type === 'password';
    password.type = mostrar ? 'text' : 'password';
    toggle.setAttribute('aria-pressed', String(mostrar));
    toggle.setAttribute('aria-label', mostrar ? 'Ocultar contraseña' : 'Mostrar contraseña');
    password.focus({ preventScroll: true });
  });
  form.addEventListener('submit', async (e) => {
    e.preventDefault(); // evita que el form recargue la página, comportamiento por defecto del HTML
    const boton = container.querySelector('#loginButton');
    if (boton.disabled) return;
    boton.disabled = true;
    errorMsg.textContent = "";

    const usuario = container.querySelector('#usuario').value;
    const contrasena = container.querySelector('#contrasena').value;

    try {
      await login(usuario, contrasena);
      onLoginExitoso(); // avisa al que llamó esta función que ya puede navegar a la siguiente pantalla
    } catch (err) {
      errorMsg.textContent = err.message;
    } finally {
      boton.disabled = false;
    }
  });
}
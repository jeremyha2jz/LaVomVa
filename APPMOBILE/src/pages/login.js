// src/pages/login.js
import { login } from '../services/ticketService.js';

export function renderLogin(container, onLoginExitoso) {
  container.innerHTML = `
    <div class="login-page">
      <h1>Iniciar sesión</h1>
      <form id="login-form">
        <label for="usuario">Usuario</label>
        <input type="text" id="usuario" placeholder="Tu usuario" autocomplete="username" required />
        <label for="contrasena">Contraseña</label>
        <input type="password" id="contrasena" placeholder="Tu contraseña" autocomplete="current-password" required />
        <button type="submit">Entrar</button>
      </form>
      <p id="login-error" class="error"></p>
    </div>
  `;

  const form = container.querySelector('#login-form');
  const errorMsg = container.querySelector('#login-error');

  form.addEventListener('submit', async (e) => {
    e.preventDefault(); // evita que el form recargue la página, comportamiento por defecto del HTML
    errorMsg.textContent = "";

    const usuario = container.querySelector('#usuario').value;
    const contrasena = container.querySelector('#contrasena').value;

    try {
      const resultado = await login(usuario, contrasena);
      // Guardamos el token y datos del usuario para usarlos en el resto de la app
      localStorage.setItem('token', resultado.token);
      if (resultado.refreshToken) localStorage.setItem('refreshToken', resultado.refreshToken);
      if (resultado.expiresAt) localStorage.setItem('expiresAt', resultado.expiresAt);
      localStorage.setItem('nombre', resultado.nombre);
      localStorage.setItem('rol', resultado.rol);

      onLoginExitoso(); // avisa al que llamó esta función que ya puede navegar a la siguiente pantalla
    } catch (err) {
      errorMsg.textContent = err.message;
    }
  });
}

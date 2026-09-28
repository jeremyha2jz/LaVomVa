// src/main.js
import './style.css';
import { renderLogin } from './pages/login.js';
import { renderEscaner } from './pages/escaner.js';

const app = document.querySelector('#app');
const showLogin = () => renderLogin(app, () => renderEscaner(app));

// Si ya hay un token guardado, saltamos el login e iniciamos directo en el escáner
if (localStorage.getItem('token')) {
  renderEscaner(app);
} else {
  showLogin();
}
window.addEventListener('lavomva-session-expired', showLogin);
if ('serviceWorker' in navigator) {
  navigator.serviceWorker.register('/sw.js');
}

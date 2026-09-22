// src/main.js
import './style.css';
import { renderLogin } from './pages/login.js';
import { renderEscaner } from './pages/escaner.js';

const app = document.querySelector('#app');

// Si ya hay un token guardado, saltamos el login e iniciamos directo en el escáner
if (localStorage.getItem('token')) {
  renderEscaner(app);
} else {
  renderLogin(app, () => {
    renderEscaner(app);
  });
}
if ('serviceWorker' in navigator) {
  navigator.serviceWorker.register('/sw.js');
}
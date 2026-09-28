(() => {
  const api = window.FuelDispatchApp = window.FuelDispatchApp || {};
  const toast = document.getElementById('toast');
  let toastTimer;

  api.showToast = (message) => {
    if (!toast) return;
    toast.textContent = message;
    toast.classList.add('show');
    clearTimeout(toastTimer);
    toastTimer = setTimeout(() => toast.classList.remove('show'), 2200);
  };

  api.navigate = (route) => {
    const detail = { route };
    if (typeof api.onNavigate === 'function') api.onNavigate(detail);
    document.dispatchEvent(new CustomEvent('fuel:navigate', { detail }));
    if (typeof api.onNavigate === 'function') return;
    const pages = { scan:'escanear_ticket.html', tickets:'mis_tickets.html', profile:'perfil.html' };
    if (pages[route]) location.href = pages[route];
  };

  document.querySelectorAll('[data-route]').forEach(btn => {
    btn.addEventListener('click', () => api.navigate(btn.dataset.route));
  });
})();

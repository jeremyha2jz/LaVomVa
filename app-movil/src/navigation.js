// El historial guarda solo índices; los callbacks y datos permanecen en memoria.
const marca = 'lavomvaNavigation';
const entradas = new Map();
let indice = 1;
let actual;
let reproduciendo = false;
let restaurando = false;
let enTransicion = false;
let dialogo;

export function registrarPantalla(clave, mostrar, antesDeVolver) {
  if (!actual) {
    const existente = history.state?.[marca];
    if (Number.isInteger(existente) && existente > 0) indice = existente;
    else {
      history.replaceState({ [marca]: 0 }, '');
      history.pushState({ [marca]: 1 }, '');
    }
  } else if (!reproduciendo && actual.clave !== clave) {
    // Login y su salida no deben permitir volver a una sesión anterior.
    if (clave === 'login' || actual.clave === 'login') {
      entradas.clear();
    } else {
      for (const numero of entradas.keys()) if (numero > indice) entradas.delete(numero);
      indice++;
      history.pushState({ [marca]: indice }, '');
    }
  }
  actual = { clave, mostrar, antesDeVolver };
  entradas.set(indice, actual);
  if (clave === 'login' && dialogo?.open) dialogo.close();
}

export function volverEnHistorial() { history.back(); }

function confirmarSalida() {
  if (!dialogo) {
    dialogo = document.createElement('dialog');
    dialogo.className = 'salida-modal';
    dialogo.setAttribute('aria-labelledby', 'salida-titulo');
    dialogo.innerHTML = `<h2 id="salida-titulo">¿Salir de LaVomVa?</h2>
      <p>¿Estás seguro de que quieres salir?</p>
      <form method="dialog"><button value="cancelar" autofocus>Cancelar</button><button value="salir">Salir</button></form>`;
    document.body.append(dialogo);
  }
  return new Promise(resolve => {
    dialogo.returnValue = 'cancelar';
    dialogo.addEventListener('close', () => resolve(dialogo.returnValue === 'salir'), { once: true });
    dialogo.showModal();
  });
}

window.addEventListener('popstate', async evento => {
  if (restaurando) { restaurando = false; return; }
  if (enTransicion || !actual) return;
  const destino = evento.state?.[marca];
  if (!Number.isInteger(destino)) return; // Permitir la salida normal del documento.
  const anterior = indice;
  const pantallaAnterior = actual;
  const restaurar = () => {
    restaurando = true;
    history.go(anterior - destino);
  };
  enTransicion = true;
  try {
    if (!entradas.has(destino)) {
      const salir = await confirmarSalida();
      if (salir) {
        // No cerrar ventanas mediante scripts ni añadir nuevas entradas.
        history.go(-(destino + 1));
      } else restaurar();
      return;
    }
    if (actual.antesDeVolver && !await actual.antesDeVolver()) {
      restaurar();
      return;
    }
    // Un logout o una navegación durante la espera invalida el destino anterior.
    if (actual !== pantallaAnterior || !entradas.has(destino)) {
      restaurar();
      return;
    }
    indice = destino;
    reproduciendo = true;
    entradas.get(destino).mostrar();
  } finally {
    reproduciendo = false;
    enTransicion = false;
  }
});

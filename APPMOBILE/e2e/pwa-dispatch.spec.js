import { test, expect, chromium, request } from '@playwright/test';
import QRCode from 'qrcode';
import { PNG } from 'pngjs';
import { execFileSync } from 'node:child_process';
import { mkdir, writeFile } from 'node:fs/promises';
import path from 'node:path';

const apiUrl = process.env.E2E_API_URL;
const pwaUrl = process.env.E2E_PWA_URL;
const tmp = process.env.E2E_TMP;
const dispatcherUser = 'e2e-despachador';
const dispatcherPassword = 'TEST ONLY E2E password 2026';
let api;
let adminToken;
let dispatcherToken;
let adminId;
let tankId;
let validTicket;
let lowStockTicket;
let expiredTicket;
let inventoryStart;
let fuelName;
const videos = new Map();

test.beforeAll(async () => {
  if (!apiUrl || !pwaUrl || !tmp) throw new Error('E2E requiere los servidores locales y PostgreSQL temporal de scripts/test-e2e.sh.');
  await mkdir(tmp, { recursive: true });
  execFileSync('dotnet', ['scripts/e2e-support/bin/Release/net8.0/E2eSupport.dll', 'reset'], { env: process.env, stdio: 'pipe' });
  api = await request.newContext({ baseURL: apiUrl });

  const bootstrap = await api.post('/api/login/inicializar-admin', { data: {
    secreto: process.env.Bootstrap__Secret,
    usuario: 'e2e-admin', correo: 'e2e-admin@example.test', nombreCompleto: 'Administrador E2E',
    contrasena: 'TEST ONLY E2E admin password 2026',
  } });
  await ensure(bootstrap, 200, 'crear admin sintético');
  const adminLogin = await api.post('/api/login', { data: { usuario: 'e2e-admin', contrasena: 'TEST ONLY E2E admin password 2026' } });
  const adminSession = await json(adminLogin, 'login admin');
  adminToken = adminSession.token;
  const authAdmin = { Authorization: `Bearer ${adminToken}` };

  const users = await api.get('/api/gestion/usuarios', { headers: authAdmin });
  const userList = await json(users, 'usuarios');
  adminId = userList.find((user) => user.nombreUsuario === 'e2e-admin').id;
  const roleResponse = await api.get('/api/catalogos/roles', { headers: authAdmin });
  const roles = await json(roleResponse, 'roles');
  const dispatcherRole = roles.find((role) => (role.nombre ?? role.Nombre) === 'DESPACHADOR');
  await ensure(await api.post('/api/gestion/usuarios', { headers: authAdmin, data: {
    nombreUsuario: dispatcherUser, correo: 'e2e-despachador@example.test', nombreCompleto: 'Despachador E2E',
    password: dispatcherPassword, telefono: null, rolId: dispatcherRole.id ?? dispatcherRole.Id,
  } }), 201, 'crear despachador sintético');
  const dispatcherLogin = await api.post('/api/login', { data: { usuario: dispatcherUser, contrasena: dispatcherPassword } });
  dispatcherToken = (await json(dispatcherLogin, 'login despachador')).token;

  const department = await json(await api.post('/api/gestion/departamentos', { headers: authAdmin, data: { codigo: 'E2E', nombre: 'Operaciones E2E', activo: true } }), 'departamento', 201);
  const employee = await json(await api.post('/api/gestion/empleados', { headers: authAdmin, data: {
    codigoEmpleado: 'E2E-001', nombreCompleto: 'Empleado Sintético E2E', cedula: '000-0000000-0', departamentoId: department.id,
    cargo: 'QA', correo: null, telefonoMovil: null, activo: true,
  } }), 'empleado', 201);
  const vehicle = await json(await api.post('/api/gestion/vehiculos', { headers: authAdmin, data: {
    placa: 'E2E-001', ficha: 'E2E-F1', marca: 'QA', modelo: 'Synthetic', anio: 2025, tipo: 'SUV',
    departamentoId: department.id, capacidadTanqueGalones: 20, odometroKm: 0, activo: true,
  } }), 'vehículo', 201);
  const station = await json(await api.post('/api/gestion/estaciones', { headers: authAdmin, data: { nombre: 'Estación E2E', ubicacion: 'Temporal', activo: true } }), 'estación', 201);
  const fuels = await json(await api.get('/api/catalogos/tipos-combustible', { headers: authAdmin }), 'combustibles');
  const fuel = fuels[0];
  fuelName = fuel.nombre ?? fuel.Nombre;
  const tank = await json(await api.post('/api/gestion/tanques', { headers: authAdmin, data: {
    codigo: 'E2E-T1', nombre: 'Tanque de prueba', estacionId: station.id, tipoCombustibleId: fuel.id,
    capacidadGalones: 40, nivelCriticoGalones: 2, activo: true,
  } }), 'tanque', 201);
  tankId = tank.id;
  await ensure(await api.post('/api/inventario/ajustes', { headers: authAdmin, data: {
    tanqueId: tankId, tipo: 'AJUSTE_POSITIVO', cantidadGalones: 8, motivo: 'Stock sintético para E2E', usuarioId: adminId,
  } }), 201, 'cargar inventario temporal');
  inventoryStart = 8;

  const expiration = new Date(Date.now() + 10 * 86_400_000).toISOString();
  validTicket = await createTicket({ department, employee, vehicle, fuel, expiration, quantity: 5, suffix: 'valid' });
  lowStockTicket = await createTicket({ department, employee, vehicle, fuel, expiration, quantity: 10, suffix: 'low' });
  expiredTicket = await createTicket({ department, employee, vehicle, fuel, expiration, quantity: 2, suffix: 'expired' });
  execFileSync('dotnet', ['scripts/e2e-support/bin/Release/net8.0/E2eSupport.dll', 'expire', expiredTicket.id], { env: process.env, stdio: 'pipe' });

  for (const [name, ticket] of [['valid', validTicket], ['low', lowStockTicket], ['expired', expiredTicket]]) {
    const response = await api.get(`/api/tickets/${ticket.id}/qr`, { headers: authAdmin });
    if (response.status() !== 200) throw new Error(`No se pudo obtener el QR del ticket ${name} (HTTP ${response.status()}).`);
    videos.set(name, await createCameraVideo(await response.body(), `${name}.y4m`));
  }
  videos.set('invalid', await createCameraVideo(await QRCode.toBuffer('TOKEN-INVALIDO-SINTETICO-E2E', { type: 'png', width: 400, margin: 4 }), 'invalid.y4m'));
  videos.set('blank', await createCameraVideo(blankPng(), 'blank.y4m'));
});

test.afterAll(async () => { await api?.dispose(); });

test('login PWA rechaza credenciales erróneas, abre sesión de despachador y logout limpia/revoca refresh', async () => {
  const session = await openPwa('blank');
  try {
    await session.page.goto(pwaUrl);
    await expect(session.page.getByRole('heading', { name: 'Iniciar sesión' })).toBeVisible();
    await session.page.getByLabel('Usuario').fill(dispatcherUser);
    await session.page.locator('#contrasena').fill('TEST ONLY contraseña incorrecta');
    await session.page.getByRole('button', { name: 'Entrar' }).click();
    await expect(session.page.locator('#login-error')).toContainText('incorrectos');

    await session.page.locator('#contrasena').fill(dispatcherPassword);
    await session.page.getByRole('button', { name: 'Entrar' }).click();
    await expect(session.page.getByRole('heading', { name: 'Escanear ticket' })).toBeVisible();
    const savedRefresh = await session.page.evaluate(() => localStorage.getItem('refreshToken'));
    expect(savedRefresh).toBeTruthy();
    await session.page.getByRole('button', { name: 'Cerrar sesión' }).click();
    await expect(session.page.getByRole('heading', { name: 'Iniciar sesión' })).toBeVisible();
    expect(await session.page.evaluate(() => localStorage.getItem('token'))).toBeNull();
    const replay = await api.post('/api/login/refresh', { data: { refreshToken: savedRefresh } });
    expect(replay.status()).toBe(401);
  } finally { await session.browser.close(); }
});

test('cámara autorizada escanea QR real de API, valida identidad, despacha, persiste inventario y rechaza ticket consumido', async () => {
  const session = await openPwa('valid');
  try {
    await loginAsDispatcher(session.page);
    await expect(session.page.getByRole('heading', { name: 'Ticket válido' })).toBeVisible({ timeout: 30_000 });
    await expect(session.page.getByText('Empleado Sintético E2E')).toBeVisible();
    await expect(session.page.locator('.ticket-page p').filter({ hasText: 'Vehículo:' })).toContainText('E2E-001');
    await expect(session.page.getByText('5 galones')).toBeVisible();
    await expect(session.page.locator('.ticket-page p').filter({ hasText: 'Combustible:' })).toContainText(fuelName);
    await expect(session.page.getByRole('button', { name: 'Registrar despacho' })).toBeVisible();
    await session.page.getByRole('button', { name: 'Registrar despacho' }).click();
    await expect(session.page.getByRole('heading', { name: 'Registrar despacho' })).toBeVisible();
    const identity = session.page.getByLabel('Confirmo que verifiqué la identidad del conductor.');
    expect(await identity.evaluate((element) => element.required && !element.checked)).toBe(true);
    expect(await session.page.locator('#despacho-form').evaluate((form) => form.checkValidity())).toBe(false);
    await session.page.getByLabel('Galones servidos').fill('5');
    await identity.check();
    await session.page.getByRole('button', { name: 'Confirmar despacho' }).click();
    await expect(session.page.locator('#despacho-estado')).toContainText('Despacho registrado');

    const dispatcherHeaders = { Authorization: `Bearer ${dispatcherToken}` };
    const persisted = await json(await api.get(`/api/tickets/${validTicket.id}`, { headers: dispatcherHeaders }), 'ticket consumido');
    expect(persisted.estado).toBe('CONSUMIDO');
    const inventory = await json(await api.get('/api/inventario', { headers: dispatcherHeaders }), 'inventario posterior al despacho');
    expect(inventory.find((row) => row.id === tankId).existenciaActualGalones).toBe(inventoryStart - 5);
    const movements = await json(await api.get(`/api/inventario/movimientos?tanqueId=${tankId}`, { headers: dispatcherHeaders }), 'movimientos persistidos');
    expect(movements.filter((row) => row.referenciaTipo === 'DESPACHO')).toHaveLength(1);

    await session.page.reload();
    await expect(session.page.getByRole('heading', { name: 'Ticket no válido' })).toBeVisible({ timeout: 30_000 });
    await expect(session.page.locator('.error')).toContainText('consumido');
  } finally { await session.browser.close(); }
});

test('permiso de cámara denegado muestra error recuperable sin error JavaScript de página', async () => {
  const session = await openPwa('blank', true);
  const errors = [];
  session.page.on('pageerror', (error) => errors.push(error.message));
  try {
    await loginAsDispatcher(session.page, false);
    await expect(session.page.locator('#escaner-estado')).toContainText('No se pudo acceder a la cámara', { timeout: 15_000 });
    await expect(session.page.getByRole('button', { name: 'Reintentar' })).toBeVisible();
    await session.page.getByRole('button', { name: 'Reintentar' }).click();
    await expect(session.page.locator('#escaner-estado')).toContainText('No se pudo acceder a la cámara');
    expect(errors).toEqual([]);
  } finally { await session.browser.close(); }
});

test('QR inválido escaneado por la cámara produce error visible y permite volver al lector', async () => {
  const session = await openPwa('invalid');
  try {
    await loginAsDispatcher(session.page);
    await expect(session.page.getByRole('heading', { name: 'Ticket no válido' })).toBeVisible({ timeout: 30_000 });
    await expect(session.page.locator('.error')).toContainText('no existe o el QR es inválido');
    await session.page.getByRole('button', { name: 'Volver a escanear' }).click();
    await expect(session.page.getByRole('heading', { name: 'Escanear ticket' })).toBeVisible();
    await expect(session.page.locator('#qr-reader video')).toBeVisible();
  } finally { await session.browser.close(); }
});

test('QR vencido escaneado por la cámara se rechaza con el estado del servidor', async () => {
  const session = await openPwa('expired');
  try {
    await loginAsDispatcher(session.page);
    await expect(session.page.getByRole('heading', { name: 'Ticket no válido' })).toBeVisible({ timeout: 30_000 });
    await expect(session.page.locator('.error')).toContainText('ya venció');
  } finally { await session.browser.close(); }
});

test('inventario insuficiente se muestra como conflicto sin consumir ticket ni llevar el stock a negativo', async () => {
  const session = await openPwa('low');
  try {
    await loginAsDispatcher(session.page);
    await expect(session.page.getByRole('heading', { name: 'Ticket válido' })).toBeVisible({ timeout: 30_000 });
    await session.page.getByRole('button', { name: 'Registrar despacho' }).click();
    await session.page.getByLabel('Galones servidos').fill('10');
    await session.page.getByLabel('Confirmo que verifiqué la identidad del conductor.').check();
    const beforeInventory = await json(await api.get('/api/inventario', { headers: { Authorization: `Bearer ${dispatcherToken}` } }), 'inventario previo al conflicto');
    const stockBefore = beforeInventory.find((row) => row.id === tankId).existenciaActualGalones;
    await session.page.getByRole('button', { name: 'Confirmar despacho' }).click();
    await expect(session.page.locator('#despacho-estado')).toContainText('Inventario insuficiente');
    const ticket = await json(await api.get(`/api/tickets/${lowStockTicket.id}`, { headers: { Authorization: `Bearer ${dispatcherToken}` } }), 'ticket después del conflicto');
    expect(ticket.estado).toBe('CREADO');
    const inventory = await json(await api.get('/api/inventario', { headers: { Authorization: `Bearer ${dispatcherToken}` } }), 'inventario después del conflicto');
    expect(inventory.find((row) => row.id === tankId).existenciaActualGalones).toBe(stockBefore);
    expect(inventory.find((row) => row.id === tankId).existenciaActualGalones).toBeGreaterThanOrEqual(0);
  } finally { await session.browser.close(); }
});

test('fallo de red al validar un QR mantiene PWA utilizable y comunica el error', async () => {
  const session = await openPwa('low');
  try {
    await loginAsDispatcher(session.page);
    await session.page.route('**/api/tickets/validar', (route) => route.abort('failed'));
    await expect(session.page.getByRole('heading', { name: 'Escanear ticket' })).toBeVisible();
    await expect(session.page.locator('#escaner-estado')).toContainText('Error al validar el ticket', { timeout: 30_000 });
    await expect(session.page.getByRole('button', { name: 'Cerrar sesión' })).toBeVisible();
  } finally { await session.browser.close(); }
});

test('manifest, iconos, service worker e integridad de la caché privada se verifican en Chromium', async () => {
  const browser = await chromium.launch({ headless: true });
  try {
    const context = await browser.newContext({ viewport: { width: 390, height: 844 } });
    const page = await context.newPage();
    await page.goto(pwaUrl);
    const manifestLink = await page.locator('link[rel="manifest"]').getAttribute('href');
    expect(manifestLink).toBe('/manifest.json');
    const manifest = await page.evaluate(async (href) => (await (await fetch(href)).json()), manifestLink);
    expect(manifest.start_url).toBe('/');
    expect(manifest.display).toBe('standalone');
    for (const icon of manifest.icons) expect((await page.request.get(new URL(icon.src, pwaUrl).toString())).status()).toBe(200);
    expect(await page.evaluate(async () => Boolean(await navigator.serviceWorker.ready))).toBe(true);
    await page.evaluate(() => fetch('/api/login', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: '{}' }).catch(() => {}));
    expect(await page.evaluate(() => caches.match('/api/login').then(Boolean))).toBe(false);
    await context.close();
  } finally { await browser.close(); }
});

async function openPwa(videoName, cameraDenied = false) {
  const args = cameraDenied ? [] : [
    '--use-fake-device-for-media-stream', '--use-fake-ui-for-media-stream',
    `--use-file-for-fake-video-capture=${videos.get(videoName)}`,
  ];
  const browser = await chromium.launch({ headless: true, args });
  const context = await browser.newContext({
    viewport: { width: 390, height: 844 },
    permissions: cameraDenied ? [] : ['camera'],
    serviceWorkers: 'block',
  });
  if (cameraDenied) {
    const page = await context.newPage();
    const cdp = await context.newCDPSession(page);
    await cdp.send('Browser.setPermission', { permission: { name: 'camera' }, setting: 'denied', origin: pwaUrl });
    return { browser, context, page };
  }
  return { browser, context, page: await context.newPage() };
}

async function loginAsDispatcher(page, expectVideo = true) {
  await page.goto(pwaUrl);
  await page.locator('#usuario').fill(dispatcherUser);
  await page.locator('#contrasena').fill(dispatcherPassword);
  await page.getByRole('button', { name: 'Entrar' }).click();
  await expect(page.getByRole('heading', { name: 'Escanear ticket' })).toBeVisible();
  if (expectVideo) await expect(page.locator('#qr-reader video')).toBeVisible({ timeout: 15_000 });
}

async function createTicket({ department, employee, vehicle, fuel, expiration, quantity, suffix }) {
  const headers = { Authorization: `Bearer ${adminToken}` };
  const requestResponse = await api.post('/api/solicitudes', { headers, data: {
    empleadoId: employee.id, vehiculoId: vehicle.id, departamentoId: department.id, tipoCombustibleId: fuel.id,
    cantidadSolicitadaGalones: quantity, fechaVencimiento: expiration, tipoSolicitud: 'MANUAL', motivo: `E2E ${suffix}`,
  } });
  const solicitud = await json(requestResponse, `solicitud ${suffix}`, 201);
  await ensure(await api.put(`/api/solicitudes/${solicitud.id}/aprobar`, { headers, data: {
    cantidadAutorizadaGalones: quantity, fechaVencimiento: expiration, usuarioAprobadorId: adminId,
  } }), 200, `aprobación ${suffix}`);
  return json(await api.post('/api/tickets', { headers, data: { solicitudId: solicitud.id, usuarioEmisorId: adminId } }), `ticket ${suffix}`, 201);
}

async function json(response, label, expectedStatus = 200) {
  await ensure(response, expectedStatus, label);
  return response.json();
}

async function ensure(response, expectedStatus, label) {
  if (response.status() !== expectedStatus) throw new Error(`${label}: se esperaba HTTP ${expectedStatus}, se obtuvo ${response.status()}: ${(await response.text()).slice(0, 300)}`);
}

function blankPng() {
  const png = new PNG({ width: 400, height: 400 });
  for (let index = 0; index < png.data.length; index += 4) { png.data[index] = 255; png.data[index + 1] = 255; png.data[index + 2] = 255; png.data[index + 3] = 255; }
  return PNG.sync.write(png);
}

async function createCameraVideo(sourcePng, filename) {
  const width = 640;
  const height = 480;
  const source = PNG.sync.read(Buffer.from(sourcePng));
  // Keep the complete QR (including its quiet zone) inside the PWA's 250px scan box.
  const scale = Math.min(220 / source.width, 220 / source.height);
  const drawWidth = Math.round(source.width * scale);
  const drawHeight = Math.round(source.height * scale);
  const rgba = Buffer.alloc(width * height * 4, 255);
  const offsetX = Math.floor((width - drawWidth) / 2);
  const offsetY = Math.floor((height - drawHeight) / 2);
  for (let y = 0; y < drawHeight; y++) for (let x = 0; x < drawWidth; x++) {
    const sx = Math.min(source.width - 1, Math.floor(x / scale));
    const sy = Math.min(source.height - 1, Math.floor(y / scale));
    const from = (sy * source.width + sx) * 4;
    const to = ((offsetY + y) * width + offsetX + x) * 4;
    rgba[to] = source.data[from]; rgba[to + 1] = source.data[from + 1]; rgba[to + 2] = source.data[from + 2]; rgba[to + 3] = 255;
  }
  const yPlane = Buffer.alloc(width * height);
  const uPlane = Buffer.alloc(width * height / 4);
  const vPlane = Buffer.alloc(width * height / 4);
  for (let y = 0; y < height; y++) for (let x = 0; x < width; x++) {
    const i = (y * width + x) * 4;
    yPlane[y * width + x] = clamp(16 + 0.257 * rgba[i] + 0.504 * rgba[i + 1] + 0.098 * rgba[i + 2]);
  }
  for (let y = 0; y < height; y += 2) for (let x = 0; x < width; x += 2) {
    let red = 0, green = 0, blue = 0;
    for (let dy = 0; dy < 2; dy++) for (let dx = 0; dx < 2; dx++) {
      const i = ((y + dy) * width + x + dx) * 4;
      red += rgba[i]; green += rgba[i + 1]; blue += rgba[i + 2];
    }
    red /= 4; green /= 4; blue /= 4;
    const i = (y / 2) * (width / 2) + x / 2;
    uPlane[i] = clamp(128 - 0.148 * red - 0.291 * green + 0.439 * blue);
    vPlane[i] = clamp(128 + 0.439 * red - 0.368 * green - 0.071 * blue);
  }
  const frame = Buffer.concat([Buffer.from('FRAME\n'), yPlane, uPlane, vPlane]);
  const video = path.join(tmp, filename);
  await writeFile(video, Buffer.concat([Buffer.from('YUV4MPEG2 W640 H480 F10:1 Ip A1:1 C420jpeg\n'), ...Array(60).fill(frame)]));
  return video;
}

function clamp(value) { return Math.max(0, Math.min(255, Math.round(value))); }

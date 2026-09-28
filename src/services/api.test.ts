/** @vitest-environment jsdom */
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { api, clearSession, exportReport, getReport, loadLiveData, login, logout, reconcileTicketDelivery, register, retryTicketDelivery, savedSession, sendTicket, ticketDeliveryHistory, ticketQr } from './api'

const fetchMock = vi.fn<typeof fetch>()

function response(body: unknown, status = 200, contentType = 'application/json') {
  return new Response(contentType === 'application/json' ? JSON.stringify(body) : String(body), { status, headers: { 'Content-Type': contentType } })
}

beforeEach(() => { sessionStorage.clear(); fetchMock.mockReset(); vi.stubGlobal('fetch', fetchMock) })
afterEach(() => { vi.unstubAllGlobals() })

describe('servicio API web', () => {
  it('consulta reportes con los filtros y parámetros de paginación codificados', async () => {
    fetchMock.mockResolvedValue(response({ items: [], totalRegistros: 0 }))
    await getReport({ tipo: 'consumo', desde: '2026-09-01', hasta: '2026-09-30', departamentoId: 4, combustibleId: 2, estado: 'CONSUMIDO', pagina: 2, tamanoPagina: 25 })
    const url = new URL(String(fetchMock.mock.calls[0][0]), window.location.origin)
    expect(url.pathname).toBe('/api/reportes')
    expect(Object.fromEntries(url.searchParams)).toEqual({ tipo: 'consumo', desde: '2026-09-01', hasta: '2026-09-30', departamentoId: '4', combustibleId: '2', estado: 'CONSUMIDO', pagina: '2', tamanoPagina: '25' })
  })

  it('descarga exportaciones con JWT, formato, filtros y nombre anunciado por la API', async () => {
    sessionStorage.setItem('lavomva-session', JSON.stringify({ token: 'report-token', id: 7, name: 'QA', role: 'AUDITOR' }))
    fetchMock.mockResolvedValue(new Response('xlsx-bytes', { headers: { 'Content-Type': 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet', 'Content-Disposition': 'attachment; filename=\"reporte-consumo-2026-09-27.xlsx\"' } }))
    const result = await exportReport({ tipo: 'consumo', estacionId: 8, desde: '2026-09-01' }, 'xlsx')
    const url = new URL(String(fetchMock.mock.calls[0][0]), window.location.origin)
    expect(url.pathname).toBe('/api/reportes/exportar')
    expect(url.searchParams.get('formato')).toBe('xlsx')
    expect(url.searchParams.get('estacionId')).toBe('8')
    expect(fetchMock.mock.calls[0][1]?.headers).toMatchObject({ Authorization: 'Bearer report-token' })
    expect(result.filename).toBe('reporte-consumo-2026-09-27.xlsx')
    expect(await result.blob.text()).toBe('xlsx-bytes')
  })

  it('guarda, recupera, limpia y descarta una sesión corrupta', () => {
    expect(savedSession()).toBeNull()
    sessionStorage.setItem('lavomva-session', '{mal json')
    expect(savedSession()).toBeNull()
    sessionStorage.setItem('lavomva-session', JSON.stringify({ token: 'qa-token', id: 8, name: 'QA', role: 'ADMINISTRADOR' }))
    expect(savedSession()?.id).toBe(8)
    clearSession()
    expect(savedSession()).toBeNull()
  })

  it('login persiste la sesión y envía credenciales como JSON', async () => {
    fetchMock.mockResolvedValue(response({ token: 'signed-qa-token', refreshToken: 'refresh-qa-token', expiresAt: '2026-09-27T18:00:00Z', id: 7, nombre: 'QA Admin', rol: 'ADMINISTRADOR' }))
    await expect(login('qa.admin', 'not-real-password')).resolves.toEqual({ token: 'signed-qa-token', refreshToken: 'refresh-qa-token', expiresAt: '2026-09-27T18:00:00Z', id: 7, name: 'QA Admin', role: 'ADMINISTRADOR' })
    expect(JSON.parse(String(fetchMock.mock.calls[0][1]?.body))).toEqual({ usuario: 'qa.admin', contrasena: 'not-real-password' })
    expect(savedSession()?.token).toBe('signed-qa-token')
  })

  it('envía token en operaciones protegidas y el registro no requiere sesión', async () => {
    sessionStorage.setItem('lavomva-session', JSON.stringify({ token: 'active-token', id: 7, name: 'QA', role: 'ADMINISTRADOR' }))
    fetchMock.mockImplementation(async () => response({ id: 3 }))
    await api.users()
    expect(fetchMock.mock.calls[0][1]?.headers).toMatchObject({ Authorization: 'Bearer active-token' })
    await register('new.qa', 'new@example.test', 'New QA', 'safe-test-only')
    const [url, init] = fetchMock.mock.calls[1]
    expect(String(url)).toContain('/login/registro')
    expect(JSON.parse(String(init?.body))).toEqual({ usuario: 'new.qa', correo: 'new@example.test', nombreCompleto: 'New QA', contrasena: 'safe-test-only' })
  })

  it('renueva una sola vez ante 401, actualiza la sesión y reintenta con el token nuevo', async () => {
    sessionStorage.setItem('lavomva-session', JSON.stringify({ token: 'old-access', refreshToken: 'old-refresh', id: 7, name: 'QA', role: 'ADMINISTRADOR' }))
    fetchMock.mockResolvedValueOnce(response({ mensaje: 'JWT vencido' }, 401))
      .mockResolvedValueOnce(response({ token: 'new-access', refreshToken: 'new-refresh', expiresAt: '2026-09-27T18:00:00Z' }))
      .mockResolvedValueOnce(response([{ id: 1, nombre: 'ADMINISTRADOR' }]))
    await expect(api.roles()).resolves.toHaveLength(1)
    expect(fetchMock).toHaveBeenCalledTimes(3)
    expect(fetchMock.mock.calls[1][0]).toBe('/api/login/refresh')
    expect(JSON.parse(String(fetchMock.mock.calls[1][1]?.body))).toEqual({ refreshToken: 'old-refresh' })
    expect(fetchMock.mock.calls[2][1]?.headers).toMatchObject({ Authorization: 'Bearer new-access' })
    expect(savedSession()).toMatchObject({ token: 'new-access', refreshToken: 'new-refresh' })
  })

  it('si refresh falla limpia sesión, notifica expiración y no repite la petición', async () => {
    sessionStorage.setItem('lavomva-session', JSON.stringify({ token: 'old-access', refreshToken: 'revoked', id: 7, name: 'QA', role: 'ADMINISTRADOR' }))
    const expired = vi.fn()
    window.addEventListener('lavomva-session-expired', expired)
    fetchMock.mockResolvedValueOnce(response({ mensaje: 'JWT vencido' }, 401)).mockResolvedValueOnce(response({ mensaje: 'Sesión no válida' }, 401))
    await expect(api.roles()).rejects.toThrow('JWT vencido')
    expect(fetchMock).toHaveBeenCalledTimes(2)
    expect(savedSession()).toBeNull()
    expect(expired).toHaveBeenCalledOnce()
    window.removeEventListener('lavomva-session-expired', expired)
  })

  it('logout revoca refresh en la API y limpia la sesión local', async () => {
    sessionStorage.setItem('lavomva-session', JSON.stringify({ token: 'access', refreshToken: 'refresh', id: 7, name: 'QA', role: 'ADMINISTRADOR' }))
    fetchMock.mockResolvedValue(new Response(null, { status: 204 }))
    await logout()
    expect(fetchMock.mock.calls[0][0]).toBe('/api/login/logout')
    expect(JSON.parse(String(fetchMock.mock.calls[0][1]?.body))).toEqual({ refreshToken: 'refresh' })
    expect(savedSession()).toBeNull()
  })

  it.each([502, 503, 504])('mapea indisponibilidad HTTP %i a un mensaje estable', async (status) => {
    fetchMock.mockResolvedValue(response('bad gateway', status, 'text/plain'))
    await expect(api.roles()).rejects.toThrow('El servidor de la aplicación no está disponible')
  })

  it('propaga errores JSON y texto, y trata 204 como respuesta vacía', async () => {
    fetchMock.mockResolvedValueOnce(response({ mensaje: 'No permitido' }, 403))
    await expect(api.roles()).rejects.toThrow('No permitido')
    fetchMock.mockResolvedValueOnce(response('Ticket bloqueado', 409, 'text/plain'))
    await expect(api.roles()).rejects.toThrow('Ticket bloqueado')
    fetchMock.mockResolvedValueOnce(new Response(null, { status: 204 }))
    await expect(api.activateUser(9)).resolves.toBeUndefined()
  })

  it('descarga QR como Blob con sesión y propaga el rechazo del endpoint', async () => {
    sessionStorage.setItem('lavomva-session', JSON.stringify({ token: 'qr-token', id: 7, name: 'QA', role: 'ADMINISTRADOR' }))
    const image = new Blob(['png-data'], { type: 'image/png' })
    fetchMock.mockResolvedValueOnce(new Response(image, { status: 200 }))
    await expect(ticketQr('ticket/with-slash')).resolves.toBeInstanceOf(Blob)
    expect(String(fetchMock.mock.calls[0][0])).toContain('ticket%2Fwith-slash/qr')
    expect(fetchMock.mock.calls[0][1]?.headers).toMatchObject({ Authorization: 'Bearer qr-token' })
    fetchMock.mockResolvedValueOnce(response('denied', 403, 'text/plain'))
    await expect(ticketQr('id')).rejects.toThrow('No se pudo cargar el QR del ticket.')
  })

  it('normaliza catálogos y movimientos desde la respuesta API y usa fallbacks de campos faltantes', async () => {
    const rows: Record<string, unknown> = {
      '/catalogos/departamentos': [{ id: 1, codigo: 'OPS', nombre: 'Operaciones', activo: true }],
      '/catalogos/empleados': [{ id: 2, codigoEmpleado: 'E-2', nombreCompleto: 'Eva QA', cedula: '000', departamentoId: 1, cargo: 'Operadora', correo: 'eva@example.test' }],
      '/catalogos/vehiculos': [{ id: 3, placa: 'A-003', ficha: 'F-3', marca: 'Marca', modelo: 'M', departamentoId: 1, capacidadTanqueGalones: 25, odometroKm: 4 }],
      '/catalogos/tipos-combustible': [{ id: 4, nombre: 'DIESEL' }],
      '/catalogos/tanques': [{ id: 5, codigo: 'T-5', estacionId: 6, tipoCombustibleId: 4, capacidadGalones: 100, existenciaActualGalones: 45, nivelCriticoGalones: 10 }],
      '/catalogos/estaciones': [{ id: 6, nombre: 'Estación Central', ubicacion: 'Santo Domingo' }],
      '/recepciones/proveedores': [{ id: 7, nombre: 'Proveedor QA', rnc: 'RNC' }],
      '/solicitudes': [{ id: 8, empleadoId: 2, vehiculoId: 3, departamentoId: 1, tipoCombustibleId: 4, cantidadSolicitadaGalones: 12, estado: 'PENDIENTE' }],
      '/tickets': [{ id: 'uuid-ticket', numeroSecuencial: 'COM-2026-000001', empleado: 'Eva QA', vehiculo: 'A-003', departamento: 'Operaciones', tipoCombustible: 'DIESEL', cantidadAutorizadaGalones: 8, estado: 'CREADO' }],
      '/inventario/movimientos': [{ id: 9, tanqueId: 5, tipoMovimiento: 'SALIDA', cantidadGalones: 3, existenciaAnterior: 48, existenciaNueva: 45, referenciaTipo: 'DESPACHO' }],
    }
    fetchMock.mockImplementation(async (input) => response(rows[new URL(String(input), window.location.origin).pathname.replace('/api', '')] ?? []))
    const data = await loadLiveData()
    expect(data.catalogs.departments[0]).toMatchObject({ id: 1, employees: 1, vehicles: 1, active: true })
    expect(data.catalogs.employees[0]).toMatchObject({ name: 'Eva QA', department: 'Operaciones', email: 'eva@example.test' })
    expect(data.catalogs.vehicles[0]).toMatchObject({ plate: 'A-003', department: 'Operaciones', tankCapacity: 25 })
    expect(data.tanks[0]).toMatchObject({ station: 'Estación Central', fuelType: 'DIESEL', stock: 45 })
    expect(data.requests[0]).toMatchObject({ employee: 'Eva QA', vehicle: 'A-003 · Marca M', reason: 'Sin motivo registrado', requestedGallons: 12 })
    expect(data.tickets[0]).toMatchObject({ id: 'uuid-ticket', sequence: 'COM-2026-000001', gallons: 8 })
    expect(data.movements[0]).toMatchObject({ tank: 'T-5', reference: 'DESPACHO', user: 'Sistema', current: 45 })
  })

  it('api serializa cambios de catálogo y operaciones sobre solicitudes e inventario', async () => {
    fetchMock.mockImplementation(async () => response({ ok: true }))
    await api.catalogCreate('empleados', { nombreCompleto: 'QA' })
    await api.catalogUpdate('vehiculos', 5, { placa: 'QA' })
    await api.catalogDeactivate('departamentos', 4)
    await api.updateUser(7, { nombreCompleto: 'QA' })
    await api.resetUserPassword(7, 'new-test-password')
    await api.deactivateUser(7)
    await api.createRequest({ cantidad: 1 })
    await api.approveRequest(9, { cantidad: 1 })
    await api.rejectRequest(10)
    await api.createTicket(9, 7)
    await api.cancelTicket('uuid/ticket', 'Duplicado')
    await api.receive({ proveedorId: 1 })
    expect(fetchMock.mock.calls.map(([url]) => String(url))).toEqual([
      '/api/gestion/empleados', '/api/gestion/vehiculos/5', '/api/gestion/departamentos/4', '/api/gestion/usuarios/7',
      '/api/gestion/usuarios/7/restablecer-contrasena', '/api/gestion/usuarios/7', '/api/solicitudes', '/api/solicitudes/9/aprobar',
      '/api/solicitudes/10/rechazar', '/api/tickets', '/api/tickets/uuid%2Fticket/anular', '/api/recepciones',
    ])
    expect(JSON.parse(fetchMock.mock.calls[10][1]?.body as string)).toEqual({ motivo: 'Duplicado' })
  })

  it('serializa cierres, filtros del historial y descarga PDF autenticada', async () => {
    sessionStorage.setItem('lavomva-session', JSON.stringify({ token: 'close-token', id: 7, name: 'QA', role: 'DESPACHADOR' }))
    fetchMock.mockImplementation(async () => response({ id: 12 }))
    await api.closeSummary(4, '2026-09-27')
    await api.createDailyClose({ estacionId: 4, fecha: '2026-09-27', inventariosFisicos: [{ tanqueId: 8, inventarioFisicoGalones: 35.5 }] })
    await api.dailyCloses({ desde: '2026-09-20', hasta: '2026-09-27', estacionId: 4, usuarioId: 7 })
    expect(fetchMock.mock.calls.slice(0, 3).map(([url]) => String(url))).toEqual([
      '/api/cierres-diarios/resumen?estacionId=4&fecha=2026-09-27', '/api/cierres-diarios',
      '/api/cierres-diarios?desde=2026-09-20&hasta=2026-09-27&estacionId=4&usuarioId=7',
    ])
    expect(JSON.parse(fetchMock.mock.calls[1][1]?.body as string)).toMatchObject({ fecha: '2026-09-27', inventariosFisicos: [{ tanqueId: 8, inventarioFisicoGalones: 35.5 }] })
    expect(fetchMock.mock.calls[2][1]?.headers).toMatchObject({ Authorization: 'Bearer close-token' })
    const blob = new Blob(['%PDF-1.4'], { type: 'application/pdf' })
    fetchMock.mockResolvedValueOnce(new Response(blob, { status: 200, headers: { 'Content-Type': 'application/pdf' } }))
    await expect((await import('./api')).downloadCierrePdf(12)).resolves.toBeInstanceOf(Blob)
    expect(fetchMock.mock.calls[3][0]).toBe('/api/cierres-diarios/12/pdf')
    expect(fetchMock.mock.calls[3][1]?.headers).toMatchObject({ Authorization: 'Bearer close-token' })
  })

  it('envía y reintenta tickets con sesión y claves idempotentes, y consulta historial', async () => {
    sessionStorage.setItem('lavomva-session', JSON.stringify({ token: 'delivery-token', id: 7, name: 'QA', role: 'SUPERVISOR' }))
    fetchMock.mockImplementation(async () => response({ estadoTicket: 'PENDIENTE', envios: [] }))
    await sendTicket('uuid/ticket', 'AMBOS', 'f6c96a8b-2d6f-4f72-88c2-f83d05b05d1a')
    await retryTicketDelivery('uuid/ticket', 'SMS', 'aa106c46-1ceb-4070-8f46-9743a0155a82')
    await ticketDeliveryHistory('uuid/ticket')
    await reconcileTicketDelivery('uuid/ticket', 22, 'ENVIADO')
    expect(fetchMock.mock.calls.map(([url]) => String(url))).toEqual([
      '/api/tickets/uuid%2Fticket/enviar', '/api/tickets/uuid%2Fticket/reenviar', '/api/tickets/uuid%2Fticket/envios', '/api/tickets/uuid%2Fticket/envios/22/reconciliar',
    ])
    expect(JSON.parse(fetchMock.mock.calls[0][1]?.body as string)).toEqual({ canal: 'AMBOS', idempotencyKey: 'f6c96a8b-2d6f-4f72-88c2-f83d05b05d1a' })
    expect(JSON.parse(fetchMock.mock.calls[1][1]?.body as string)).toEqual({ canal: 'SMS', idempotencyKey: 'aa106c46-1ceb-4070-8f46-9743a0155a82' })
    expect(JSON.parse(fetchMock.mock.calls[3][1]?.body as string)).toEqual({ estado: 'ENVIADO' })
    expect(fetchMock.mock.calls.every(([, init]) => (init?.headers as Record<string, string>).Authorization === 'Bearer delivery-token')).toBe(true)
  })
})

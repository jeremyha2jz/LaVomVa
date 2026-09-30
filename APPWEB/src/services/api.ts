import type { Catalogs, Department, Employee, FuelRequest, FuelType, InventoryMovement, Session, Station, Supplier, Tank, Ticket, Vehicle } from '../types'

const baseUrl = (import.meta.env.VITE_API_URL || '/api').replace(/\/$/, '')
const SESSION_KEY = 'lavomva-session'

type Row = Record<string, unknown>
const str = (value: unknown, fallback = '') => value == null ? fallback : String(value)
const number = (value: unknown) => Number(value ?? 0)
const rows = (value: unknown): Row[] => Array.isArray(value) ? value as Row[] : []

export function savedSession(): Session | null {
  try {
    const value = sessionStorage.getItem(SESSION_KEY)
    return value ? JSON.parse(value) as Session : null
  } catch { return null }
}

export function clearSession() { sessionStorage.removeItem(SESSION_KEY) }

let refreshInFlight: Promise<Session | null> | null = null

async function refreshSession(): Promise<Session | null> {
  if (refreshInFlight) return refreshInFlight
  refreshInFlight = (async () => {
    const current = savedSession()
    if (!current?.refreshToken) return null
    try {
      const response = await fetch(`${baseUrl}/login/refresh`, {
        method: 'POST', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ refreshToken: current.refreshToken }),
      })
      if (!response.ok) throw new Error('refresh failed')
      const next = await response.json() as { token: string; refreshToken: string; expiresAt: string }
      const updated = { ...current, ...next }
      sessionStorage.setItem(SESSION_KEY, JSON.stringify(updated))
      return updated
    } catch {
      clearSession()
      window.dispatchEvent(new Event('lavomva-session-expired'))
      return null
    }
  })().finally(() => { refreshInFlight = null })
  return refreshInFlight
}

async function authenticatedFetch(path: string, init?: RequestInit): Promise<Response> {
  const send = (token?: string) => fetch(`${baseUrl}${path}`, {
    ...init,
    headers: { ...(init?.body ? { 'Content-Type': 'application/json' } : {}), ...(token ? { Authorization: `Bearer ${token}` } : {}), ...init?.headers },
  })
  const session = savedSession()
  const response = await send(session?.token)
  if (response.status !== 401 || path === '/login' || path.startsWith('/login/')) return response
  if (!session) return response
  if (!session.refreshToken) {
    clearSession()
    window.dispatchEvent(new Event('lavomva-session-expired'))
    return response
  }
  const updated = await refreshSession()
  return updated ? send(updated.token) : response
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await authenticatedFetch(path, init)
  if (!response.ok) {
    if ([502, 503, 504].includes(response.status)) {
      throw new Error('El servidor de la aplicación no está disponible. Inténtalo de nuevo más tarde.')
    }
    const body = await response.text()
    let message = body || `Error ${response.status}`
    try { const parsed = JSON.parse(body); message = parsed.mensaje || parsed.title || parsed.detail || body } catch { /* Plain text error. */ }
    throw new Error(message)
  }
  return response.status === 204 ? undefined as T : await response.json() as T
}

export type ReportType = 'consumo' | 'tickets' | 'despachos' | 'movimientos'
export type ReportFormat = 'csv' | 'xlsx' | 'pdf'
export type ReportFilters = {
  tipo: ReportType; desde?: string; hasta?: string; departamentoId?: number
  combustibleId?: number; empleadoId?: number; vehiculoId?: number; estado?: string
  estacionId?: number; pagina?: number; tamanoPagina?: number
}
export type ReportGroup = { nombre: string; galones: number; cantidad: number }
export type ReportRow = {
  id: number; fechaUtc: string; tipo: string; ticket: string | null; empleado: string | null
  vehiculo: string | null; departamento: string | null; combustible: string | null
  estacion: string | null; tanque: string | null; estado: string | null; galones: number
  referencia: string | null; usuario: string | null
}
export type ReportResult = {
  tipo: ReportType; rangoUtc: string; generadoEnUtc: string; filtros: ReportFilters
  pagina: number; tamanoPagina: number; totalRegistros: number
  totales: { registros: number; galones: number; despachos: number; tickets: number; solicitudes: number; inventarioActualGalones: number }
  porDepartamento: ReportGroup[]; porCombustible: ReportGroup[]; porVehiculo: ReportGroup[]
  porEmpleado: ReportGroup[]; porEstado: ReportGroup[]; items: ReportRow[]
}

function reportQuery(filters: ReportFilters): string {
  const params = new URLSearchParams()
  for (const [key, value] of Object.entries(filters)) if (value !== undefined && value !== '') params.set(key, String(value))
  return params.toString()
}

export async function getReport(filters: ReportFilters): Promise<ReportResult> {
  return request<ReportResult>('/reportes?' + reportQuery(filters))
}

export async function exportReport(filters: ReportFilters, formato: ReportFormat): Promise<{ blob: Blob; filename: string }> {
  const response = await authenticatedFetch('/reportes/exportar?' + reportQuery(filters) + '&formato=' + formato)
  if (!response.ok) {
    const body = await response.text()
    let message = body || 'Error ' + response.status
    try { const parsed = JSON.parse(body); message = parsed.mensaje || parsed.title || parsed.detail || body } catch { /* Plain text error. */ }
    throw new Error(message)
  }
  const disposition = response.headers.get('Content-Disposition') ?? ''
  const encodedName = disposition.match(/filename\*=UTF-8''([^;]+)/i)?.[1]
  const quotedName = disposition.match(/filename=\"?([^\";]+)\"?/i)?.[1]
  return { blob: await response.blob(), filename: decodeURIComponent(encodedName ?? quotedName ?? ('reporte-' + filters.tipo + '.' + formato)) }
}

export async function login(usuario: string, contrasena: string): Promise<Session> {
  const result = await request<{ token: string; refreshToken: string; expiresAt: string; id: number; nombre: string; rol: string }>('/login', { method: 'POST', body: JSON.stringify({ usuario, contrasena }) })
  const session = { token: result.token, refreshToken: result.refreshToken, expiresAt: result.expiresAt, id: result.id, name: result.nombre, role: result.rol }
  sessionStorage.setItem(SESSION_KEY, JSON.stringify(session))
  return session
}

export async function logout(): Promise<void> {
  const session = savedSession()
  clearSession()
  if (session?.refreshToken) {
    try { await fetch(`${baseUrl}/login/logout`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ refreshToken: session.refreshToken }) }) } catch { /* Local logout remains complete if the API is unreachable. */ }
  }
}

export async function register(usuario: string, correo: string, nombreCompleto: string, contrasena: string): Promise<void> {
  await request<unknown>('/login/registro', { method: 'POST', body: JSON.stringify({ usuario, correo, nombreCompleto, contrasena }) })
}

export async function ticketQr(id: string): Promise<Blob> {
  const response = await authenticatedFetch(`/tickets/${encodeURIComponent(id)}/qr`)
  if (!response.ok) throw new Error('No se pudo cargar el QR del ticket.')
  return response.blob()
}

export type DeliveryChannel = 'CORREO' | 'SMS' | 'AMBOS'
export type TicketDeliveryRow = { id: number; ticketId: string; canal: 'CORREO' | 'SMS'; destino: string; estadoEnvio: 'PENDIENTE' | 'ENVIADO' | 'FALLIDO'; detalleError: string | null; fechaEnvio: string | null; solicitadoEn: string; intento: number; proveedor: string | null; resultado: string | null; loteId: string }
export type TicketDeliveryOutcome = { ticketId: string; numeroTicket: string; estadoTicket: string; duplicadoIdempotente: boolean; envios: { canal: string; estado: string; destinoEnmascarado: string; error: string | null }[] }

export async function sendTicket(id: string, canal: DeliveryChannel, idempotencyKey = crypto.randomUUID()): Promise<TicketDeliveryOutcome> {
  return request<TicketDeliveryOutcome>(`/tickets/${encodeURIComponent(id)}/enviar`, { method: 'POST', body: JSON.stringify({ canal, idempotencyKey }) })
}

export async function retryTicketDelivery(id: string, canal: Exclude<DeliveryChannel, 'AMBOS'>, idempotencyKey = crypto.randomUUID()): Promise<TicketDeliveryOutcome> {
  return request<TicketDeliveryOutcome>(`/tickets/${encodeURIComponent(id)}/reenviar`, { method: 'POST', body: JSON.stringify({ canal, idempotencyKey }) })
}

export async function ticketDeliveryHistory(id: string): Promise<TicketDeliveryRow[]> {
  return request<TicketDeliveryRow[]>(`/tickets/${encodeURIComponent(id)}/envios`)
}

export async function reconcileTicketDelivery(ticketId: string, envioId: number, estado: 'ENVIADO' | 'FALLIDO'): Promise<TicketDeliveryOutcome> {
  return request<TicketDeliveryOutcome>(`/tickets/${encodeURIComponent(ticketId)}/envios/${encodeURIComponent(envioId)}/reconciliar`, {
    method: 'POST', body: JSON.stringify({ estado }),
  })
}

export type AppNotification = {
  id: number; tipo: 'TICKET_PROXIMO_A_VENCER' | 'TICKET_VENCIDO' | 'INVENTARIO_BAJO' | 'FALLO_INTEGRACION' | 'AJUSTE_INVENTARIO' | string
  titulo: string; mensaje: string; severidad: 'INFO' | 'AVISO' | 'CRITICA'; usuarioId: number
  referenciaTipo: string | null; referenciaId: string | null; fechaCreacion: string; fechaLectura: string | null; leida: boolean
  metadata: Record<string, unknown>
}
export type NotificationPage = { pagina: number; tamano: number; total: number; items: AppNotification[] }
export const listNotifications = (pagina = 1, tamano = 30) => request<NotificationPage>(`/notificaciones?pagina=${pagina}&tamano=${tamano}`)
export const unreadNotificationCount = () => request<{ cantidad: number }>('/notificaciones/no-leidas')
export const markNotificationRead = (id: number) => request<{ id: number; leida: boolean; fechaLectura: string }>(`/notificaciones/${id}/leer`, { method: 'POST' })
export const markAllNotificationsRead = () => request<{ actualizadas: number }>('/notificaciones/leer-todas', { method: 'POST' })

export type ScheduleType = 'AUTOMATICA' | 'RECURRENTE'
export type ScheduleFrequency = 'DIARIA' | 'SEMANAL' | 'MENSUAL'
export type ProgrammedRequest = {
  id: number; tipoSolicitud: ScheduleType; empleadoId: number; vehiculoId: number; departamentoId: number
  tipoCombustibleId: number; cantidadSolicitadaGalones: number; fechaInicial: string; fechaFinal: string | null
  frecuencia: ScheduleFrequency | null; proximaEjecucion: string | null; ultimaEjecucion: string | null
  activa: boolean; usuarioCreadorId: number; creadoEn: string; actualizadoEn: string
}
export type ProgrammedRequestBody = Pick<ProgrammedRequest, 'tipoSolicitud' | 'empleadoId' | 'vehiculoId' | 'departamentoId' | 'tipoCombustibleId' | 'cantidadSolicitadaGalones' | 'fechaInicial' | 'fechaFinal' | 'frecuencia'>
export type ProgrammedExecution = { id: number; programacionId: number; fechaProgramada: string; ejecutadaEn: string; estado: 'GENERADA' | 'FALLIDA'; solicitudGeneradaId: number | null; estadoSolicitud: string | null; detalleError: string | null }

export async function listProgrammedRequests(): Promise<ProgrammedRequest[]> {
  return request<ProgrammedRequest[]>('/programaciones')
}
export async function createProgrammedRequest(body: ProgrammedRequestBody): Promise<ProgrammedRequest> {
  return request<ProgrammedRequest>('/programaciones', { method: 'POST', body: JSON.stringify(body) })
}
export async function updateProgrammedRequest(id: number, body: ProgrammedRequestBody): Promise<ProgrammedRequest> {
  return request<ProgrammedRequest>(`/programaciones/${id}`, { method: 'PUT', body: JSON.stringify(body) })
}
export async function setProgrammedRequestActive(id: number, active: boolean): Promise<ProgrammedRequest> {
  return request<ProgrammedRequest>(`/programaciones/${id}/${active ? 'activar' : 'desactivar'}`, { method: 'POST' })
}
export async function programmedRequestHistory(id: number): Promise<ProgrammedExecution[]> {
  return request<ProgrammedExecution[]>(`/programaciones/${id}/ejecuciones`)
}

export type CierreTankSummary = {
  tanqueId: number; codigo: string; nombre: string; capacidadGalones: number
  inventarioInicialGalones: number; entradasGalones: number; despachadoGalones: number
  otrasSalidasGalones: number; mermasGalones: number; ajustesGalones: number
  inventarioTeoricoFinalGalones: number
}
export type CierreSummary = {
  estacionId: number; estacion: string; fecha: string; inventarioInicialGalones: number
  volumenRecibidoGalones: number; volumenDespachadoGalones: number; otrasSalidasGalones: number
  mermasGalones: number; ajustesGalones: number; inventarioTeoricoFinalGalones: number
  cantidadDespachos: number; tanques: CierreTankSummary[]
}
export type CierreRow = {
  cierre: { id: number; estacionId: number; fecha: string; inventarioInicialGalones: number; volumenRecibidoGalones: number; volumenDespachadoGalones: number; inventarioFinalGalones: number; inventarioFisicoGalones: number; diferenciaGalones: number; cantidadDespachos: number; usuarioCierreId: number; cerradoEn: string; estado: string }
  estacion: string
}

export async function downloadCierrePdf(id: number): Promise<Blob> {
  const response = await authenticatedFetch(`/cierres-diarios/${encodeURIComponent(id)}/pdf`)
  if (!response.ok) {
    const body = await response.text()
    let message = body || `Error ${response.status}`
    try { const parsed = JSON.parse(body); message = parsed.mensaje || parsed.title || parsed.detail || body } catch { /* API plain-text error. */ }
    throw new Error(message)
  }
  return response.blob()
}

export async function loadLiveData() {
  const [departmentsRaw, employeesRaw, vehiclesRaw, fuelsRaw, tanksRaw, stationsRaw, suppliersRaw, requestsRaw, ticketsRaw, movementsRaw] = await Promise.all([
    request<unknown>('/catalogos/departamentos'),
    request<unknown>('/catalogos/empleados'),
    request<unknown>('/catalogos/vehiculos'),
    request<unknown>('/catalogos/tipos-combustible'),
    request<unknown>('/catalogos/tanques'),
    request<unknown>('/catalogos/estaciones'),
    request<unknown>('/recepciones/proveedores'),
    request<unknown>('/solicitudes'),
    request<unknown>('/tickets'),
    request<unknown>('/inventario/movimientos'),
  ])
  const departmentRows = rows(departmentsRaw)
  const employeeRows = rows(employeesRaw)
  const vehicleRows = rows(vehiclesRaw)
  const fuelRows = rows(fuelsRaw)
  const tankRows = rows(tanksRaw)
  const stationRows = rows(stationsRaw)
  const departmentName = (id: unknown) => str(departmentRows.find((row) => number(row.id) === number(id))?.nombre, 'Departamento')
  const fuelName = (id: unknown) => str(fuelRows.find((row) => number(row.id) === number(id))?.nombre, 'Combustible')
  const stationName = (id: unknown) => str(stationRows.find((row) => number(row.id) === number(id))?.nombre, 'Estación')
  const employeeName = (id: unknown) => str(employeeRows.find((row) => number(row.id) === number(id))?.nombreCompleto, 'Empleado')
  const employeeCode = (id: unknown) => str(employeeRows.find((row) => number(row.id) === number(id))?.codigoEmpleado)
  const vehiclePlate = (id: unknown) => str(vehicleRows.find((row) => number(row.id) === number(id))?.placa, 'Vehículo')
  const vehicleLabel = (id: unknown) => {
    const row = vehicleRows.find((item) => number(item.id) === number(id))
    return row ? `${str(row.placa)} · ${str(row.marca)} ${str(row.modelo)}` : 'Vehículo'
  }
  const departments: Department[] = departmentRows.map((row) => ({ id: number(row.id), code: str(row.codigo), name: str(row.nombre), employees: employeeRows.filter((item) => number(item.departamentoId) === number(row.id)).length, vehicles: vehicleRows.filter((item) => number(item.departamentoId) === number(row.id)).length, active: row.activo !== false }))
  const employees: Employee[] = employeeRows.map((row) => ({ id: number(row.id), code: str(row.codigoEmpleado), name: str(row.nombreCompleto), document: str(row.cedula), department: departmentName(row.departamentoId), position: str(row.cargo), email: str(row.correo), phone: str(row.telefonoMovil), active: row.activo !== false }))
  const vehicles: Vehicle[] = vehicleRows.map((row) => ({ id: number(row.id), plate: str(row.placa), code: str(row.ficha), brand: str(row.marca), model: str(row.modelo), year: number(row.anio), type: str(row.tipo), department: departmentName(row.departamentoId), tankCapacity: number(row.capacidadTanqueGalones), odometer: number(row.odometroKm), active: row.activo !== false }))
  const fuelTypes: FuelType[] = fuelRows.map((row) => ({ id: number(row.id), name: str(row.nombre) }))
  const stations: Station[] = stationRows.map((row) => ({ id: number(row.id), name: str(row.nombre), location: str(row.ubicacion) }))
  const suppliers: Supplier[] = rows(suppliersRaw).map((row) => ({ id: number(row.id), name: str(row.nombre), rnc: str(row.rnc) }))
  const tanks: Tank[] = tankRows.map((row) => ({ id: number(row.id), code: str(row.codigo), name: str(row.nombre, str(row.codigo)), stationId: number(row.estacionId), fuelTypeId: number(row.tipoCombustibleId), station: stationName(row.estacionId), fuelType: fuelName(row.tipoCombustibleId), capacity: number(row.capacidadGalones), stock: number(row.existenciaActualGalones), criticalLevel: number(row.nivelCriticoGalones) }))
  const requests: FuelRequest[] = rows(requestsRaw).map((row) => ({ id: number(row.id), employeeId: number(row.empleadoId), vehicleId: number(row.vehiculoId), departmentId: number(row.departamentoId), fuelTypeId: number(row.tipoCombustibleId), employee: employeeName(row.empleadoId), employeeCode: employeeCode(row.empleadoId), vehicle: vehicleLabel(row.vehiculoId), department: departmentName(row.departamentoId), fuelType: fuelName(row.tipoCombustibleId), requestedGallons: number(row.cantidadSolicitadaGalones), authorizedGallons: row.cantidadAutorizadaGalones == null ? undefined : number(row.cantidadAutorizadaGalones), requestedAt: str(row.fechaSolicitud, new Date().toISOString()), expiresAt: str(row.fechaVencimiento, new Date().toISOString()), kind: str(row.tipoSolicitud, 'MANUAL') as FuelRequest['kind'], status: str(row.estado, 'PENDIENTE') as FuelRequest['status'], reason: str(row.motivo, 'Sin motivo registrado') }))
  const tickets: Ticket[] = rows(ticketsRaw).map((row) => ({ id: str(row.id), sequence: str(row.numeroSecuencial), employee: str(row.empleado), vehicle: str(row.vehiculo), department: str(row.departamento), fuelType: str(row.tipoCombustible), gallons: number(row.cantidadAutorizadaGalones), createdAt: str(row.fechaCreacion), expiresAt: str(row.fechaVencimiento), status: str(row.estado, 'CREADO') as Ticket['status'] }))
  const movements: InventoryMovement[] = rows(movementsRaw).map((row) => ({ id: number(row.id), tankId: number(row.tanqueId), tank: str(tankRows.find((item) => number(item.id) === number(row.tanqueId))?.codigo, `TQ-${row.tanqueId}`), type: str(row.tipoMovimiento) as InventoryMovement['type'], gallons: number(row.cantidadGalones), previous: number(row.existenciaAnterior), current: number(row.existenciaNueva), reference: str(row.referenciaId, str(row.referenciaTipo, '—')), user: row.usuarioId == null ? 'Sistema' : `Usuario #${row.usuarioId}`, date: str(row.fechaHora) }))
  return { catalogs: { employees, vehicles, departments, fuelTypes, suppliers, stations } as Catalogs, requests, tickets, tanks, movements }
}

export const api = {
  catalogCreate: (kind: string, body: unknown) => request<unknown>(`/gestion/${kind}`, { method: 'POST', body: JSON.stringify(body) }),
  catalogUpdate: (kind: string, id: number, body: unknown) => request<unknown>(`/gestion/${kind}/${id}`, { method: 'PUT', body: JSON.stringify(body) }),
  catalogDeactivate: (kind: string, id: number) => request<void>(`/gestion/${kind}/${id}`, { method: 'DELETE' }),
  users: () => request<{ id: number; nombreUsuario: string; correo: string; nombreCompleto: string; telefono?: string; activo: boolean; rolId?: number; rol?: string }[]>('/gestion/usuarios'),
  roles: () => request<{ id: number; nombre: string }[]>('/catalogos/roles'),
  createUser: (body: unknown) => request<unknown>('/gestion/usuarios', { method: 'POST', body: JSON.stringify(body) }),
  createSupplier: (body: unknown) => request<unknown>('/recepciones/proveedores', { method: 'POST', body: JSON.stringify(body) }),
  updateUser: (id: number, body: unknown) => request<unknown>(`/gestion/usuarios/${id}`, { method: 'PUT', body: JSON.stringify(body) }),
  resetUserPassword: (id: number, contrasena: string) => request<void>(`/gestion/usuarios/${id}/restablecer-contrasena`, { method: 'POST', body: JSON.stringify({ contrasena }) }),
  deactivateUser: (id: number) => request<void>(`/gestion/usuarios/${id}`, { method: 'DELETE' }),
  activateUser: (id: number) => request<void>(`/gestion/usuarios/${id}/activar`, { method: 'POST' }),
  createRequest: (body: unknown) => request<unknown>('/solicitudes', { method: 'POST', body: JSON.stringify(body) }),
  approveRequest: (id: number, body: unknown) => request<unknown>(`/solicitudes/${id}/aprobar`, { method: 'PUT', body: JSON.stringify(body) }),
  rejectRequest: (id: number) => request<unknown>(`/solicitudes/${id}/rechazar`, { method: 'PUT' }),
  createTicket: (requestId: number, issuerId: number) => request<unknown>('/tickets', { method: 'POST', body: JSON.stringify({ solicitudId: requestId, usuarioEmisorId: issuerId }) }),
  cancelTicket: (id: string, motivo: string) => request<unknown>(`/tickets/${encodeURIComponent(id)}/anular`, { method: 'POST', body: JSON.stringify({ motivo }) }),
  closeSummary: (stationId: number, date: string) => request<{ resumen: CierreSummary; cerrado: boolean; cierre: (CierreRow['cierre'] & { detalleTanques: { tanqueId: number; inventarioFisicoGalones: number }[] }) | null }>(`/cierres-diarios/resumen?estacionId=${stationId}&fecha=${encodeURIComponent(date)}`),
  createDailyClose: (body: { estacionId: number; fecha: string; inventariosFisicos: { tanqueId: number; inventarioFisicoGalones: number | null }[]; observaciones?: string }) => request<CierreRow['cierre']>('/cierres-diarios', { method: 'POST', body: JSON.stringify(body) }),
  dailyCloses: (filters: { desde?: string; hasta?: string; estacionId?: number; usuarioId?: number } = {}) => {
    const params = new URLSearchParams()
    if (filters.desde) params.set('desde', filters.desde)
    if (filters.hasta) params.set('hasta', filters.hasta)
    if (filters.estacionId) params.set('estacionId', String(filters.estacionId))
    if (filters.usuarioId) params.set('usuarioId', String(filters.usuarioId))
    return request<CierreRow[]>(`/cierres-diarios${params.size ? `?${params}` : ''}`)
  },
  receive: (body: unknown) => request<unknown>('/recepciones', { method: 'POST', body: JSON.stringify(body) }),
}

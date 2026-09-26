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

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const session = savedSession()
  const response = await fetch(`${baseUrl}${path}`, {
    ...init,
    headers: { ...(init?.body ? { 'Content-Type': 'application/json' } : {}), ...(session ? { Authorization: `Bearer ${session.token}` } : {}), ...init?.headers },
  })
  if (!response.ok) {
    if ([502, 503, 504].includes(response.status)) {
      throw new Error('El servidor de la aplicación no está disponible. Inténtalo de nuevo cuando la API esté en funcionamiento.')
    }
    const body = await response.text()
    let message = body || `Error ${response.status}`
    try { const parsed = JSON.parse(body); message = parsed.mensaje || parsed.title || parsed.detail || body } catch { /* Plain text error. */ }
    throw new Error(message)
  }
  return response.status === 204 ? undefined as T : await response.json() as T
}

export async function login(usuario: string, contrasena: string): Promise<Session> {
  const result = await request<{ token: string; id: number; nombre: string; rol: string }>('/login', { method: 'POST', body: JSON.stringify({ usuario, contrasena }) })
  const session = { token: result.token, id: result.id, name: result.nombre, role: result.rol }
  sessionStorage.setItem(SESSION_KEY, JSON.stringify(session))
  return session
}

export async function register(usuario: string, correo: string, nombreCompleto: string, contrasena: string): Promise<void> {
  await request<unknown>('/login/registro', { method: 'POST', body: JSON.stringify({ usuario, correo, nombreCompleto, contrasena }) })
}

export async function ticketQr(id: string): Promise<Blob> {
  const session = savedSession()
  const response = await fetch(`${baseUrl}/tickets/${encodeURIComponent(id)}/qr`, {
    headers: session ? { Authorization: `Bearer ${session.token}` } : {},
  })
  if (!response.ok) throw new Error('No se pudo cargar el QR del ticket.')
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
  const movements: InventoryMovement[] = rows(movementsRaw).map((row) => ({ id: number(row.id), tank: str(tankRows.find((item) => number(item.id) === number(row.tanqueId))?.codigo, `TQ-${row.tanqueId}`), type: str(row.tipoMovimiento) as InventoryMovement['type'], gallons: number(row.cantidadGalones), previous: number(row.existenciaAnterior), current: number(row.existenciaNueva), reference: str(row.referenciaId, str(row.referenciaTipo, '—')), user: row.usuarioId == null ? 'Sistema' : `Usuario #${row.usuarioId}`, date: str(row.fechaHora) }))
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
  receive: (body: unknown) => request<unknown>('/recepciones', { method: 'POST', body: JSON.stringify(body) }),
}

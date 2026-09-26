import { createContext, useContext, useEffect, useState, type ReactNode } from 'react'
import { api, clearSession, loadLiveData, login as apiLogin, register as apiRegister, savedSession } from '../services/api'
import type { Catalogs, FuelRequest, InventoryMovement, Session, Tank, Ticket, ToastMessage } from '../types'

export type NewRequest = Pick<FuelRequest, 'employee' | 'employeeCode' | 'vehicle' | 'department' | 'fuelType' | 'requestedGallons' | 'expiresAt' | 'kind' | 'reason'> & {
  employeeId?: number; vehicleId?: number; departmentId?: number; fuelTypeId?: number
}
export type ReceptionDetails = { supplierId?: number; date?: string; notes?: string }

interface AppState {
  register: (username: string, email: string, name: string, password: string) => Promise<void>
  loading: boolean
  error: string | null
  session: Session | null
  catalogs: Catalogs
  requests: FuelRequest[]
  tickets: Ticket[]
  tanks: Tank[]
  movements: InventoryMovement[]
  toasts: ToastMessage[]
  login: (username: string, password: string) => Promise<void>
  logout: () => void
  refresh: () => Promise<void>
  addRequest: (request: NewRequest) => Promise<void>
  resolveRequest: (id: number, decision: 'approve' | 'reject', gallons?: number) => Promise<void>
  receiveFuel: (tankId: number, gallons: number, invoice: string, details?: ReceptionDetails) => Promise<boolean>
  removeToast: (id: number) => void
  notify: (title: string, description: string, tone?: ToastMessage['tone']) => void
}

const AppContext = createContext<AppState | null>(null)
const emptyCatalogs: Catalogs = { employees: [], vehicles: [], departments: [], fuelTypes: [], suppliers: [], stations: [] }

export function AppProvider({ children }: { children: ReactNode }) {
  const [session, setSession] = useState<Session | null>(savedSession)
  const [catalogs, setCatalogs] = useState<Catalogs>(emptyCatalogs)
  const [requests, setRequests] = useState<FuelRequest[]>([])
  const [tickets, setTickets] = useState<Ticket[]>([])
  const [tanks, setTanks] = useState<Tank[]>([])
  const [movements, setMovements] = useState<InventoryMovement[]>([])
  const [toasts, setToasts] = useState<ToastMessage[]>([])
  const [loading, setLoading] = useState(!!session)
  const [error, setError] = useState<string | null>(null)

  async function refresh() {
    setLoading(true)
    try {
      const data = await loadLiveData()
      setCatalogs(data.catalogs)
      setRequests(data.requests)
      setTickets(data.tickets)
      setTanks(data.tanks)
      setMovements(data.movements)
      setError(null)
    } catch (cause) {
      const message = cause instanceof Error ? cause.message : 'No se pudo cargar la API.'
      setError(message)
      throw cause
    } finally { setLoading(false) }
  }

  useEffect(() => {
    if (session) void refresh().catch(() => {})
  }, [session?.token])

  function notify(title: string, description: string, tone: ToastMessage['tone'] = 'success') {
    const id = Date.now() + Math.random()
    setToasts((current) => [...current, { id, title, description, tone }])
    window.setTimeout(() => setToasts((current) => current.filter((item) => item.id !== id)), 4200)
  }

  async function login(username: string, password: string) { setSession(await apiLogin(username, password)) }
  async function register(username: string, email: string, name: string, password: string) { await apiRegister(username, email, name, password) }
  function logout() { clearSession(); setSession(null); setRequests([]); setTickets([]); setTanks([]); setMovements([]); setCatalogs(emptyCatalogs); setError(null) }

  async function addRequest(item: NewRequest) {
    if (!item.employeeId || !item.vehicleId || !item.departmentId || !item.fuelTypeId) throw new Error('Selecciona datos válidos de los catálogos.')
    await api.createRequest({ empleadoId: item.employeeId, vehiculoId: item.vehicleId, departamentoId: item.departmentId, tipoCombustibleId: item.fuelTypeId, cantidadSolicitadaGalones: item.requestedGallons, fechaVencimiento: item.expiresAt, usuarioCreadorId: session?.id, tipoSolicitud: item.kind, motivo: item.reason })
    await refresh()
    notify('Solicitud registrada', 'La solicitud quedó pendiente de aprobación.')
  }

  async function resolveRequest(id: number, decision: 'approve' | 'reject', gallons?: number) {
    const selected = requests.find((request) => request.id === id)
    if (!selected || selected.status !== 'PENDIENTE') throw new Error('La solicitud ya no está pendiente.')
    if (!session) throw new Error('Inicia sesión para continuar.')
    if (decision === 'approve') {
      await api.approveRequest(id, { cantidadAutorizadaGalones: gallons ?? selected.requestedGallons, fechaVencimiento: selected.expiresAt, usuarioAprobadorId: session.id })
      try { await api.createTicket(id, session.id) } catch (cause) { await refresh(); throw new Error(`Solicitud aprobada, pero no se pudo emitir el ticket: ${cause instanceof Error ? cause.message : String(cause)}`) }
    } else await api.rejectRequest(id)
    await refresh()
    notify(decision === 'approve' ? 'Solicitud aprobada' : 'Solicitud rechazada', decision === 'approve' ? 'Se emitió un ticket digital.' : 'La decisión quedó registrada.', decision === 'approve' ? 'success' : 'warning')
  }

  async function receiveFuel(tankId: number, gallons: number, invoice: string, details: ReceptionDetails = {}): Promise<boolean> {
    const tank = tanks.find((item) => item.id === tankId)
    if (!tank || !Number.isFinite(gallons) || gallons <= 0 || tank.stock + gallons > tank.capacity) return false
    if (!session || !details.supplierId) throw new Error('Selecciona un proveedor e inicia sesión.')
    await api.receive({ proveedorId: details.supplierId, numeroFactura: invoice, fechaRecepcion: details.date || new Date().toISOString(), usuarioReceptorId: session.id, observaciones: details.notes, detalles: [{ tanqueId: tankId, volumenRecibidoGalones: gallons, costoUnitario: null }] })
    await refresh()
    notify('Recepción registrada', 'El inventario del tanque fue actualizado.')
    return true
  }

  const value: AppState = { loading, error, session, catalogs, requests, tickets, tanks, movements, toasts, login, register, logout, refresh, addRequest, resolveRequest, receiveFuel, removeToast: (id) => setToasts((items) => items.filter((item) => item.id !== id)), notify }
  return <AppContext.Provider value={value}>{children}</AppContext.Provider>
}

export function useApp() {
  const context = useContext(AppContext)
  if (!context) throw new Error('useApp debe usarse dentro de AppProvider')
  return context
}

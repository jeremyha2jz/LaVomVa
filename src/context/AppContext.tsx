import { createContext, useContext, useEffect, useState, type ReactNode } from 'react'
import * as seed from '../data/mock'
import { api, clearSession, liveApiEnabled, loadLiveData, login as apiLogin, savedSession } from '../services/api'
import type { Catalogs, FuelRequest, InventoryMovement, Session, Tank, Ticket, ToastMessage } from '../types'

export type NewRequest = Pick<FuelRequest, 'employee' | 'employeeCode' | 'vehicle' | 'department' | 'fuelType' | 'requestedGallons' | 'expiresAt' | 'kind' | 'reason'> & {
  employeeId?: number; vehicleId?: number; departmentId?: number; fuelTypeId?: number
}
export type ReceptionDetails = { supplierId?: number; date?: string; notes?: string }

interface AppState {
  live: boolean
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
  dispatchTicket: (sequence: string, tankId: number, gallons: number, odometer: number) => Promise<boolean>
  receiveFuel: (tankId: number, gallons: number, invoice: string, details?: ReceptionDetails) => Promise<boolean>
  removeToast: (id: number) => void
  notify: (title: string, description: string, tone?: ToastMessage['tone']) => void
}

const AppContext = createContext<AppState | null>(null)
const STORAGE_KEY = 'intec-fuel-demo-v1'
const demoCatalogs: Catalogs = {
  employees: seed.employees, vehicles: seed.vehicles, departments: seed.departments,
  fuelTypes: [{ id: 1, name: 'GASOLINA REGULAR' }, { id: 2, name: 'GASOLINA PREMIUM' }, { id: 3, name: 'DIÉSEL' }],
  suppliers: [{ id: 1, name: 'Caribe Combustibles, SRL', rnc: '1-31-45678-2' }, { id: 2, name: 'Distribuidora Nacional de Petróleo', rnc: '1-01-12345-6' }],
}
const emptyCatalogs: Catalogs = { employees: [], vehicles: [], departments: [], fuelTypes: [], suppliers: [] }

function loadDemoState() {
  if (liveApiEnabled) return { requests: [] as FuelRequest[], tickets: [] as Ticket[], tanks: [] as Tank[], movements: [] as InventoryMovement[] }
  try {
    const saved = localStorage.getItem(STORAGE_KEY)
    if (saved) {
      const value = JSON.parse(saved)
      return {
        requests: Array.isArray(value.requests) ? value.requests as FuelRequest[] : seed.requests,
        tickets: Array.isArray(value.tickets) ? value.tickets as Ticket[] : seed.tickets,
        tanks: Array.isArray(value.tanks) ? value.tanks as Tank[] : seed.tanks,
        movements: Array.isArray(value.movements) ? value.movements as InventoryMovement[] : seed.movements,
      }
    }
  } catch { /* Browser storage can be unavailable; use sample data. */ }
  return { requests: seed.requests, tickets: seed.tickets, tanks: seed.tanks, movements: seed.movements }
}

export function AppProvider({ children }: { children: ReactNode }) {
  const [initial] = useState(loadDemoState)
  const [session, setSession] = useState<Session | null>(liveApiEnabled ? savedSession : null)
  const [catalogs, setCatalogs] = useState<Catalogs>(liveApiEnabled ? emptyCatalogs : demoCatalogs)
  const [requests, setRequests] = useState(initial.requests)
  const [tickets, setTickets] = useState(initial.tickets)
  const [tanks, setTanks] = useState(initial.tanks)
  const [movements, setMovements] = useState(initial.movements)
  const [toasts, setToasts] = useState<ToastMessage[]>([])
  const [loading, setLoading] = useState(liveApiEnabled && !!session)
  const [error, setError] = useState<string | null>(null)

  async function refresh() {
    if (!liveApiEnabled) return
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
    if (liveApiEnabled && session) void refresh().catch(() => {})
  }, [session?.token])

  useEffect(() => {
    if (!liveApiEnabled) localStorage.setItem(STORAGE_KEY, JSON.stringify({ requests, tickets, tanks, movements }))
  }, [requests, tickets, tanks, movements])

  function notify(title: string, description: string, tone: ToastMessage['tone'] = 'success') {
    const id = Date.now() + Math.random()
    setToasts((current) => [...current, { id, title, description, tone }])
    window.setTimeout(() => setToasts((current) => current.filter((item) => item.id !== id)), 4200)
  }

  async function login(username: string, password: string) { setSession(await apiLogin(username, password)) }
  function logout() { clearSession(); setSession(null); setRequests([]); setTickets([]); setTanks([]); setMovements([]); setCatalogs(emptyCatalogs); setError(null) }

  async function addRequest(item: NewRequest) {
    if (liveApiEnabled) {
      if (!item.employeeId || !item.vehicleId || !item.departmentId || !item.fuelTypeId) throw new Error('Selecciona datos válidos de los catálogos.')
      await api.createRequest({ empleadoId: item.employeeId, vehiculoId: item.vehicleId, departamentoId: item.departmentId, tipoCombustibleId: item.fuelTypeId, cantidadSolicitadaGalones: item.requestedGallons, fechaVencimiento: item.expiresAt, usuarioCreadorId: session?.id, tipoSolicitud: item.kind, motivo: item.reason })
      await refresh()
    } else {
      setRequests((current) => [{ ...item, id: Math.max(0, ...current.map((request) => request.id)) + 1, requestedAt: new Date().toISOString(), status: 'PENDIENTE' }, ...current])
    }
    notify('Solicitud registrada', 'La solicitud quedó pendiente de aprobación.')
  }

  async function resolveRequest(id: number, decision: 'approve' | 'reject', gallons?: number) {
    const selected = requests.find((request) => request.id === id)
    if (!selected || selected.status !== 'PENDIENTE') throw new Error('La solicitud ya no está pendiente.')
    if (liveApiEnabled) {
      if (!session) throw new Error('Inicia sesión para continuar.')
      if (decision === 'approve') {
        await api.approveRequest(id, { cantidadAutorizadaGalones: gallons ?? selected.requestedGallons, fechaVencimiento: selected.expiresAt, usuarioAprobadorId: session.id })
        try { await api.createTicket(id, session.id) } catch (cause) { await refresh(); throw new Error(`Solicitud aprobada, pero no se pudo emitir el ticket: ${cause instanceof Error ? cause.message : String(cause)}`) }
      } else await api.rejectRequest(id)
      await refresh()
    } else {
      setRequests((current) => current.map((request) => request.id === id ? { ...request, status: decision === 'approve' ? 'APROBADA' : 'RECHAZADA', authorizedGallons: decision === 'approve' ? gallons ?? request.requestedGallons : undefined } : request))
      if (decision === 'approve') {
        const next = Math.max(0, ...tickets.map((ticket) => Number(ticket.sequence.split('-').pop()) || 0)) + 1
        setTickets((current) => [{ id: crypto.randomUUID(), sequence: `COM-${new Date().getFullYear()}-${String(next).padStart(6, '0')}`, employee: selected.employee, vehicle: selected.vehicle.split(' · ')[0], department: selected.department, fuelType: selected.fuelType, gallons: gallons ?? selected.requestedGallons, createdAt: new Date().toISOString(), expiresAt: selected.expiresAt, status: 'CREADO' }, ...current])
      }
    }
    notify(decision === 'approve' ? 'Solicitud aprobada' : 'Solicitud rechazada', decision === 'approve' ? 'Se emitió un ticket digital.' : 'La decisión quedó registrada.', decision === 'approve' ? 'success' : 'warning')
  }

  async function dispatchTicket(sequence: string, tankId: number, gallons: number, odometer: number): Promise<boolean> {
    const ticket = tickets.find((item) => item.sequence === sequence)
    const tank = tanks.find((item) => item.id === tankId)
    if (!ticket || !tank || !['CREADO', 'ENVIADO', 'PENDIENTE', 'PROXIMO_A_VENCER'].includes(ticket.status) || new Date(ticket.expiresAt).getTime() <= Date.now() || tank.fuelType !== ticket.fuelType || !Number.isFinite(gallons) || gallons <= 0 || gallons > tank.stock || gallons > ticket.gallons || !Number.isFinite(odometer) || odometer < 0) return false
    if (liveApiEnabled) {
      if (!session) throw new Error('Inicia sesión para continuar.')
      await api.dispatch({ ticketId: ticket.id, tanqueId: tankId, galonesServidos: gallons, odometroKm: odometer, identidadConfirmada: true, operadorId: session.id })
      await refresh()
    } else {
      setTickets((current) => current.map((item) => item.sequence === sequence ? { ...item, status: 'CONSUMIDO' } : item))
      setTanks((current) => current.map((item) => item.id === tankId ? { ...item, stock: item.stock - gallons } : item))
      setMovements((current) => [{ id: Math.max(0, ...current.map((item) => item.id)) + 1, tank: tank.code, type: 'SALIDA', gallons, previous: tank.stock, current: tank.stock - gallons, reference: sequence, user: 'Operador demo', date: new Date().toISOString() }, ...current])
    }
    notify('Despacho completado', `${gallons} galones registrados.`)
    return true
  }

  async function receiveFuel(tankId: number, gallons: number, invoice: string, details: ReceptionDetails = {}): Promise<boolean> {
    const tank = tanks.find((item) => item.id === tankId)
    if (!tank || !Number.isFinite(gallons) || gallons <= 0 || tank.stock + gallons > tank.capacity) return false
    if (liveApiEnabled) {
      if (!session || !details.supplierId) throw new Error('Selecciona un proveedor e inicia sesión.')
      await api.receive({ proveedorId: details.supplierId, numeroFactura: invoice, fechaRecepcion: details.date || new Date().toISOString(), usuarioReceptorId: session.id, observaciones: details.notes, detalles: [{ tanqueId: tankId, volumenRecibidoGalones: gallons, costoUnitario: null }] })
      await refresh()
    } else {
      setTanks((current) => current.map((item) => item.id === tankId ? { ...item, stock: item.stock + gallons } : item))
      setMovements((current) => [{ id: Math.max(0, ...current.map((item) => item.id)) + 1, tank: tank.code, type: 'ENTRADA', gallons, previous: tank.stock, current: tank.stock + gallons, reference: invoice, user: 'Supervisor demo', date: new Date().toISOString() }, ...current])
    }
    notify('Recepción registrada', 'El inventario del tanque fue actualizado.')
    return true
  }

  const value: AppState = { live: liveApiEnabled, loading, error, session, catalogs, requests, tickets, tanks, movements, toasts, login, logout, refresh, addRequest, resolveRequest, dispatchTicket, receiveFuel, removeToast: (id) => setToasts((items) => items.filter((item) => item.id !== id)), notify }
  return <AppContext.Provider value={value}>{children}</AppContext.Provider>
}

export function useApp() {
  const context = useContext(AppContext)
  if (!context) throw new Error('useApp debe usarse dentro de AppProvider')
  return context
}

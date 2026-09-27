export type RequestStatus = 'PENDIENTE' | 'APROBADA' | 'RECHAZADA' | 'CANCELADA'
export type TicketStatus = 'CREADO' | 'ENVIADO' | 'PENDIENTE' | 'PROXIMO_A_VENCER' | 'VENCIDO' | 'CONSUMIDO' | 'ANULADO'

export interface FuelRequest {
  id: number
  employeeId?: number
  vehicleId?: number
  departmentId?: number
  fuelTypeId?: number
  employee: string
  employeeCode: string
  vehicle: string
  department: string
  fuelType: string
  requestedGallons: number
  authorizedGallons?: number
  requestedAt: string
  expiresAt: string
  kind: 'MANUAL' | 'AUTOMATICA' | 'RECURRENTE'
  status: RequestStatus
  reason: string
}

export interface Ticket {
  id: string
  sequence: string
  employee: string
  vehicle: string
  department: string
  fuelType: string
  gallons: number
  createdAt: string
  expiresAt: string
  status: TicketStatus
}

export interface Tank {
  id: number
  stationId?: number
  fuelTypeId?: number
  code: string
  name: string
  station: string
  fuelType: string
  capacity: number
  stock: number
  criticalLevel: number
}

export interface InventoryMovement {
  id: number
  tankId?: number
  tank: string
  type: 'ENTRADA' | 'SALIDA' | 'AJUSTE_POSITIVO' | 'AJUSTE_NEGATIVO' | 'MERMA'
  gallons: number
  previous: number
  current: number
  reference: string
  user: string
  date: string
}

export interface Employee { id: number; code: string; name: string; document: string; department: string; position: string; email: string; phone: string; active: boolean }
export interface Vehicle { id: number; plate: string; code: string; brand: string; model: string; year: number; type: string; department: string; tankCapacity: number; odometer: number; active: boolean }
export interface Department { id: number; code: string; name: string; employees: number; vehicles: number; active: boolean }
export interface User { id: number; name: string; username: string; email: string; roles: string[]; lastAccess: string; active: boolean }
export interface FuelType { id: number; name: string }
export interface Supplier { id: number; name: string; rnc: string }
export interface Station { id: number; name: string; location: string }
export interface Session { token: string; id: number; name: string; role: string }
export interface Catalogs { employees: Employee[]; vehicles: Vehicle[]; departments: Department[]; fuelTypes: FuelType[]; suppliers: Supplier[]; stations: Station[] }
export interface ToastMessage { id: number; title: string; description: string; tone: 'success' | 'warning' | 'error' | 'info' }

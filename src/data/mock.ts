import type { Department, Employee, FuelRequest, InventoryMovement, Tank, Ticket, User, Vehicle } from '../types'

export const requests: FuelRequest[] = [
  { id: 1048, employee: 'María Rodríguez', employeeCode: 'EMP-0142', vehicle: 'L-402918 · Toyota Hilux', department: 'Operaciones', fuelType: 'DIÉSEL', requestedGallons: 24, requestedAt: '2026-09-22T08:42:00', expiresAt: '2026-09-24T18:00:00', kind: 'MANUAL', status: 'PENDIENTE', reason: 'Ruta de inspección de subestaciones' },
  { id: 1047, employee: 'Carlos Méndez', employeeCode: 'EMP-0087', vehicle: 'EG-01834 · Kia K2700', department: 'Mantenimiento', fuelType: 'DIÉSEL', requestedGallons: 18, authorizedGallons: 16, requestedAt: '2026-09-22T07:15:00', expiresAt: '2026-09-24T18:00:00', kind: 'RECURRENTE', status: 'APROBADA', reason: 'Mantenimiento preventivo' },
  { id: 1046, employee: 'Ana Sánchez', employeeCode: 'EMP-0201', vehicle: 'A-782113 · Hyundai Tucson', department: 'Administración', fuelType: 'GASOLINA REGULAR', requestedGallons: 12, authorizedGallons: 12, requestedAt: '2026-09-21T15:30:00', expiresAt: '2026-09-23T18:00:00', kind: 'MANUAL', status: 'APROBADA', reason: 'Diligencias institucionales' },
  { id: 1045, employee: 'José Ramírez', employeeCode: 'EMP-0063', vehicle: 'L-399021 · Ford Ranger', department: 'Operaciones', fuelType: 'DIÉSEL', requestedGallons: 22, requestedAt: '2026-09-21T13:10:00', expiresAt: '2026-09-22T18:00:00', kind: 'AUTOMATICA', status: 'RECHAZADA', reason: 'Visita técnica programada' },
]

export const tickets: Ticket[] = [
  { id: 'b07e34c2-70be-4b38-81df-325431faf801', sequence: 'COM-2026-000184', employee: 'Carlos Méndez', vehicle: 'EG-01834', department: 'Mantenimiento', fuelType: 'DIÉSEL', gallons: 16, createdAt: '2026-09-22T07:31:00', expiresAt: '2026-09-24T18:00:00', status: 'ENVIADO' },
  { id: 'a938cb76-34cb-4f0f-a916-d80468e321df', sequence: 'COM-2026-000183', employee: 'Ana Sánchez', vehicle: 'A-782113', department: 'Administración', fuelType: 'GASOLINA REGULAR', gallons: 12, createdAt: '2026-09-21T16:02:00', expiresAt: '2026-09-23T18:00:00', status: 'PROXIMO_A_VENCER' },
  { id: '07887e2f-3927-428a-8374-c8fa95d3d234', sequence: 'COM-2026-000182', employee: 'Luis Herrera', vehicle: 'L-401277', department: 'Operaciones', fuelType: 'DIÉSEL', gallons: 20, createdAt: '2026-09-21T10:20:00', expiresAt: '2026-09-22T12:00:00', status: 'CONSUMIDO' },
  { id: 'e9a791c1-3d6c-4a1f-b78a-198499df6431', sequence: 'COM-2026-000181', employee: 'Patricia Gómez', vehicle: 'A-776502', department: 'Gestión Humana', fuelType: 'GASOLINA PREMIUM', gallons: 10, createdAt: '2026-09-20T09:10:00', expiresAt: '2026-09-22T09:10:00', status: 'VENCIDO' },
]

export const tanks: Tank[] = [
  { id: 1, code: 'TQ-D-01', name: 'Tanque diésel principal', station: 'Estación INTEC Central', fuelType: 'DIÉSEL', capacity: 5000, stock: 3725, criticalLevel: 900 },
  { id: 2, code: 'TQ-GR-01', name: 'Tanque gasolina regular', station: 'Estación INTEC Central', fuelType: 'GASOLINA REGULAR', capacity: 3000, stock: 684, criticalLevel: 750 },
  { id: 3, code: 'TQ-GP-01', name: 'Tanque gasolina premium', station: 'Estación INTEC Central', fuelType: 'GASOLINA PREMIUM', capacity: 2000, stock: 1280, criticalLevel: 400 },
]

export const movements: InventoryMovement[] = [
  { id: 6881, tank: 'TQ-D-01', type: 'SALIDA', gallons: 20, previous: 3745, current: 3725, reference: 'COM-2026-000182', user: 'Rafael Torres', date: '2026-09-22T09:18:00' },
  { id: 6880, tank: 'TQ-GR-01', type: 'SALIDA', gallons: 14, previous: 698, current: 684, reference: 'COM-2026-000180', user: 'Rafael Torres', date: '2026-09-22T08:46:00' },
  { id: 6879, tank: 'TQ-D-01', type: 'ENTRADA', gallons: 1200, previous: 2545, current: 3745, reference: 'FAC-B010000892', user: 'Elena Vargas', date: '2026-09-21T16:12:00' },
  { id: 6878, tank: 'TQ-GP-01', type: 'AJUSTE_NEGATIVO', gallons: 8, previous: 1288, current: 1280, reference: 'AJ-2026-041', user: 'Elena Vargas', date: '2026-09-21T14:30:00' },
]

export const employees: Employee[] = [
  { id: 1, code: 'EMP-0142', name: 'María Rodríguez', document: '001-1849203-7', department: 'Operaciones', position: 'Técnica de campo', email: 'maria.rodriguez@intec.edu.do', phone: '809-555-0142', active: true },
  { id: 2, code: 'EMP-0087', name: 'Carlos Méndez', document: '001-0938154-4', department: 'Mantenimiento', position: 'Encargado de mantenimiento', email: 'carlos.mendez@intec.edu.do', phone: '809-555-0087', active: true },
  { id: 3, code: 'EMP-0201', name: 'Ana Sánchez', document: '001-2047182-6', department: 'Administración', position: 'Analista administrativa', email: 'ana.sanchez@intec.edu.do', phone: '809-555-0201', active: true },
  { id: 4, code: 'EMP-0063', name: 'José Ramírez', document: '001-0721430-1', department: 'Operaciones', position: 'Supervisor de campo', email: 'jose.ramirez@intec.edu.do', phone: '809-555-0063', active: false },
]

export const vehicles: Vehicle[] = [
  { id: 1, plate: 'L-402918', code: 'VH-031', brand: 'Toyota', model: 'Hilux', year: 2024, type: 'Camioneta', department: 'Operaciones', tankCapacity: 21, odometer: 18420, active: true },
  { id: 2, plate: 'EG-01834', code: 'VH-018', brand: 'Kia', model: 'K2700', year: 2022, type: 'Camión ligero', department: 'Mantenimiento', tankCapacity: 16, odometer: 48218, active: true },
  { id: 3, plate: 'A-782113', code: 'VH-044', brand: 'Hyundai', model: 'Tucson', year: 2025, type: 'SUV', department: 'Administración', tankCapacity: 14.3, odometer: 12890, active: true },
]

export const departments: Department[] = [
  { id: 1, code: 'OPE', name: 'Operaciones', employees: 38, vehicles: 14, active: true },
  { id: 2, code: 'MAN', name: 'Mantenimiento', employees: 22, vehicles: 9, active: true },
  { id: 3, code: 'ADM', name: 'Administración', employees: 31, vehicles: 6, active: true },
  { id: 4, code: 'GH', name: 'Gestión Humana', employees: 12, vehicles: 2, active: true },
]

export const users: User[] = [
  { id: 1, name: 'Elena Vargas', username: 'evargas', email: 'elena.vargas@intec.edu.do', roles: ['ADMINISTRADOR'], lastAccess: '2026-09-22T09:42:00', active: true },
  { id: 2, name: 'Miguel Peña', username: 'mpena', email: 'miguel.pena@intec.edu.do', roles: ['SUPERVISOR'], lastAccess: '2026-09-22T09:28:00', active: true },
  { id: 3, name: 'Rafael Torres', username: 'rtorres', email: 'rafael.torres@intec.edu.do', roles: ['DESPACHADOR'], lastAccess: '2026-09-22T09:18:00', active: true },
  { id: 4, name: 'Laura Castillo', username: 'lcastillo', email: 'laura.castillo@intec.edu.do', roles: ['AUDITOR', 'CONSULTA'], lastAccess: '2026-09-21T16:58:00', active: true },
]

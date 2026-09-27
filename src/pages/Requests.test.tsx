/** @vitest-environment jsdom */
import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { Requests } from './Requests'

const context = vi.hoisted(() => ({ value: null as unknown }))
const scheduleApi = vi.hoisted(() => ({ list: vi.fn().mockResolvedValue([]) }))
vi.mock('../context/AppContext', () => ({ useApp: () => context.value }))
vi.mock('../services/api', async (importOriginal) => ({ ...(await importOriginal<typeof import('../services/api')>()), listProgrammedRequests: scheduleApi.list }))

afterEach(cleanup)

const catalogs = {
  employees: [{ id: 1, code: 'E-1', name: 'Eva QA' }],
  vehicles: [{ id: 2, plate: 'A-2', brand: 'Marca', model: 'Modelo' }],
  departments: [{ id: 3, name: 'Operaciones' }],
  fuelTypes: [{ id: 4, name: 'DIESEL' }],
}
const pendingRequest = { id: 20, employeeId: 1, employee: 'Eva QA', employeeCode: 'E-1', vehicleId: 2, vehicle: 'A-2 · Marca Modelo', departmentId: 3, department: 'Operaciones', fuelTypeId: 4, fuelType: 'DIESEL', requestedGallons: 10, requestedAt: '2026-09-10T12:00:00', expiresAt: '2026-09-12T12:00:00', kind: 'MANUAL', status: 'PENDIENTE', reason: 'Operación QA' }

describe('solicitudes y validación del formulario', () => {
  it('crea una solicitud mapeando catálogos y convierte fecha a ISO', async () => {
    const addRequest = vi.fn().mockResolvedValue(undefined)
    const notify = vi.fn()
    context.value = { requests: [], catalogs, addRequest, resolveRequest: vi.fn(), notify, session: { role: 'SOLICITANTE' } }
    render(<Requests />)
    fireEvent.click(screen.getByRole('button', { name: /Nueva solicitud/ }))
    fireEvent.change(screen.getByLabelText('Empleado'), { target: { value: '1' } })
    fireEvent.change(screen.getByLabelText('Vehículo'), { target: { value: '2' } })
    fireEvent.change(screen.getByLabelText('Departamento'), { target: { value: '3' } })
    fireEvent.change(screen.getByLabelText('Tipo de combustible'), { target: { value: '4' } })
    fireEvent.change(screen.getByLabelText('Galones solicitados'), { target: { value: '8' } })
    fireEvent.change(screen.getByLabelText('Fecha de vencimiento'), { target: { value: '2027-06-15T10:00' } })
    fireEvent.change(screen.getByLabelText('Motivo'), { target: { value: 'Ruta de prueba' } })
    fireEvent.submit(screen.getByRole('dialog').querySelector('form')!)
    await waitFor(() => expect(addRequest).toHaveBeenCalledOnce())
    expect(addRequest).toHaveBeenCalledWith(expect.objectContaining({ employeeId: 1, vehicleId: 2, departmentId: 3, fuelTypeId: 4, requestedGallons: 8, kind: 'MANUAL', reason: 'Ruta de prueba' }))
    expect(addRequest.mock.calls[0][0].expiresAt).toBe(new Date('2027-06-15T10:00').toISOString())
    expect(notify).not.toHaveBeenCalled()
  })

  it('bloquea una aprobación fuera de rango y comunica errores del flujo de decisión', async () => {
    const notify = vi.fn()
    const resolveRequest = vi.fn().mockRejectedValue(new Error('Solicitud procesada en otro turno'))
    context.value = { requests: [pendingRequest], catalogs, addRequest: vi.fn(), resolveRequest, notify, session: { id: 9, role: 'SUPERVISOR' } }
    render(<Requests />)
    fireEvent.click(screen.getByRole('row', { name: /Eva QA/ }))
    const amount = screen.getByLabelText('Galones autorizados')
    fireEvent.change(amount, { target: { value: '11' } })
    expect(screen.getByRole('button', { name: /Aprobar y emitir ticket/ })).toHaveProperty('disabled', true)
    fireEvent.change(amount, { target: { value: '7' } })
    fireEvent.click(screen.getByRole('button', { name: /Aprobar y emitir ticket/ }))
    await waitFor(() => expect(resolveRequest).toHaveBeenCalledWith(20, 'approve', 7))
    expect(notify).toHaveBeenCalledWith('No se pudo completar', 'Solicitud procesada en otro turno', 'error')
  })

  it('muestra estado vacío para solicitante sin datos', () => {
    context.value = { requests: [], catalogs, addRequest: vi.fn(), resolveRequest: vi.fn(), notify: vi.fn(), session: { role: 'CONSULTA' } }
    render(<Requests />)
    expect(screen.getByText('No hay solicitudes')).toBeTruthy()
    expect(screen.queryByRole('button', { name: /Nueva solicitud/ })).toBeNull()
  })
})

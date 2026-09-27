/** @vitest-environment jsdom */
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { ScheduleManager } from './ScheduleManager'

const context = vi.hoisted(() => ({ value: null as unknown }))
const apiMocks = vi.hoisted(() => ({ list: vi.fn(), create: vi.fn(), update: vi.fn(), toggle: vi.fn(), history: vi.fn() }))
vi.mock('../context/AppContext', () => ({ useApp: () => context.value }))
vi.mock('../services/api', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../services/api')>()),
  listProgrammedRequests: apiMocks.list,
  createProgrammedRequest: apiMocks.create,
  updateProgrammedRequest: apiMocks.update,
  setProgrammedRequestActive: apiMocks.toggle,
  programmedRequestHistory: apiMocks.history,
}))

afterEach(cleanup)
beforeEach(() => {
  apiMocks.list.mockReset().mockResolvedValue([])
  apiMocks.create.mockReset().mockResolvedValue({ id: 1 })
  apiMocks.update.mockReset().mockResolvedValue({ id: 1 })
  apiMocks.toggle.mockReset().mockResolvedValue({ id: 1 })
  apiMocks.history.mockReset().mockResolvedValue([])
  context.value = {
    catalogs: {
      employees: [{ id: 1, code: 'E-1', name: 'Eva QA', active: true }],
      vehicles: [{ id: 2, plate: 'A-2', brand: 'QA', model: 'Test', active: true }],
      departments: [{ id: 3, name: 'Operaciones', active: true }],
      fuelTypes: [{ id: 4, name: 'DIESEL', active: true }],
      suppliers: [], stations: [],
    },
    session: { role: 'ADMINISTRADOR' },
    notify: vi.fn(),
  }
})

describe('gestión de programaciones de solicitudes', () => {
  it('crea una recurrente con frecuencia y referencias seleccionadas', async () => {
    const beforeOpen = Date.now()
    render(<ScheduleManager />)
    fireEvent.click(await screen.findByRole('button', { name: 'Nueva programación' }))
    const suggestedStart = new Date((screen.getByLabelText('Fecha y hora inicial (hora local; se guarda en UTC)') as HTMLInputElement).value).getTime()
    expect(suggestedStart).toBeGreaterThanOrEqual(beforeOpen + 50_000)
    expect(suggestedStart).toBeLessThanOrEqual(beforeOpen + 75_000)
    fireEvent.change(screen.getByLabelText('Tipo'), { target: { value: 'RECURRENTE' } })
    fireEvent.change(screen.getByLabelText('Frecuencia'), { target: { value: 'MENSUAL' } })
    fireEvent.change(screen.getByLabelText('Empleado'), { target: { value: '1' } })
    fireEvent.change(screen.getByLabelText('Vehículo'), { target: { value: '2' } })
    fireEvent.change(screen.getByLabelText('Departamento'), { target: { value: '3' } })
    fireEvent.change(screen.getByLabelText('Combustible'), { target: { value: '4' } })
    fireEvent.change(screen.getByLabelText('Galones'), { target: { value: '8.25' } })
    fireEvent.change(screen.getByLabelText('Fecha y hora inicial (hora local; se guarda en UTC)'), { target: { value: '2026-10-31T12:00' } })
    fireEvent.submit(screen.getByRole('dialog').querySelector('form')!)
    await waitFor(() => expect(apiMocks.create).toHaveBeenCalledOnce())
    expect(apiMocks.create).toHaveBeenCalledWith(expect.objectContaining({
      tipoSolicitud: 'RECURRENTE', empleadoId: 1, vehiculoId: 2, departamentoId: 3,
      tipoCombustibleId: 4, cantidadSolicitadaGalones: 8.25, frecuencia: 'MENSUAL',
    }))
    expect(new Date(apiMocks.create.mock.calls[0][0].fechaInicial).toISOString()).toBe(new Date('2026-10-31T12:00').toISOString())
  })

  it('muestra próxima y última ejecución, consulta historial y permite pausar', async () => {
    apiMocks.list.mockResolvedValue([{ id: 7, tipoSolicitud: 'RECURRENTE', empleadoId: 1, vehiculoId: 2, departamentoId: 3, tipoCombustibleId: 4, cantidadSolicitadaGalones: 5, fechaInicial: '2026-09-01T12:00:00Z', fechaFinal: null, frecuencia: 'SEMANAL', proximaEjecucion: '2026-10-01T12:00:00Z', ultimaEjecucion: '2026-09-24T12:00:00Z', activa: true, usuarioCreadorId: 1, creadoEn: '2026-09-01T12:00:00Z', actualizadoEn: '2026-09-01T12:00:00Z' }])
    apiMocks.history.mockResolvedValue([{ id: 10, programacionId: 7, fechaProgramada: '2026-09-24T12:00:00Z', ejecutadaEn: '2026-09-24T12:01:00Z', estado: 'GENERADA', solicitudGeneradaId: 18, estadoSolicitud: 'PENDIENTE', detalleError: null }])
    render(<ScheduleManager />)
    expect(await screen.findByText('Eva QA · A-2')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Historial programación 7' }))
    expect(await screen.findByText(/solicitud #18/)).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Desactivar programación 7' }))
    await waitFor(() => expect(apiMocks.toggle).toHaveBeenCalledWith(7, false))
  })

  it('expone los errores reales de la API y oculta el administrador a roles no autorizados', async () => {
    apiMocks.list.mockRejectedValueOnce(new Error('Permiso denegado por la API'))
    render(<ScheduleManager />)
    expect((await screen.findByRole('alert')).textContent).toContain('Permiso denegado por la API')
    context.value = { ...context.value as object, session: { role: 'DESPACHADOR' } }
    cleanup()
    render(<ScheduleManager />)
    expect(screen.queryByRole('region', { name: 'Programaciones automáticas' })).toBeNull()
  })
})

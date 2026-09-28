/** @vitest-environment jsdom */
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import * as api from '../services/api'
import * as ui from '../components/ui'
import { Reports } from './Reports'

const state = vi.hoisted(() => ({ value: null as unknown }))
vi.mock('../context/AppContext', () => ({ useApp: () => state.value }))
vi.mock('../services/api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../services/api')>()
  return { ...actual, getReport: vi.fn(), exportReport: vi.fn() }
})

afterEach(() => { cleanup(); vi.restoreAllMocks() })

const rows: api.ReportRow[] = [
  { id: 1, fechaUtc: '2026-09-10T12:00:00', tipo: 'DESPACHO', ticket: 'COM-2026-000001', empleado: 'Eva', vehiculo: 'A-1', departamento: 'Operaciones', combustible: 'DIESEL', estacion: 'Central', tanque: 'TQ-1', estado: 'CONSUMIDO', galones: 7, referencia: null, usuario: 'Operador' },
  { id: 2, fechaUtc: '2026-09-11T09:00:00', tipo: 'DESPACHO', ticket: 'COM-2026-000002', empleado: 'Luis', vehiculo: 'B-2', departamento: 'Transporte', combustible: 'GASOLINA', estacion: 'Central', tanque: 'TQ-2', estado: 'CONSUMIDO', galones: 20, referencia: null, usuario: 'Operador' },
  { id: 3, fechaUtc: '2026-09-20T09:00:00', tipo: 'DESPACHO', ticket: 'COM-2026-000003', empleado: 'Mia', vehiculo: 'C-3', departamento: 'Operaciones', combustible: 'DIESEL', estacion: 'Central', tanque: 'TQ-1', estado: 'CONSUMIDO', galones: 18, referencia: null, usuario: 'Operador' },
]

function report(items = rows): api.ReportResult {
  const gallons = items.reduce((sum, row) => sum + row.galones, 0)
  return {
    tipo: 'consumo', rangoUtc: '2026-09-01–2026-09-30 (UTC, ambos inclusive)', generadoEnUtc: '2026-09-27T12:00:00Z',
    filtros: { tipo: 'consumo' }, pagina: 1, tamanoPagina: 50, totalRegistros: items.length,
    totales: { registros: items.length, galones: gallons, despachos: items.length, tickets: items.length, solicitudes: 5, inventarioActualGalones: 100 },
    porDepartamento: items.length ? [{ nombre: 'Operaciones', galones: items.filter((x) => x.departamento === 'Operaciones').reduce((s, x) => s + x.galones, 0), cantidad: 2 }, { nombre: 'Transporte', galones: items.filter((x) => x.departamento === 'Transporte').reduce((s, x) => s + x.galones, 0), cantidad: 1 }].filter((x) => x.galones > 0) : [],
    porCombustible: items.length ? [{ nombre: 'DIESEL', galones: items.filter((x) => x.combustible === 'DIESEL').reduce((s, x) => s + x.galones, 0), cantidad: 2 }, { nombre: 'GASOLINA', galones: items.filter((x) => x.combustible === 'GASOLINA').reduce((s, x) => s + x.galones, 0), cantidad: 1 }].filter((x) => x.galones > 0) : [],
    porVehiculo: [], porEmpleado: [], porEstado: [], items,
  }
}

const catalogs = {
  departments: [{ id: 1, name: 'Operaciones' }, { id: 2, name: 'Transporte' }],
  fuelTypes: [{ id: 1, name: 'DIESEL' }, { id: 2, name: 'GASOLINA' }],
  employees: [{ id: 1, name: 'Eva' }], vehicles: [{ id: 1, plate: 'A-1' }], stations: [{ id: 1, name: 'Central' }],
}

describe('reportes y filtros desde API', () => {
  beforeEach(() => {
    state.value = { catalogs }
    vi.mocked(api.getReport).mockResolvedValue(report())
    vi.mocked(api.exportReport).mockResolvedValue({ blob: new Blob(['reporte']), filename: 'reporte-consumo-2026-09-27.csv' })
  })

  it('muestra agregados y despachos que provienen del reporte del servidor', async () => {
    render(<Reports />)
    expect(await screen.findByText('45 gal')).toBeTruthy()
    expect(screen.getByText('3 tickets asociados')).toBeTruthy()
    expect(screen.getAllByText('Operaciones').length).toBeGreaterThanOrEqual(1)
    expect(screen.getByText('COM-2026-000001')).toBeTruthy()
    expect(api.getReport).toHaveBeenCalledWith({ tipo: 'consumo', pagina: 1, tamanoPagina: 50 })
    fireEvent.change(screen.getByLabelText('Desde'), { target: { value: '2026-09-10' } })
    fireEvent.change(screen.getByLabelText('Hasta'), { target: { value: '2026-09-12' } })
    fireEvent.change(screen.getByLabelText('Departamento'), { target: { value: '1' } })
    fireEvent.change(screen.getByLabelText('Combustible'), { target: { value: '1' } })
    vi.mocked(api.getReport).mockResolvedValueOnce(report([rows[0]]))
    fireEvent.click(screen.getByRole('button', { name: /Aplicar filtros/ }))
    await waitFor(() => expect(api.getReport).toHaveBeenLastCalledWith({ tipo: 'consumo', pagina: 1, tamanoPagina: 50, desde: '2026-09-10', hasta: '2026-09-12', departamentoId: 1, combustibleId: 1 }))
    expect((await screen.findAllByText('7 gal')).length).toBeGreaterThanOrEqual(1)
    expect(screen.queryByText('COM-2026-000002')).toBeNull()
  })

  it('exporta el formato elegido usando los filtros aplicados y descarga el filename del servidor', async () => {
    const download = vi.spyOn(ui, 'downloadBlob').mockImplementation(() => {})
    render(<Reports />)
    await screen.findByText('45 gal')
    fireEvent.change(screen.getByLabelText('Departamento'), { target: { value: '1' } })
    fireEvent.click(screen.getByRole('button', { name: /Aplicar filtros/ }))
    await waitFor(() => expect(api.getReport).toHaveBeenLastCalledWith(expect.objectContaining({ departamentoId: 1 })))
    fireEvent.click(screen.getByRole('button', { name: 'Excel' }))
    await waitFor(() => expect(api.exportReport).toHaveBeenCalledWith({ tipo: 'consumo', pagina: undefined, tamanoPagina: undefined, departamentoId: 1 }, 'xlsx'))
    await waitFor(() => expect(download).toHaveBeenCalledWith('reporte-consumo-2026-09-27.csv', expect.any(Blob)))
    fireEvent.click(screen.getByRole('button', { name: 'PDF' }))
    await waitFor(() => expect(api.exportReport).toHaveBeenCalledWith(expect.objectContaining({ departamentoId: 1 }), 'pdf'))
    fireEvent.click(screen.getByRole('button', { name: 'CSV' }))
    await waitFor(() => expect(api.exportReport).toHaveBeenCalledWith(expect.objectContaining({ departamentoId: 1 }), 'csv'))
  })

  it('muestra loading, errores de API y resultado sin filas', async () => {
    let resolveReport!: (value: api.ReportResult) => void
    vi.mocked(api.getReport).mockReturnValueOnce(new Promise((resolve) => { resolveReport = resolve }))
    render(<Reports />)
    expect(screen.getByRole('status').textContent).toContain('Cargando')
    resolveReport(report([]))
    expect(await screen.findByText('Sin resultados')).toBeTruthy()
    vi.mocked(api.getReport).mockRejectedValueOnce(new Error('API no disponible'))
    fireEvent.change(screen.getByLabelText('Reporte'), { target: { value: 'tickets' } })
    fireEvent.click(screen.getByRole('button', { name: /Aplicar filtros/ }))
    expect((await screen.findByRole('alert')).textContent).toContain('API no disponible')
  })

  it('solicita otras páginas con el mismo filtro y evita saltos fuera de rango', async () => {
    const largeReport = { ...report(), totalRegistros: 61 }
    vi.mocked(api.getReport).mockResolvedValue(largeReport)
    render(<Reports />)
    await screen.findByText('Página 1 de 2')
    fireEvent.click(screen.getByRole('button', { name: 'Siguiente' }))
    await waitFor(() => expect(api.getReport).toHaveBeenLastCalledWith({ tipo: 'consumo', pagina: 2, tamanoPagina: 50 }))
    expect(await screen.findByText('Página 2 de 2')).toBeTruthy()
  })
})

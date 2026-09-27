/** @vitest-environment jsdom */
import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import * as ui from '../components/ui'
import { Reports } from './Reports'

const state = vi.hoisted(() => ({ value: null as unknown }))
vi.mock('../context/AppContext', () => ({ useApp: () => state.value }))

afterEach(cleanup)

const sampleTickets = [
  { id: '1', sequence: 'COM-2026-000001', employee: 'Eva', vehicle: 'A-1', department: 'Operaciones', fuelType: 'DIESEL', gallons: 10, createdAt: '2026-09-10T12:00:00', expiresAt: '2026-09-12', status: 'CONSUMIDO' },
  { id: '2', sequence: 'COM-2026-000002', employee: 'Luis', vehicle: 'B-2', department: 'Transporte', fuelType: 'GASOLINA', gallons: 20, createdAt: '2026-09-11T12:00:00', expiresAt: '2026-09-13', status: 'CREADO' },
  { id: '3', sequence: 'COM-2026-000003', employee: 'Mia', vehicle: 'C-3', department: 'Operaciones', fuelType: 'DIESEL', gallons: 15, createdAt: '2026-09-20T12:00:00', expiresAt: '2026-09-23', status: 'CONSUMIDO' },
]

describe('reportes y filtros', () => {
  it('agrega galones, consumidos y despachos reales según el filtro aplicado', () => {
    state.value = {
      tickets: sampleTickets,
      movements: [
        { id: 1, type: 'SALIDA', gallons: 7, date: '2026-09-11T09:00:00' },
        { id: 2, type: 'AJUSTE_NEGATIVO', gallons: 5, date: '2026-09-11T10:00:00' },
        { id: 3, type: 'SALIDA', gallons: 6, date: '2026-09-20T09:00:00' },
      ],
      catalogs: { departments: [{ id: 1, name: 'Operaciones' }, { id: 2, name: 'Transporte' }], fuelTypes: [{ id: 1, name: 'DIESEL' }, { id: 2, name: 'GASOLINA' }] },
    }
    render(<Reports />)
    expect(screen.getByText('45 gal')).toBeTruthy()
    expect(screen.getByText('2 / 3')).toBeTruthy()
    fireEvent.change(screen.getByLabelText('Desde'), { target: { value: '2026-09-10' } })
    fireEvent.change(screen.getByLabelText('Hasta'), { target: { value: '2026-09-12' } })
    fireEvent.change(screen.getByLabelText('Departamento'), { target: { value: 'Operaciones' } })
    fireEvent.change(screen.getByLabelText('Combustible'), { target: { value: 'DIESEL' } })
    expect(screen.getAllByText('10 gal')).toHaveLength(3)
    expect(screen.getByText('1 / 1')).toBeTruthy()
    expect(screen.getByText('7 gal')).toBeTruthy()
    expect(screen.getAllByText('Operaciones')).toHaveLength(2)
  })

  it('exporta solo los tickets visibles y cubre el resultado vacío', () => {
    state.value = { tickets: sampleTickets, movements: [], catalogs: { departments: [{ id: 1, name: 'Operaciones' }], fuelTypes: [{ id: 1, name: 'DIESEL' }] } }
    const exporter = vi.spyOn(ui, 'downloadCsv').mockImplementation(() => {})
    const { rerender } = render(<Reports />)
    fireEvent.change(screen.getByLabelText('Departamento'), { target: { value: 'Operaciones' } })
    fireEvent.click(screen.getByRole('button', { name: /Exportar CSV/ }))
    expect(exporter).toHaveBeenCalledWith('lavomva-tickets.csv', expect.arrayContaining([expect.arrayContaining(['COM-2026-000001']), expect.arrayContaining(['COM-2026-000003'])]))
    expect(exporter.mock.calls[0][1]).not.toEqual(expect.arrayContaining([expect.arrayContaining(['COM-2026-000002'])]))
    state.value = { tickets: [], movements: [], catalogs: { departments: [], fuelTypes: [] } }
    rerender(<Reports />)
    expect(screen.getAllByText('Sin tickets para estos filtros.')).toHaveLength(2)
    exporter.mockRestore()
  })
})

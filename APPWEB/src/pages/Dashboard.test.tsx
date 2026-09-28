/** @vitest-environment jsdom */
import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { Dashboard } from './Dashboard'

const state = vi.hoisted(() => ({ value: null as unknown }))
vi.mock('../context/AppContext', () => ({ useApp: () => state.value }))

afterEach(cleanup)

describe('tablero', () => {
  it('calcula existencias, solicitudes, tickets y salidas de hoy; navega desde avisos', () => {
    const now = new Date()
    const timestamp = now.toISOString()
    state.value = {
      session: { name: 'Ana QA Operaciones' },
      requests: [{ id: 1, employee: 'Eva Persona', vehicle: 'A-1 · Marca Modelo', requestedGallons: 12, requestedAt: timestamp, status: 'PENDIENTE' }],
      tickets: [{ id: 'uuid', sequence: 'COM-2026-1', status: 'CREADO' }, { id: 'uuid-2', sequence: 'COM-2026-2', status: 'CONSUMIDO' }],
      tanks: [
        { id: 1, code: 'T-1', stock: 10, capacity: 50, criticalLevel: 10, fuelType: 'DIESEL' },
        { id: 2, code: 'T-2', stock: 40, capacity: 100, criticalLevel: 5, fuelType: 'GASOLINA' },
      ],
      movements: [{ id: 1, type: 'SALIDA', gallons: 12, date: timestamp, reference: 'Ticket', user: 'Operador' }],
    }
    const onNavigate = vi.fn()
    render(<Dashboard onNavigate={onNavigate} />)
    expect(screen.getByRole('heading', { name: 'Hola, Ana' })).toBeTruthy()
    expect(screen.getByText('50 gal')).toBeTruthy()
    expect(screen.getByText('12 gal')).toBeTruthy()
    expect(screen.getByText('Solicitudes pendientes')).toBeTruthy()
    expect(screen.getByText('Atención: inventario por debajo del nivel crítico')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: /Atención: inventario/ }))
    expect(onNavigate).toHaveBeenCalledWith('inventario')
    fireEvent.click(screen.getByRole('button', { name: /Revisar solicitudes/ }))
    expect(onNavigate).toHaveBeenCalledWith('solicitudes')
  })

  it('usa valores vacíos seguros cuando no hay tanques, actividad ni sesión', () => {
    state.value = { session: null, requests: [], tickets: [], tanks: [], movements: [] }
    render(<Dashboard onNavigate={vi.fn()} />)
    expect(screen.getByRole('heading', { name: 'Hola, equipo' })).toBeTruthy()
    expect(screen.getByText('0% de capacidad disponible')).toBeTruthy()
    expect(screen.queryByText('Atención: inventario por debajo del nivel crítico')).toBeNull()
  })
})

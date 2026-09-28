// @vitest-environment jsdom
import { act, cleanup, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { AppProvider } from './AppContext'
import { Dashboard } from '../pages/Dashboard'
import type { InventoryMovement, Tank } from '../types'
import type { CriticalInventoryChangedEvent, InventoryMovementCreatedEvent, InventoryUpdatedEvent } from '../services/inventoryRealtime'

const state = vi.hoisted(() => ({ handlers: null as null | Record<string, (...args: any[]) => any>, getToken: null as null | (() => string | undefined), stop: vi.fn() }))
const session = { token: 'qa-web-jwt', id: 17, name: 'QA User', role: 'CONSULTA' }
const tank: Tank = { id: 1, code: 'T-1', name: 'Tanque 1', station: 'Estación 1', stationId: 3, fuelType: 'Diesel', capacity: 100, stock: 50, criticalLevel: 10 }
const previousMovement: InventoryMovement = { id: 12, tankId: 1, tank: 'T-1', type: 'ENTRADA', gallons: 50, previous: 0, current: 50, reference: 'Carga', user: 'QA', date: '2026-09-27T11:00:00Z' }

vi.mock('../services/api', () => ({
  api: {}, clearSession: vi.fn(), savedSession: () => session,
  login: vi.fn(), register: vi.fn(),
  loadLiveData: vi.fn(async () => ({ catalogs: { employees: [], vehicles: [], departments: [], fuelTypes: [], suppliers: [], stations: [] }, requests: [], tickets: [], tanks: [tank], movements: [previousMovement] })),
}))
vi.mock('../services/inventoryRealtime', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../services/inventoryRealtime')>()
  return {
    ...actual,
    createInventoryRealtimeClient: vi.fn((options: { getToken: () => string | undefined; [key: string]: any }) => {
      state.handlers = options as Record<string, (...args: any[]) => any>
      state.getToken = options.getToken
      return { stop: state.stop }
    }),
  }
})

beforeEach(() => { state.handlers = null; state.getToken = null; session.token = 'qa-web-jwt'; state.stop.mockReset() })
afterEach(cleanup)

describe('AppProvider inventory realtime integration', () => {
  it('updates Dashboard, critical banner and recent activity from one shared connection, then cleans up', async () => {
    const view = render(<AppProvider><Dashboard onNavigate={vi.fn()} /></AppProvider>)
    await waitFor(() => expect(screen.getAllByText('50 gal').length).toBeGreaterThan(0))
    expect(state.handlers).not.toBeNull()

    const update: InventoryUpdatedEvent = { tankId: 1, stationId: 3, previousQuantity: 50, currentQuantity: 8, capacity: 100, percentage: 8, critical: true, movementType: 'SALIDA', quantity: 42, movementId: 14, occurredAt: '2026-09-27T12:00:00Z' }
    const created: InventoryMovementCreatedEvent = { movementId: 14, tankId: 1, tankCode: 'T-1', stationId: 3, movementType: 'SALIDA', quantity: 42, previousQuantity: 50, currentQuantity: 8, referenceType: 'DESPACHO', referenceId: '77', occurredAt: update.occurredAt }
    const critical: CriticalInventoryChangedEvent = { tankId: 1, stationId: 3, critical: true, currentQuantity: 8, criticalLevel: 10, movementId: 14, occurredAt: update.occurredAt }

    act(() => state.handlers?.onInventoryUpdated(update))
    act(() => state.handlers?.onMovementCreated(created))
    act(() => state.handlers?.onCriticalInventoryChanged(critical))
    expect((await screen.findAllByText('8 gal')).length).toBeGreaterThan(0)
    expect(screen.getByText('Atención: inventario por debajo del nivel crítico')).toBeTruthy()
    expect(screen.getAllByText('SALIDA').length).toBeGreaterThan(0)

    // A delayed event from the same tank must not roll the Dashboard back.
    act(() => state.handlers?.onInventoryUpdated({ ...update, movementId: 13, currentQuantity: 45 }))
    expect(screen.getAllByText('8 gal').length).toBeGreaterThan(0)
    expect(state.handlers).not.toBeNull()
    view.unmount()
    expect(state.stop).toHaveBeenCalledOnce()
  })

  it('SignalR solicita siempre el access token más reciente después de la rotación', async () => {
    const view = render(<AppProvider><Dashboard onNavigate={vi.fn()} /></AppProvider>)
    await waitFor(() => expect(state.getToken).not.toBeNull())
    expect(state.getToken?.()).toBe('qa-web-jwt')
    session.token = 'qa-refreshed-jwt'
    expect(state.getToken?.()).toBe('qa-refreshed-jwt')
    view.unmount()
  })
})

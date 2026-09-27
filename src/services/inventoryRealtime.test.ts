import { act } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { InventoryMovement, Tank } from '../types'

const mock = vi.hoisted(() => {
  const handlers = new Map<string, (payload?: unknown) => void>()
  const lifecycle = new Map<string, (error?: Error) => void>()
  const connection = {
    state: 'Disconnected',
    on: vi.fn((name: string, handler: (payload?: unknown) => void) => handlers.set(name, handler)),
    off: vi.fn((name: string) => handlers.delete(name)),
    onreconnected: vi.fn((handler: (error?: Error) => void) => lifecycle.set('reconnected', handler)),
    onclose: vi.fn((handler: (error?: Error) => void) => lifecycle.set('close', handler)),
    start: vi.fn(async () => { connection.state = 'Connected' }),
    stop: vi.fn(async () => { connection.state = 'Disconnected' }),
  }
  const withUrl = vi.fn()
  class Builder {
    withUrl(...args: unknown[]) { withUrl(...args); return this }
    withAutomaticReconnect = vi.fn(() => this)
    build = vi.fn(() => connection)
  }
  return { handlers, lifecycle, connection, withUrl, Builder }
})

vi.mock('@microsoft/signalr', () => ({ HubConnectionBuilder: mock.Builder }))

import {
  applyInventoryUpdate, applyMovementCreated, createInventoryRealtimeClient,
  initialTankMovementIds, isNewerTankMovement,
  type CriticalInventoryChangedEvent, type InventoryMovementCreatedEvent, type InventoryUpdatedEvent,
} from './inventoryRealtime'

const tanks: Tank[] = [
  { id: 1, code: 'T-1', name: 'Tanque 1', station: 'S-1', stationId: 10, capacity: 100, stock: 50, criticalLevel: 10, fuelType: 'Diesel' },
  { id: 2, code: 'T-2', name: 'Tanque 2', station: 'S-1', stationId: 10, capacity: 80, stock: 20, criticalLevel: 5, fuelType: 'Gasolina' },
]
const update: InventoryUpdatedEvent = { tankId: 1, stationId: 10, previousQuantity: 50, currentQuantity: 42, capacity: 100, percentage: 42, critical: false, movementType: 'SALIDA', quantity: 8, movementId: 14, occurredAt: '2026-09-27T12:00:00Z' }
const movement: InventoryMovementCreatedEvent = { movementId: 14, tankId: 1, tankCode: 'T-1', stationId: 10, movementType: 'SALIDA', quantity: 8, previousQuantity: 50, currentQuantity: 42, referenceType: 'DESPACHO', referenceId: '99', occurredAt: '2026-09-27T12:00:00Z' }
const initialMovements: InventoryMovement[] = [{ id: 12, tankId: 1, tank: 'T-1', type: 'ENTRADA', gallons: 10, previous: 40, current: 50, reference: '1', user: 'QA', date: '2026-09-27T11:00:00Z' }]

beforeEach(() => {
  mock.handlers.clear()
  mock.lifecycle.clear()
  mock.connection.state = 'Disconnected'
  vi.clearAllMocks()
})

describe('inventory realtime client', () => {
  it('starts one authenticated connection at the Hub endpoint', async () => {
    const client = createInventoryRealtimeClient({ getToken: () => 'jwt-test', synchronize: vi.fn(async () => {}), onInventoryUpdated: vi.fn(), onMovementCreated: vi.fn(), onCriticalInventoryChanged: vi.fn() })
    await vi.waitFor(() => expect(mock.connection.start).toHaveBeenCalledOnce())
    expect(mock.withUrl).toHaveBeenCalledWith(expect.stringMatching(/\/hubs\/inventory$/), expect.objectContaining({ accessTokenFactory: expect.any(Function) }))
    expect(mock.withUrl.mock.calls[0][1] && (mock.withUrl.mock.calls[0][1] as { accessTokenFactory: () => string }).accessTokenFactory()).toBe('jwt-test')
    await client.stop()
  })

  it('delivers tank, movement and critical events and removes listeners on cleanup', async () => {
    const onInventoryUpdated = vi.fn()
    const onMovementCreated = vi.fn()
    const onCriticalInventoryChanged = vi.fn()
    const client = createInventoryRealtimeClient({ getToken: () => 'jwt', synchronize: vi.fn(async () => {}), onInventoryUpdated, onMovementCreated, onCriticalInventoryChanged })
    const critical: CriticalInventoryChangedEvent = { tankId: 1, stationId: 10, critical: true, currentQuantity: 4, criticalLevel: 5, movementId: 15, occurredAt: update.occurredAt }
    await act(async () => {
      mock.handlers.get('InventoryUpdated')?.(update)
      mock.handlers.get('InventoryMovementCreated')?.(movement)
      mock.handlers.get('CriticalInventoryChanged')?.(critical)
    })
    expect(onInventoryUpdated).toHaveBeenCalledWith(update)
    expect(onMovementCreated).toHaveBeenCalledWith(movement)
    expect(onCriticalInventoryChanged).toHaveBeenCalledWith(critical)
    await client.stop()
    expect(mock.connection.off).toHaveBeenCalledTimes(3)
    expect(mock.handlers.size).toBe(0)
  })

  it('synchronizes REST after SignalR reconnects', async () => {
    const synchronize = vi.fn(async () => {})
    const onReconnected = vi.fn()
    const client = createInventoryRealtimeClient({ getToken: () => 'jwt', synchronize, onReconnected, onInventoryUpdated: vi.fn(), onMovementCreated: vi.fn(), onCriticalInventoryChanged: vi.fn() })
    await act(async () => { mock.lifecycle.get('reconnected')?.() })
    expect(onReconnected).toHaveBeenCalledOnce()
    expect(synchronize).toHaveBeenCalledOnce()
    await client.stop()
  })

  it('retries an initial connection with a bounded delay', async () => {
    vi.useFakeTimers()
    mock.connection.start.mockRejectedValueOnce(new Error('offline')).mockImplementation(async () => { mock.connection.state = 'Connected' })
    const client = createInventoryRealtimeClient({ getToken: () => 'jwt', synchronize: vi.fn(async () => {}), onInventoryUpdated: vi.fn(), onMovementCreated: vi.fn(), onCriticalInventoryChanged: vi.fn() })
    await Promise.resolve()
    await Promise.resolve()
    expect(mock.connection.start).toHaveBeenCalledOnce()
    await vi.advanceTimersByTimeAsync(2_000)
    expect(mock.connection.start).toHaveBeenCalledTimes(2)
    await client.stop()
    vi.useRealTimers()
  })

  it('updates only the matching tank and keeps the newest movement first once', () => {
    const changed = applyInventoryUpdate(tanks, update)
    expect(changed[0].stock).toBe(42)
    expect(changed[1]).toBe(tanks[1])
    const first = applyMovementCreated(initialMovements, tanks, movement)
    expect(first.map((item) => item.id)).toEqual([14, 12])
    expect(first[0]).toMatchObject({ tankId: 1, tank: 'T-1', current: 42, type: 'SALIDA' })
    expect(applyMovementCreated(first, tanks, movement)).toBe(first)
  })

  it('rejects out-of-order tank events while accepting the next persisted movement', () => {
    const versions = initialTankMovementIds(initialMovements)
    expect(versions.get(1)).toBe(12)
    expect(isNewerTankMovement(versions, 1, 14)).toBe(true)
    expect(isNewerTankMovement(versions, 1, 13)).toBe(false)
    expect(isNewerTankMovement(versions, 2, 1)).toBe(true)
  })
})

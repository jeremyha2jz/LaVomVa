import { HubConnectionBuilder } from '@microsoft/signalr'
import type { InventoryMovement, Tank } from '../types'

export type InventoryUpdatedEvent = {
  tankId: number; stationId: number; previousQuantity: number; currentQuantity: number
  capacity: number; percentage: number; critical: boolean; movementType: InventoryMovement['type']
  quantity: number; movementId: number; occurredAt: string
}
export type InventoryMovementCreatedEvent = {
  movementId: number; tankId: number; tankCode: string; stationId: number; movementType: InventoryMovement['type']
  quantity: number; previousQuantity: number; currentQuantity: number; referenceType: string | null
  referenceId: string | null; occurredAt: string
}
export type CriticalInventoryChangedEvent = {
  tankId: number; stationId: number; critical: boolean; currentQuantity: number
  criticalLevel: number; movementId: number; occurredAt: string
}

type Handlers = {
  onInventoryUpdated: (event: InventoryUpdatedEvent) => void
  onMovementCreated: (event: InventoryMovementCreatedEvent) => void
  onCriticalInventoryChanged: (event: CriticalInventoryChangedEvent) => void
  synchronize: () => Promise<void>
  getToken: () => string
  onConnectionChange?: (connected: boolean) => void
  onReconnected?: () => void
}

function hubUrl() {
  const apiUrl = (import.meta.env.VITE_API_URL || '/api').replace(/\/$/, '')
  if (/^https?:\/\//.test(apiUrl)) return `${apiUrl.replace(/\/api$/, '')}/hubs/inventory`
  const origin = typeof window === 'undefined' ? 'http://localhost' : window.location.origin
  return `${origin}${apiUrl.replace(/\/api$/, '')}/hubs/inventory`
}

export function createInventoryRealtimeClient(handlers: Handlers) {
  const connection = new HubConnectionBuilder()
    .withUrl(hubUrl(), { accessTokenFactory: handlers.getToken })
    .withAutomaticReconnect([0, 2_000, 10_000, 30_000])
    .build()
  let disposed = false
  let retry = 0
  let retryTimer: ReturnType<typeof setTimeout> | undefined

  const inventoryUpdated = (event: InventoryUpdatedEvent) => handlers.onInventoryUpdated(event)
  const movementCreated = (event: InventoryMovementCreatedEvent) => handlers.onMovementCreated(event)
  const criticalChanged = (event: CriticalInventoryChangedEvent) => handlers.onCriticalInventoryChanged(event)
  connection.on('InventoryUpdated', inventoryUpdated)
  connection.on('InventoryMovementCreated', movementCreated)
  connection.on('CriticalInventoryChanged', criticalChanged)

  const syncAfterReconnect = async () => {
    retry = 0
    handlers.onConnectionChange?.(true)
    handlers.onReconnected?.()
    await handlers.synchronize()
  }
  connection.onreconnected(syncAfterReconnect)
  connection.onclose(() => {
    handlers.onConnectionChange?.(false)
    if (!disposed) scheduleStart()
  })

  function scheduleStart() {
    if (disposed || retryTimer) return
    const delays = [2_000, 5_000, 10_000, 30_000]
    const delay = delays[Math.min(retry, delays.length - 1)]
    retry += 1
    retryTimer = setTimeout(() => {
      retryTimer = undefined
      void start()
    }, delay)
  }

  async function start() {
    if (disposed || connection.state !== 'Disconnected') return
    try {
      await connection.start()
      if (disposed) {
        await connection.stop()
        return
      }
      retry = 0
      handlers.onConnectionChange?.(true)
    } catch {
      handlers.onConnectionChange?.(false)
      scheduleStart()
    }
  }

  void start()
  return {
    connection,
    stop: async () => {
      disposed = true
      if (retryTimer) clearTimeout(retryTimer)
      retryTimer = undefined
      connection.off('InventoryUpdated', inventoryUpdated)
      connection.off('InventoryMovementCreated', movementCreated)
      connection.off('CriticalInventoryChanged', criticalChanged)
      await connection.stop()
    },
  }
}

export function applyInventoryUpdate(tanks: Tank[], event: InventoryUpdatedEvent): Tank[] {
  return tanks.map((tank) => tank.id === event.tankId
    ? { ...tank, stock: event.currentQuantity, capacity: event.capacity }
    : tank)
}

export function applyMovementCreated(
  movements: InventoryMovement[], tanks: Tank[], event: InventoryMovementCreatedEvent,
): InventoryMovement[] {
  if (movements.some((movement) => movement.id === event.movementId)) return movements
  const tank = tanks.find((item) => item.id === event.tankId)
  const movement: InventoryMovement = {
    id: event.movementId,
    tankId: event.tankId,
    tank: tank?.code ?? event.tankCode,
    type: event.movementType,
    gallons: event.quantity,
    previous: event.previousQuantity,
    current: event.currentQuantity,
    reference: event.referenceId ?? event.referenceType ?? '—',
    user: 'Sistema',
    date: event.occurredAt,
  }
  return [movement, ...movements].sort((a, b) => b.id - a.id).slice(0, 100)
}

export function isNewerTankMovement(lastIds: Map<number, number>, tankId: number, movementId: number) {
  const previous = lastIds.get(tankId) ?? 0
  if (movementId <= previous) return false
  lastIds.set(tankId, movementId)
  return true
}

export function initialTankMovementIds(movements: InventoryMovement[]) {
  return movements.reduce((latest, movement) => {
    if (movement.tankId !== undefined) latest.set(movement.tankId, Math.max(latest.get(movement.tankId) ?? 0, movement.id))
    return latest
  }, new Map<number, number>())
}

/** @vitest-environment jsdom */
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { AppShell } from './AppShell'

const context = vi.hoisted(() => ({ value: null as unknown }))
const api = vi.hoisted(() => ({
  listNotifications: vi.fn(), unreadNotificationCount: vi.fn(), markNotificationRead: vi.fn(), markAllNotificationsRead: vi.fn(),
}))
vi.mock('../context/AppContext', () => ({ useApp: () => context.value }))
vi.mock('../services/api', () => api)

const notice = { id: 7, tipo: 'INVENTARIO_BAJO', titulo: 'Inventario bajo', mensaje: 'Tanque T-1 alcanzó su nivel crítico.', severidad: 'CRITICA', usuarioId: 1, referenciaTipo: 'TANQUE', referenciaId: '1', fechaCreacion: '2026-09-27T12:00:00Z', fechaLectura: null, leida: false, metadata: {} }
const defaults = () => ({
  toasts: [{ id: 1, title: 'Guardado', description: 'Cambios aplicados', tone: 'success' }], removeToast: vi.fn(),
  tanks: [{ stock: 2, criticalLevel: 5 }], tickets: [{ status: 'PROXIMO_A_VENCER' }], requests: [{ status: 'PENDIENTE' }, { status: 'PENDIENTE' }],
  session: { id: 1, token: 'qa', name: 'Ana QA', role: 'ADMINISTRADOR' }, logout: vi.fn(),
})

beforeEach(() => {
  context.value = defaults()
  api.listNotifications.mockResolvedValue({ pagina: 1, tamano: 30, total: 1, items: [notice] })
  api.unreadNotificationCount.mockResolvedValue({ cantidad: 1 })
  api.markNotificationRead.mockResolvedValue({ id: 7, leida: true, fechaLectura: '2026-09-27T12:01:00Z' })
  api.markAllNotificationsRead.mockResolvedValue({ actualizadas: 1 })
})
afterEach(() => { cleanup(); vi.clearAllMocks() })

describe('marco de navegación y avisos persistentes', () => {
  it('muestra el badge, lista avisos de API, marca leído y navega al módulo relacionado', async () => {
    const onNavigate = vi.fn()
    render(<AppShell page="dashboard" onNavigate={onNavigate}><p>Contenido</p></AppShell>)
    expect(screen.getByRole('button', { name: /Solicitudes/ }).textContent).toContain('2')
    expect(screen.getByText('Ana QA')).toBeTruthy()
    expect(await screen.findByRole('button', { name: 'Notificaciones, 1 sin leer' })).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Notificaciones, 1 sin leer' }))
    expect(await screen.findByText('Tanque T-1 alcanzó su nivel crítico.')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: /Inventario bajo/ }))
    await waitFor(() => expect(api.markNotificationRead).toHaveBeenCalledWith(7))
    expect(onNavigate).toHaveBeenCalledWith('inventario')
  })

  it('marca todas como leídas y conserva una sola fila al sincronizar IDs repetidos', async () => {
    api.listNotifications.mockResolvedValue({ pagina: 1, tamano: 30, total: 1, items: [notice] })
    render(<AppShell page="dashboard" onNavigate={vi.fn()}><p>Contenido</p></AppShell>)
    fireEvent.click(await screen.findByRole('button', { name: 'Notificaciones, 1 sin leer' }))
    fireEvent.click(await screen.findByRole('button', { name: 'Marcar todas como leídas' }))
    await waitFor(() => expect(api.markAllNotificationsRead).toHaveBeenCalledOnce())
    expect(screen.getByText('0 sin leer')).toBeTruthy()
    expect(screen.getAllByText('Inventario bajo')).toHaveLength(1)
  })

  it('muestra el error de lectura de API y mantiene abierto el panel', async () => {
    api.markNotificationRead.mockRejectedValueOnce(new Error('No se pudo guardar la lectura.'))
    const onNavigate = vi.fn()
    render(<AppShell page="dashboard" onNavigate={onNavigate}><p>Contenido</p></AppShell>)
    fireEvent.click(await screen.findByRole('button', { name: 'Notificaciones, 1 sin leer' }))
    fireEvent.click(await screen.findByRole('button', { name: /Inventario bajo/ }))
    expect((await screen.findByRole('alert')).textContent).toContain('No se pudo guardar la lectura.')
    expect(screen.getByRole('region', { name: 'Notificaciones' })).toBeTruthy()
    expect(onNavigate).not.toHaveBeenCalled()
  })

  it('permite buscar tickets, salir y descartar un toast', () => {
    const removeToast = vi.fn(), logout = vi.fn()
    context.value = { ...defaults(), toasts: [{ id: 2, title: 'Alerta', description: 'Prueba', tone: 'error' }], removeToast, logout, session: null }
    const onNavigate = vi.fn()
    render(<AppShell page="dashboard" onNavigate={onNavigate}><p>Contenido</p></AppShell>)
    fireEvent.click(screen.getByRole('button', { name: /Buscar tickets/ }))
    expect(onNavigate).toHaveBeenCalledWith('tickets')
    fireEvent.click(screen.getByTitle('Cerrar sesión'))
    expect(logout).toHaveBeenCalledOnce()
    fireEvent.click(screen.getByRole('button', { name: /Alerta/ }))
    expect(removeToast).toHaveBeenCalledWith(2)
  })
})

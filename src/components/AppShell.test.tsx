/** @vitest-environment jsdom */
import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { AppShell } from './AppShell'

const context = vi.hoisted(() => ({ value: null as unknown }))
vi.mock('../context/AppContext', () => ({ useApp: () => context.value }))

afterEach(cleanup)

describe('marco de navegación y avisos', () => {
  it('calcula avisos de inventario, tickets y solicitudes y navega a su origen', () => {
    context.value = {
      toasts: [{ id: 1, title: 'Guardado', description: 'Cambios aplicados', tone: 'success' }],
      removeToast: vi.fn(), tanks: [{ stock: 2, criticalLevel: 5 }],
      tickets: [{ status: 'PROXIMO_A_VENCER' }, { status: 'VENCIDO' }],
      requests: [{ status: 'PENDIENTE' }, { status: 'PENDIENTE' }],
      session: { name: 'Ana QA', role: 'ADMINISTRADOR' }, logout: vi.fn(),
    }
    const onNavigate = vi.fn()
    const { container } = render(<AppShell page="dashboard" onNavigate={onNavigate}><p>Contenido</p></AppShell>)
    expect(screen.getByRole('button', { name: /Solicitudes/ }).textContent).toContain('2')
    expect(screen.getByText('Ana QA')).toBeTruthy()
    expect(screen.getByText('Guardado')).toBeTruthy()

    fireEvent.click(container.querySelector('.notification-button')!)
    expect(screen.getByText('3 nuevas')).toBeTruthy()
    expect(screen.getByText('1 tanque requiere atención')).toBeTruthy()
    expect(screen.getByText('2 próximos a vencer o vencidos')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: /Inventario bajo/ }))
    expect(onNavigate).toHaveBeenCalledWith('inventario')
    fireEvent.click(container.querySelector('.notification-button')!)
    fireEvent.click(screen.getByRole('button', { name: /Tickets por revisar/ }))
    expect(onNavigate).toHaveBeenCalledWith('tickets')
  })

  it('permite buscar tickets, salir y descartar un aviso', () => {
    const removeToast = vi.fn()
    const logout = vi.fn()
    context.value = { toasts: [{ id: 2, title: 'Alerta', description: 'Prueba', tone: 'error' }], removeToast, tanks: [], tickets: [], requests: [], session: null, logout }
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

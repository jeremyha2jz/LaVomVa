/** @vitest-environment jsdom */
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { Tickets } from './Tickets'

const context = vi.hoisted(() => ({ value: null as unknown }))
const apiMocks = vi.hoisted(() => ({ ticketQr: vi.fn(), sendTicket: vi.fn(), retryTicketDelivery: vi.fn(), ticketDeliveryHistory: vi.fn(), reconcileTicketDelivery: vi.fn() }))
vi.mock('../context/AppContext', () => ({ useApp: () => context.value }))
vi.mock('../services/api', () => ({ ticketQr: apiMocks.ticketQr, sendTicket: apiMocks.sendTicket, retryTicketDelivery: apiMocks.retryTicketDelivery, ticketDeliveryHistory: apiMocks.ticketDeliveryHistory, reconcileTicketDelivery: apiMocks.reconcileTicketDelivery }))

afterEach(cleanup)
beforeEach(() => {
  apiMocks.ticketQr.mockReset().mockRejectedValue(new Error('QR no disponible en este test'))
  apiMocks.sendTicket.mockReset().mockResolvedValue({ estadoTicket: 'ENVIADO', duplicadoIdempotente: false, envios: [{ canal: 'CORREO', estado: 'ENVIADO', error: null }] })
  apiMocks.retryTicketDelivery.mockReset().mockResolvedValue({ estadoTicket: 'ENVIADO', duplicadoIdempotente: false, envios: [{ canal: 'CORREO', estado: 'ENVIADO', error: null }] })
  apiMocks.ticketDeliveryHistory.mockReset().mockResolvedValue([])
  apiMocks.reconcileTicketDelivery.mockReset().mockResolvedValue({ estadoTicket: 'ENVIADO', duplicadoIdempotente: false, envios: [] })
})

const ticket = { id: '5f9ab990-9bf8-4c88-9e40-bbf7d81b95dd', sequence: 'COM-2026-000001', employee: 'Eva QA', vehicle: 'A-003', department: 'Operaciones', fuelType: 'DIESEL', gallons: 8, createdAt: '2026-09-27T12:00:00', expiresAt: '2026-10-01T12:00:00', status: 'CREADO' as const }

describe('anulación desde la pantalla de tickets', () => {
  it('pide confirmación y motivo, anula y refresca el ticket', async () => {
    const cancelTicket = vi.fn().mockResolvedValue(undefined)
    context.value = { tickets: [ticket], notify: vi.fn(), session: { role: 'ADMINISTRADOR' }, cancelTicket }
    render(<Tickets />)

    fireEvent.click(screen.getByRole('row', { name: /COM-2026-000001/ }))
    fireEvent.click(await screen.findByRole('button', { name: 'Anular ticket' }))
    expect(screen.getByText('¿Confirmas la anulación?')).toBeTruthy()
    fireEvent.change(screen.getByLabelText('Motivo'), { target: { value: 'Solicitud duplicada' } })
    fireEvent.submit(screen.getByRole('button', { name: 'Confirmar anulación' }).closest('form')!)

    await waitFor(() => expect(cancelTicket).toHaveBeenCalledWith(ticket.id, 'Solicitud duplicada'))
    expect((await screen.findAllByText('ANULADO')).length).toBeGreaterThan(1)
    expect(screen.queryByRole('button', { name: 'Anular ticket' })).toBeNull()
  })

  it('oculta la acción para el despachador y para un ticket consumido', () => {
    context.value = { tickets: [ticket], notify: vi.fn(), session: { role: 'DESPACHADOR' }, cancelTicket: vi.fn(), refresh: vi.fn() }
    render(<Tickets />)
    fireEvent.click(screen.getByRole('row', { name: /COM-2026-000001/ }))
    expect(screen.queryByRole('button', { name: 'Anular ticket' })).toBeNull()

    context.value = { tickets: [{ ...ticket, status: 'CONSUMIDO' }], notify: vi.fn(), session: { role: 'SUPERVISOR' }, cancelTicket: vi.fn(), refresh: vi.fn() }
    cleanup()
    render(<Tickets />)
    fireEvent.click(screen.getByRole('row', { name: /COM-2026-000001/ }))
    expect(screen.queryByRole('button', { name: 'Anular ticket' })).toBeNull()
  })

  it('envía por canales elegidos y refleja el estado confirmado por la API', async () => {
    const refresh = vi.fn().mockResolvedValue(undefined)
    context.value = { tickets: [ticket], notify: vi.fn(), session: { role: 'SUPERVISOR' }, cancelTicket: vi.fn(), refresh }
    apiMocks.ticketDeliveryHistory.mockResolvedValue([{ id: 1, ticketId: ticket.id, canal: 'CORREO', destino: 'l***@example.test', estadoEnvio: 'FALLIDO', detalleError: 'Timeout', fechaEnvio: null, solicitadoEn: '2026-09-27T10:00:00', intento: 1, proveedor: 'fake', resultado: null, loteId: 'batch' }])
    render(<Tickets />)
    fireEvent.click(screen.getByRole('row', { name: /COM-2026-000001/ }))
    expect(await screen.findByText('Historial de envíos')).toBeTruthy()
    fireEvent.change(screen.getByLabelText('Canal de envío'), { target: { value: 'EMAIL' } })
    fireEvent.click(screen.getByRole('button', { name: 'Enviar ticket' }))
    await waitFor(() => expect(apiMocks.sendTicket).toHaveBeenCalledWith(ticket.id, 'EMAIL'))
    expect((await screen.findAllByText('ENVIADO')).length).toBeGreaterThan(0)
    expect(refresh).toHaveBeenCalledOnce()
  })

  it('permite reintentar solo el canal que falló y muestra errores del API', async () => {
    const notify = vi.fn()
    context.value = { tickets: [ticket], notify, session: { role: 'ADMINISTRADOR' }, cancelTicket: vi.fn(), refresh: vi.fn().mockResolvedValue(undefined) }
    apiMocks.ticketDeliveryHistory.mockResolvedValue([{ id: 2, ticketId: ticket.id, canal: 'SMS', destino: '*******0100', estadoEnvio: 'FALLIDO', detalleError: 'Gateway timeout', fechaEnvio: null, solicitadoEn: '2026-09-27T10:00:00', intento: 1, proveedor: 'fake', resultado: null, loteId: 'batch' }])
    render(<Tickets />)
    fireEvent.click(screen.getByRole('row', { name: /COM-2026-000001/ }))
    fireEvent.click(await screen.findByRole('button', { name: 'Reintentar SMS' }))
    await waitFor(() => expect(apiMocks.retryTicketDelivery).toHaveBeenCalledWith(ticket.id, 'SMS'))
  })

  it('exige confirmación antes de conciliar un resultado incierto y actualiza el historial', async () => {
    const confirm = vi.spyOn(window, 'confirm').mockReturnValue(true)
    context.value = { tickets: [ticket], notify: vi.fn(), session: { role: 'SUPERVISOR' }, cancelTicket: vi.fn(), refresh: vi.fn().mockResolvedValue(undefined) }
    apiMocks.ticketDeliveryHistory.mockResolvedValue([{ id: 3, ticketId: ticket.id, canal: 'CORREO', destino: 'u***@example.test', estadoEnvio: 'PENDIENTE', detalleError: 'Timeout', fechaEnvio: null, solicitadoEn: '2026-09-27T10:00:00', intento: 1, proveedor: 'smtp', resultado: 'RESULTADO_INCIERTO', loteId: 'batch' }])
    render(<Tickets />)
    fireEvent.click(screen.getByRole('row', { name: /COM-2026-000001/ }))
    fireEvent.click(await screen.findByRole('button', { name: 'Confirmar entregado' }))
    await waitFor(() => expect(apiMocks.reconcileTicketDelivery).toHaveBeenCalledWith(ticket.id, 3, 'ENVIADO'))
    expect(confirm).toHaveBeenCalled()
  })
})

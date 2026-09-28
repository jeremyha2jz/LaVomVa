/** @vitest-environment jsdom */
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { Closings } from './Closings'

const context = vi.hoisted(() => ({ value: null as unknown }))
const service = vi.hoisted(() => ({
  closeSummary: vi.fn(), createDailyClose: vi.fn(), dailyCloses: vi.fn(), downloadCierrePdf: vi.fn(),
}))
vi.mock('../context/AppContext', () => ({ useApp: () => context.value }))
vi.mock('../services/api', () => ({ api: service, downloadCierrePdf: service.downloadCierrePdf }))

afterEach(cleanup)

const summary = {
  estacionId: 4, estacion: 'Estación Central', fecha: '2026-09-27', inventarioInicialGalones: 120,
  volumenRecibidoGalones: 0, volumenDespachadoGalones: 20, otrasSalidasGalones: 0,
  mermasGalones: 0, ajustesGalones: 0, inventarioTeoricoFinalGalones: 100, cantidadDespachos: 2,
  tanques: [{ tanqueId: 8, codigo: 'T-1', nombre: 'Principal', capacidadGalones: 150, inventarioInicialGalones: 120, entradasGalones: 0, despachadoGalones: 20, otrasSalidasGalones: 0, mermasGalones: 0, ajustesGalones: 0, inventarioTeoricoFinalGalones: 100 }],
}

beforeEach(() => {
  service.closeSummary.mockReset().mockResolvedValue({ resumen: summary, cerrado: false, cierre: null })
  service.createDailyClose.mockReset().mockResolvedValue({ id: 12 })
  service.dailyCloses.mockReset().mockResolvedValue([])
  service.downloadCierrePdf.mockReset()
  context.value = { catalogs: { stations: [{ id: 4, name: 'Estación Central' }] }, session: { role: 'DESPACHADOR' }, notify: vi.fn() }
})

describe('cierre diario en web', () => {
  it('muestra el cálculo del API, confirma, envía el conteo físico y refresca el historial', async () => {
    service.closeSummary.mockResolvedValueOnce({ resumen: summary, cerrado: false, cierre: null }).mockResolvedValue({
      resumen: summary, cerrado: true, cierre: { id: 12, detalleTanques: [{ tanqueId: 8, inventarioFisicoGalones: 102 }] },
    })
    render(<Closings />)
    expect(await screen.findByText('Resumen del día')).toBeTruthy()
    expect(screen.getAllByText(/^20[,.]00 gal$/)).toHaveLength(2)

    fireEvent.change(screen.getByLabelText('Inventario físico T-1'), { target: { value: '102' } })
    expect(screen.getByText(/2[,.]00.*sobrante/)).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Confirmar cierre definitivo' }))
    expect(screen.getByRole('dialog').textContent).toContain('El acta no podrá editarse después de guardar.')
    fireEvent.click(screen.getByRole('button', { name: 'Cerrar día' }))

    await waitFor(() => expect(service.createDailyClose).toHaveBeenCalledOnce())
    expect(service.createDailyClose).toHaveBeenCalledWith(expect.objectContaining({
      estacionId: 4,
      fecha: '2026-09-27',
      inventariosFisicos: [{ tanqueId: 8, inventarioFisicoGalones: 102 }],
    }))
    expect(await screen.findByText('CERRADO')).toBeTruthy()
    expect(screen.getByLabelText('Inventario físico T-1').hasAttribute('disabled')).toBe(true)
  })

  it('muestra el error real de la API al fallar el cierre', async () => {
    service.createDailyClose.mockRejectedValue(new Error('El día operacional ya está cerrado'))
    render(<Closings />)
    fireEvent.click(await screen.findByRole('button', { name: 'Confirmar cierre definitivo' }))
    fireEvent.click(screen.getByRole('button', { name: 'Cerrar día' }))
    expect((await screen.findByRole('alert')).textContent).toContain('El día operacional ya está cerrado')
  })
})

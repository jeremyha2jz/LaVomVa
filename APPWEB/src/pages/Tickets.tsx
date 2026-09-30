import { Copy, Eye, Filter, MoreHorizontal } from 'lucide-react'
import { useEffect, useMemo, useState, type FormEvent } from 'react'
import { useApp } from '../context/AppContext'
import { TicketDeliveryPanel } from './TicketDeliveryPanel'
import type { Ticket, TicketStatus } from '../types'
import { EmptyState, formatDate, Modal, PageHeader, SearchBox, StatusBadge } from '../components/ui'
import { reconcileTicketDelivery, retryTicketDelivery, sendTicket, ticketDeliveryHistory, ticketQr, type DeliveryChannel, type TicketDeliveryRow } from '../services/api'

export function Tickets() {
  const { tickets, notify, session, cancelTicket, refresh } = useApp()
  const canSeeQr = ['ADMINISTRADOR', 'SUPERVISOR'].includes(session?.role || '')
  const [search, setSearch] = useState('')
  const [status, setStatus] = useState<'TODOS' | TicketStatus>('TODOS')
  const [selected, setSelected] = useState<Ticket | null>(null)
  const [qrUrl, setQrUrl] = useState('')
  const [confirmCancel, setConfirmCancel] = useState(false)
  const [cancelReason, setCancelReason] = useState('')
  const [cancelling, setCancelling] = useState(false)
  const [deliveryChannel, setDeliveryChannel] = useState<DeliveryChannel>('AMBOS')
  const [deliveryRows, setDeliveryRows] = useState<TicketDeliveryRow[]>([])
  const [sending, setSending] = useState(false)
  const [deliveryError, setDeliveryError] = useState('')
  useEffect(() => {
    if (!selected || !canSeeQr) return
    let cancelled = false
    let url = ''
    void ticketQr(selected.id).then((blob) => {
      url = URL.createObjectURL(blob)
      if (!cancelled) setQrUrl(url)
      else URL.revokeObjectURL(url)
    }).catch((cause) => { if (!cancelled) notify('QR no disponible', cause instanceof Error ? cause.message : 'Ocurrió un error inesperado.', 'error') })
    return () => { cancelled = true; setQrUrl(''); if (url) URL.revokeObjectURL(url) }
  }, [selected?.id, canSeeQr])

  useEffect(() => {
    if (!selected || !canSeeQr) { setDeliveryRows([]); return }
    let live = true
    void ticketDeliveryHistory(selected.id).then((rows) => { if (live) setDeliveryRows(rows) }).catch((cause) => {
      if (live) setDeliveryError(cause instanceof Error ? cause.message : 'No se pudo cargar el historial de envíos.')
    })
    return () => { live = false }
  }, [selected?.id, canSeeQr])

  const filtered = useMemo(() => tickets.filter((ticket) => (status === 'TODOS' || ticket.status === status) && `${ticket.sequence} ${ticket.employee} ${ticket.vehicle}`.toLowerCase().includes(search.toLowerCase())), [tickets, search, status])
  const canCancelSelected = selected !== null && canSeeQr && ['CREADO', 'ENVIADO', 'PENDIENTE', 'PROXIMO_A_VENCER', 'VENCIDO'].includes(selected.status)

  async function submitCancellation(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!selected || !cancelReason.trim()) {
      notify('Motivo requerido', 'Escribe el motivo de anulación.', 'error')
      return
    }
    setCancelling(true)
    try {
      await cancelTicket(selected.id, cancelReason.trim())
      setSelected({ ...selected, status: 'ANULADO' })
      setConfirmCancel(false)
      setCancelReason('')
    } catch (cause) {
      notify('No se pudo anular', cause instanceof Error ? cause.message : 'Ocurrió un error inesperado.', 'error')
    } finally {
      setCancelling(false)
    }
  }

  async function submitDelivery(retryChannel?: 'CORREO' | 'SMS') {
    if (!selected) return
    setSending(true)
    setDeliveryError('')
    try {
      const outcome = retryChannel
        ? await retryTicketDelivery(selected.id, retryChannel)
        : await sendTicket(selected.id, deliveryChannel)
      setSelected({ ...selected, status: outcome.estadoTicket as TicketStatus })
      setDeliveryRows(await ticketDeliveryHistory(selected.id))
      void refresh().catch((cause) => setDeliveryError(`El envío respondió, pero la lista no se pudo actualizar: ${cause instanceof Error ? cause.message : 'error de actualización'}`))
      const failed = outcome.envios.filter((row) => row.estado === 'FALLIDO')
      notify(failed.length ? 'Envío registrado con fallos' : 'Ticket enviado', failed.length ? failed.map((row) => `${row.canal}: ${row.error}`).join(' · ') : 'El proveedor confirmó la entrega.', failed.length ? 'warning' : 'success')
    } catch (cause) {
      const message = cause instanceof Error ? cause.message : 'No se pudo enviar el ticket.'
      setDeliveryError(message)
      notify('No se pudo enviar el ticket', message, 'error')
    } finally { setSending(false) }
  }

  async function reconcileDelivery(row: TicketDeliveryRow, state: 'ENVIADO' | 'FALLIDO') {
    if (!selected || !window.confirm(state === 'ENVIADO'
      ? '¿Confirmaste en el proveedor que este envío llegó? Esta decisión quedará auditada.'
      : '¿Confirmaste en el proveedor que este envío no llegó? Esta decisión quedará auditada.')) return
    setSending(true)
    setDeliveryError('')
    try {
      const outcome = await reconcileTicketDelivery(selected.id, row.id, state)
      setSelected({ ...selected, status: outcome.estadoTicket as TicketStatus })
      setDeliveryRows(await ticketDeliveryHistory(selected.id))
      void refresh().catch(() => setDeliveryError('El envío se confirmó, pero la lista no se pudo actualizar.'))
      notify('Resultado conciliado', state === 'ENVIADO' ? 'El envío quedó confirmado como entregado.' : 'El envío quedó confirmado como fallido.', state === 'ENVIADO' ? 'success' : 'warning')
    } catch (cause) {
      const message = cause instanceof Error ? cause.message : 'No se pudo conciliar el envío.'
      setDeliveryError(message)
      notify('No se pudo conciliar el envío', message, 'error')
    } finally { setSending(false) }
  }

  return <div className="page"><PageHeader eyebrow="Operación" title="Tickets digitales" description="Consulta el estado, vigencia y trazabilidad de cada ticket emitido." />
    <section className="panel table-panel"><div className="table-toolbar"><SearchBox value={search} onChange={setSearch} placeholder="Buscar por ticket, empleado o placa…" /><label className="select-wrap"><Filter size={16} /><select value={status} onChange={(event) => setStatus(event.target.value as typeof status)}><option value="TODOS">Todos los estados</option><option>CREADO</option><option>ENVIADO</option><option>PENDIENTE</option><option>PROXIMO_A_VENCER</option><option>VENCIDO</option><option>CONSUMIDO</option><option>ANULADO</option></select></label></div><div className="table-scroll"><table><thead><tr><th>Ticket</th><th>Asignación</th><th>Combustible</th><th>Vigencia</th><th>Estado</th><th /></tr></thead><tbody>{filtered.map((ticket) => <tr key={ticket.id} onClick={() => { setSelected(ticket); setConfirmCancel(false); setCancelReason('') }}><td><strong>{ticket.sequence}</strong><small>{ticket.id.slice(0, 8)}…</small></td><td><strong>{ticket.employee}</strong><small>{ticket.vehicle} · {ticket.department}</small></td><td><strong>{ticket.gallons} gal</strong><small>{ticket.fuelType}</small></td><td>{formatDate(ticket.expiresAt, true)}<small>Creado {formatDate(ticket.createdAt)}</small></td><td><StatusBadge value={ticket.status} /></td><td><button className="icon-button"><MoreHorizontal size={18} /></button></td></tr>)}</tbody></table>{filtered.length === 0 && <EmptyState title="No encontramos tickets" description="Prueba con otros términos o filtros." />}</div></section>
    {selected && <Modal title={selected.sequence} subtitle="Ticket emitido por el sistema" onClose={() => { setSelected(null); setConfirmCancel(false); setCancelReason(''); setDeliveryError('') }} size="lg"><div className="ticket-layout"><div className="ticket-preview"><div className="ticket-preview-head"><span>LaVomVa</span><StatusBadge value={selected.status} /></div><div className="fake-qr" aria-label="Código QR del ticket">{canSeeQr ? qrUrl ? <img src={qrUrl} alt={`QR del ticket ${selected.sequence}`} width={140} height={140} /> : <span>Cargando QR…</span> : <span>QR reservado al personal autorizado</span>}</div><strong>{selected.sequence}</strong>{!canSeeQr && <small>Consulta de estado</small>}<div className="ticket-dashes" /><dl><div><dt>Empleado</dt><dd>{selected.employee}</dd></div><div><dt>Vehículo</dt><dd>{selected.vehicle}</dd></div><div><dt>Combustible</dt><dd>{selected.gallons} gal · {selected.fuelType}</dd></div><div><dt>Válido hasta</dt><dd>{formatDate(selected.expiresAt, true)}</dd></div></dl></div><div className="ticket-actions"><h3>Datos del ticket</h3><p>El despacho requiere validar este QR.</p><button className="secondary-button" onClick={() => { navigator.clipboard?.writeText(selected.sequence); notify('Número copiado', selected.sequence) }}><Copy size={17} /> Copiar número</button><div className="security-note"><Eye size={17} /><span><strong>Importante</strong><small>No compartas el QR fuera del proceso de despacho.</small></span></div>
      {canSeeQr && !['ANULADO', 'CONSUMIDO', 'VENCIDO'].includes(selected.status) && <TicketDeliveryPanel deliveryChannel={deliveryChannel} onChannelChange={setDeliveryChannel} sending={sending} deliveryError={deliveryError} deliveryRows={deliveryRows} onSubmit={(channel) => void submitDelivery(channel)} onReconcile={(row, state) => void reconcileDelivery(row, state)} />}
      {canCancelSelected && !confirmCancel && <button className="secondary-button" onClick={() => setConfirmCancel(true)}>Anular ticket</button>}{canCancelSelected && confirmCancel && <form className="ticket-cancel-form" onSubmit={(event) => void submitCancellation(event)}><strong>¿Confirmas la anulación?</strong><label htmlFor="cancel-reason">Motivo</label><textarea id="cancel-reason" value={cancelReason} onChange={(event) => setCancelReason(event.target.value)} maxLength={500} required rows={3} placeholder="Indica por qué se anula este ticket" /><small>{cancelReason.length}/500</small><div><button className="primary-button" type="submit" disabled={cancelling}>{cancelling ? 'Anulando…' : 'Confirmar anulación'}</button><button className="secondary-button" type="button" onClick={() => { setConfirmCancel(false); setCancelReason('') }} disabled={cancelling}>Cancelar</button></div></form>}</div></div></Modal>}
  </div>
}

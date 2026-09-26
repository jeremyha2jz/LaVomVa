import { Copy, Eye, Filter, MoreHorizontal } from 'lucide-react'
import { useEffect, useMemo, useState } from 'react'
import { useApp } from '../context/AppContext'
import type { Ticket, TicketStatus } from '../types'
import { EmptyState, formatDate, Modal, PageHeader, SearchBox, StatusBadge } from '../components/ui'
import { ticketQr } from '../services/api'

export function Tickets() {
  const { tickets, notify, session } = useApp()
  const canSeeQr = ['ADMINISTRADOR', 'SUPERVISOR'].includes(session?.role || '')
  const [search, setSearch] = useState('')
  const [status, setStatus] = useState<'TODOS' | TicketStatus>('TODOS')
  const [selected, setSelected] = useState<Ticket | null>(null)
  const [qrUrl, setQrUrl] = useState('')
  useEffect(() => {
    if (!selected || !canSeeQr) return
    let cancelled = false
    let url = ''
    void ticketQr(selected.id).then((blob) => {
      url = URL.createObjectURL(blob)
      if (!cancelled) setQrUrl(url)
      else URL.revokeObjectURL(url)
    }).catch((cause) => { if (!cancelled) notify('QR no disponible', cause instanceof Error ? cause.message : 'Error de la API.', 'error') })
    return () => { cancelled = true; setQrUrl(''); if (url) URL.revokeObjectURL(url) }
  }, [selected?.id, canSeeQr])

  const filtered = useMemo(() => tickets.filter((ticket) => (status === 'TODOS' || ticket.status === status) && `${ticket.sequence} ${ticket.employee} ${ticket.vehicle}`.toLowerCase().includes(search.toLowerCase())), [tickets, search, status])
  return <div className="page"><PageHeader eyebrow="Operación" title="Tickets digitales" description="Consulta el estado, vigencia y trazabilidad de cada ticket emitido." />
    <section className="panel table-panel"><div className="table-toolbar"><SearchBox value={search} onChange={setSearch} placeholder="Buscar por ticket, empleado o placa…" /><label className="select-wrap"><Filter size={16} /><select value={status} onChange={(event) => setStatus(event.target.value as typeof status)}><option value="TODOS">Todos los estados</option><option>CREADO</option><option>ENVIADO</option><option>PROXIMO_A_VENCER</option><option>VENCIDO</option><option>CONSUMIDO</option><option>ANULADO</option></select></label></div><div className="table-scroll"><table><thead><tr><th>Ticket</th><th>Asignación</th><th>Combustible</th><th>Vigencia</th><th>Estado</th><th /></tr></thead><tbody>{filtered.map((ticket) => <tr key={ticket.id} onClick={() => setSelected(ticket)}><td><strong>{ticket.sequence}</strong><small>{ticket.id.slice(0, 8)}…</small></td><td><strong>{ticket.employee}</strong><small>{ticket.vehicle} · {ticket.department}</small></td><td><strong>{ticket.gallons} gal</strong><small>{ticket.fuelType}</small></td><td>{formatDate(ticket.expiresAt, true)}<small>Creado {formatDate(ticket.createdAt)}</small></td><td><StatusBadge value={ticket.status} /></td><td><button className="icon-button"><MoreHorizontal size={18} /></button></td></tr>)}</tbody></table>{filtered.length === 0 && <EmptyState title="No encontramos tickets" description="Prueba con otros términos o filtros." />}</div></section>
    {selected && <Modal title={selected.sequence} subtitle="Ticket emitido por el sistema" onClose={() => setSelected(null)} size="lg"><div className="ticket-layout"><div className="ticket-preview"><div className="ticket-preview-head"><span>LaVomVa</span><StatusBadge value={selected.status} /></div><div className="fake-qr" aria-label="Código QR del ticket">{canSeeQr ? qrUrl ? <img src={qrUrl} alt={`QR del ticket ${selected.sequence}`} width={140} height={140} /> : <span>Cargando QR…</span> : <span>QR reservado al personal autorizado</span>}</div><strong>{selected.sequence}</strong><small>{canSeeQr ? 'QR generado por el backend' : 'Consulta de estado'}</small><div className="ticket-dashes" /><dl><div><dt>Empleado</dt><dd>{selected.employee}</dd></div><div><dt>Vehículo</dt><dd>{selected.vehicle}</dd></div><div><dt>Combustible</dt><dd>{selected.gallons} gal · {selected.fuelType}</dd></div><div><dt>Válido hasta</dt><dd>{formatDate(selected.expiresAt, true)}</dd></div></dl></div><div className="ticket-actions"><h3>Datos del ticket</h3><p>El despacho requiere validar este QR.</p><button className="secondary-button" onClick={() => { navigator.clipboard?.writeText(selected.sequence); notify('Número copiado', selected.sequence) }}><Copy size={17} /> Copiar número</button><div className="security-note"><Eye size={17} /><span><strong>Importante</strong><small>No compartas el QR fuera del proceso de despacho.</small></span></div></div></div></Modal>}
  </div>
}

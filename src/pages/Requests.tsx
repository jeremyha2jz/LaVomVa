import { Check, ChevronRight, Filter, Plus, X } from 'lucide-react'
import { useMemo, useState, type FormEvent } from 'react'
import { useApp, type NewRequest } from '../context/AppContext'
import { EmptyState, formatDate, Modal, PageHeader, SearchBox, StatusBadge } from '../components/ui'
import type { FuelRequest, RequestStatus } from '../types'
import { ScheduleManager } from '../components/ScheduleManager'

export function Requests() {
  const { requests, catalogs, addRequest, resolveRequest, notify, session } = useApp()
  const canRequest = ['ADMINISTRADOR', 'SUPERVISOR', 'SOLICITANTE'].includes(session?.role || '')
  const canApprove = ['ADMINISTRADOR', 'SUPERVISOR'].includes(session?.role || '')
  const canManageSchedules = canApprove
  const [search, setSearch] = useState('')
  const [status, setStatus] = useState<'TODAS' | RequestStatus>('TODAS')
  const [newOpen, setNewOpen] = useState(false)
  const [selected, setSelected] = useState<FuelRequest | null>(null)
  const [authorized, setAuthorized] = useState(0)
  const [busy, setBusy] = useState(false)
  const filtered = useMemo(() => requests.filter((request) => (status === 'TODAS' || request.status === status) && `${request.employee} ${request.vehicle} ${request.department} ${request.id}`.toLowerCase().includes(search.toLowerCase())), [requests, status, search])

  function openRequest(request: FuelRequest) { setSelected(request); setAuthorized(request.requestedGallons) }

  async function decide(decision: 'approve' | 'reject') {
    if (!selected) return
    setBusy(true)
    try { await resolveRequest(selected.id, decision, authorized); setSelected(null) }
    catch (cause) { notify('No se pudo completar', cause instanceof Error ? cause.message : 'Error de la API.', 'error') }
    finally { setBusy(false) }
  }

  return <div className="page">
    <PageHeader eyebrow="Operación" title="Solicitudes de combustible" description="Revisa, aprueba y da seguimiento a las solicitudes de combustible." actions={canRequest ? <button className="primary-button" onClick={() => setNewOpen(true)}><Plus size={17} /> Nueva solicitud</button> : undefined} />
    {canManageSchedules && <ScheduleManager />}
    <section className="panel table-panel">
      <div className="table-toolbar"><SearchBox value={search} onChange={setSearch} placeholder="Buscar por empleado, placa o número…" /><label className="select-wrap"><Filter size={16} /><select value={status} onChange={(event) => setStatus(event.target.value as typeof status)}><option value="TODAS">Todos los estados</option><option>PENDIENTE</option><option>APROBADA</option><option>RECHAZADA</option><option>CANCELADA</option></select></label></div>
      <div className="table-scroll"><table><thead><tr><th>Solicitud</th><th>Empleado y vehículo</th><th>Departamento</th><th>Cantidad</th><th>Fecha</th><th>Estado</th><th /></tr></thead><tbody>{filtered.map((request) => <tr key={request.id} onClick={() => openRequest(request)}><td><strong>#{request.id}</strong><small>{request.kind}</small></td><td><strong>{request.employee}</strong><small>{request.vehicle}</small></td><td>{request.department}</td><td><strong>{request.requestedGallons} gal</strong><small>{request.fuelType}</small></td><td>{formatDate(request.requestedAt)}<small>Vence {formatDate(request.expiresAt)}</small></td><td><StatusBadge value={request.status} /></td><td><button className="icon-button" aria-label={`Ver solicitud ${request.id}`}><ChevronRight size={18} /></button></td></tr>)}</tbody></table>{filtered.length === 0 && <EmptyState title="No hay solicitudes" description="Cambia los filtros o crea una nueva solicitud." />}</div>
    </section>
    {newOpen && <NewRequestModal onClose={() => setNewOpen(false)} onSubmit={async (item) => { await addRequest(item); setNewOpen(false) }} />}
    {selected && <Modal title={`Solicitud #${selected.id}`} subtitle={`Creada ${formatDate(selected.requestedAt, true)}`} onClose={() => setSelected(null)}>
      <div className="detail-hero"><div className="initials large">{selected.employee.split(' ').map((part) => part[0]).slice(0, 2).join('')}</div><div><h3>{selected.employee}</h3><p>{selected.employeeCode} · {selected.department}</p></div><StatusBadge value={selected.status} /></div>
      <dl className="details-grid"><div><dt>Vehículo</dt><dd>{selected.vehicle}</dd></div><div><dt>Combustible</dt><dd>{selected.fuelType}</dd></div><div><dt>Cantidad solicitada</dt><dd>{selected.requestedGallons} gal</dd></div><div><dt>Vencimiento</dt><dd>{formatDate(selected.expiresAt, true)}</dd></div><div className="detail-wide"><dt>Motivo</dt><dd>{selected.reason}</dd></div></dl>
      {canApprove && selected.status === 'PENDIENTE' && <div className="approval-box"><label>Galones autorizados<input type="number" min="0.1" max={selected.requestedGallons} step="0.1" value={authorized} onChange={(event) => setAuthorized(Number(event.target.value))} /></label><div><button className="danger-button" disabled={busy} onClick={() => void decide('reject')}><X size={17} /> Rechazar</button><button className="primary-button" disabled={busy || authorized <= 0 || authorized > selected.requestedGallons} onClick={() => void decide('approve')}><Check size={17} /> Aprobar y emitir ticket</button></div></div>}
    </Modal>}
  </div>
}

function NewRequestModal({ onClose, onSubmit }: { onClose: () => void; onSubmit: (data: NewRequest) => Promise<void> }) {
  const { catalogs, notify } = useApp()
  const [busy, setBusy] = useState(false)
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const data = new FormData(event.currentTarget)
    const employee = catalogs.employees.find((item) => item.id === Number(data.get('employee')))
    const vehicle = catalogs.vehicles.find((item) => item.id === Number(data.get('vehicle')))
    const department = catalogs.departments.find((item) => item.id === Number(data.get('department')))
    const fuelType = catalogs.fuelTypes.find((item) => item.id === Number(data.get('fuelType')))
    if (!employee || !vehicle || !department || !fuelType) { notify('Datos incompletos', 'Selecciona valores válidos en todos los catálogos.', 'error'); return }
    setBusy(true)
    try {
      await onSubmit({ employeeId: employee.id, employee: employee.name, employeeCode: employee.code, vehicleId: vehicle.id, vehicle: `${vehicle.plate} · ${vehicle.brand} ${vehicle.model}`, departmentId: department.id, department: department.name, fuelTypeId: fuelType.id, fuelType: fuelType.name, requestedGallons: Number(data.get('gallons')), expiresAt: new Date(String(data.get('expiresAt'))).toISOString(), kind: 'MANUAL', reason: String(data.get('reason')) })
    } catch (cause) { notify('No se pudo registrar', cause instanceof Error ? cause.message : 'Error de la API.', 'error') }
    finally { setBusy(false) }
  }
  return <Modal title="Nueva solicitud" subtitle="Completa los datos requeridos para solicitar combustible." onClose={onClose} size="lg"><form className="form-grid" onSubmit={(event) => void submit(event)}>
    <label>Empleado<select name="employee" required defaultValue=""><option value="">Selecciona un empleado</option>{catalogs.employees.map((item) => <option key={item.id} value={item.id}>{item.code} · {item.name}</option>)}</select></label>
    <label>Vehículo<select name="vehicle" required defaultValue=""><option value="">Selecciona un vehículo</option>{catalogs.vehicles.map((item) => <option key={item.id} value={item.id}>{item.plate} · {item.brand} {item.model}</option>)}</select></label>
    <label>Departamento<select name="department" required defaultValue=""><option value="">Selecciona un departamento</option>{catalogs.departments.map((item) => <option key={item.id} value={item.id}>{item.name}</option>)}</select></label>
    <label>Tipo de combustible<select name="fuelType" required defaultValue=""><option value="">Selecciona un combustible</option>{catalogs.fuelTypes.map((item) => <option key={item.id} value={item.id}>{item.name}</option>)}</select></label>
    <label>Galones solicitados<input name="gallons" type="number" min="0.1" step="0.1" required /></label>
    <label>Fecha de vencimiento<input name="expiresAt" type="datetime-local" min={new Date().toISOString().slice(0, 16)} required /></label>
    <label className="form-wide">Motivo<textarea name="reason" rows={3} required placeholder="Describe el uso previsto del combustible" /></label>
    <div className="form-actions"><button type="button" className="secondary-button" onClick={onClose}>Cancelar</button><button className="primary-button" disabled={busy} type="submit">{busy ? 'Guardando…' : 'Registrar solicitud'}</button></div>
  </form></Modal>
}

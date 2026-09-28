import { useEffect, useState, type FormEvent } from 'react'
import { Clock3, History, Pencil, Play, Plus, Power, RefreshCw } from 'lucide-react'
import { useApp } from '../context/AppContext'
import { EmptyState, formatDate, Modal } from './ui'
import { createProgrammedRequest, listProgrammedRequests, programmedRequestHistory, setProgrammedRequestActive, updateProgrammedRequest, type ProgrammedExecution, type ProgrammedRequest, type ProgrammedRequestBody, type ScheduleFrequency, type ScheduleType } from '../services/api'

const frequencies: ScheduleFrequency[] = ['DIARIA', 'SEMANAL', 'MENSUAL']
const defaultStart = () => new Date(Date.now() + 60_000).toISOString()
function localInput(value?: string | null) {
  const date = value ? new Date(value) : new Date()
  return new Date(date.getTime() - date.getTimezoneOffset() * 60_000).toISOString().slice(0, 16)
}

export function ScheduleManager() {
  const { catalogs, session, notify } = useApp()
  const [items, setItems] = useState<ProgrammedRequest[]>([])
  const [loading, setLoading] = useState(true)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [editing, setEditing] = useState<ProgrammedRequest | null | false>(false)
  const [historyFor, setHistoryFor] = useState<ProgrammedRequest | null>(null)
  const [history, setHistory] = useState<ProgrammedExecution[]>([])
  const [historyError, setHistoryError] = useState('')
  const canManage = ['ADMINISTRADOR', 'SUPERVISOR'].includes(session?.role || '')

  async function load() {
    setLoading(true)
    setError('')
    try { setItems(await listProgrammedRequests()) }
    catch (cause) { setError(cause instanceof Error ? cause.message : 'No se pudieron cargar las programaciones.') }
    finally { setLoading(false) }
  }
  useEffect(() => { if (canManage) void load() }, [canManage])

  async function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const form = new FormData(event.currentTarget)
    const type = String(form.get('type')) as ScheduleType
    const start = String(form.get('start'))
    const end = String(form.get('end'))
    const body: ProgrammedRequestBody = {
      tipoSolicitud: type,
      empleadoId: Number(form.get('employee')),
      vehiculoId: Number(form.get('vehicle')),
      departamentoId: Number(form.get('department')),
      tipoCombustibleId: Number(form.get('fuel')),
      cantidadSolicitadaGalones: Number(form.get('gallons')),
      fechaInicial: new Date(start).toISOString(),
      fechaFinal: end ? new Date(end).toISOString() : null,
      frecuencia: type === 'RECURRENTE' ? String(form.get('frequency')) as ScheduleFrequency : null,
    }
    setBusy(true)
    setError('')
    try {
      if (editing && typeof editing === 'object') await updateProgrammedRequest(editing.id, body)
      else await createProgrammedRequest(body)
      setEditing(false)
      await load()
      notify(editing ? 'Programación actualizada' : 'Programación creada', 'Las solicitudes generadas quedarán pendientes de aprobación.')
    } catch (cause) {
      const message = cause instanceof Error ? cause.message : 'No se pudo guardar la programación.'
      setError(message)
      notify('No se pudo guardar la programación', message, 'error')
    } finally { setBusy(false) }
  }

  async function toggle(item: ProgrammedRequest) {
    setBusy(true)
    setError('')
    try { await setProgrammedRequestActive(item.id, !item.activa); await load() }
    catch (cause) {
      const message = cause instanceof Error ? cause.message : 'No se pudo cambiar el estado.'
      setError(message)
      notify('No se pudo cambiar la programación', message, 'error')
    } finally { setBusy(false) }
  }

  async function showHistory(item: ProgrammedRequest) {
    setHistoryFor(item)
    setHistory([])
    setHistoryError('')
    try { setHistory(await programmedRequestHistory(item.id)) }
    catch (cause) { setHistoryError(cause instanceof Error ? cause.message : 'No se pudo cargar el historial.') }
  }

  function label(values: { id: number; name?: string; code?: string; plate?: string }[], id: number, fallback: string) {
    const value = values.find((candidate) => candidate.id === id)
    return value?.name || value?.code || value?.plate || fallback
  }

  if (!canManage) return null
  return <section className="panel table-panel schedule-manager" aria-label="Programaciones automáticas">
    <div className="table-toolbar schedule-heading"><div><h2>Programaciones</h2><p>Las solicitudes se crean pendientes de aprobación.</p></div><div><button className="secondary-button" onClick={() => void load()} disabled={loading}><RefreshCw size={16} /> Actualizar</button><button className="primary-button" onClick={() => setEditing(null)}><Plus size={16} /> Nueva programación</button></div></div>
    {error && <p role="alert" className="schedule-error">{error}</p>}
    {loading ? <p className="muted">Cargando programaciones…</p> : items.length === 0 ? <EmptyState title="Sin programaciones" description="Crea una automática de una sola vez o una recurrente." /> : <div className="table-scroll"><table><thead><tr><th>Tipo y asignación</th><th>Cantidad</th><th>Frecuencia</th><th>Próxima ejecución</th><th>Última ejecución</th><th>Estado</th><th>Acciones</th></tr></thead><tbody>{items.map((item) => <tr key={item.id}>
      <td><strong>#{item.id} · {item.tipoSolicitud}</strong><small>{label(catalogs.employees, item.empleadoId, 'Empleado')} · {label(catalogs.vehicles, item.vehiculoId, 'Vehículo')}</small></td>
      <td>{item.cantidadSolicitadaGalones} gal<small>{label(catalogs.fuelTypes, item.tipoCombustibleId, 'Combustible')}</small></td>
      <td>{item.frecuencia || 'Una vez'}<small>Inicio {formatDate(item.fechaInicial, true)}</small></td>
      <td>{item.proximaEjecucion ? formatDate(item.proximaEjecucion, true) : 'Sin próxima ejecución'}</td>
      <td>{item.ultimaEjecucion ? formatDate(item.ultimaEjecucion, true) : 'Sin ejecuciones'}</td>
      <td><span className={`schedule-state ${item.activa ? 'is-active' : ''}`}>{item.activa ? 'Activa' : 'Inactiva'}</span></td>
      <td><div className="schedule-actions"><button className="icon-button" aria-label={`Editar programación ${item.id}`} onClick={() => setEditing(item)} disabled={busy}><Pencil size={16} /></button><button className="icon-button" aria-label={`Historial programación ${item.id}`} onClick={() => void showHistory(item)}><History size={16} /></button><button className="icon-button" aria-label={`${item.activa ? 'Desactivar' : 'Activar'} programación ${item.id}`} onClick={() => void toggle(item)} disabled={busy}><Power size={16} /></button></div></td>
    </tr>)}</tbody></table></div>}
    {editing !== false && <ScheduleForm key={editing && typeof editing === 'object' ? editing.id : 'new'} item={editing || null} catalogs={catalogs} busy={busy} onClose={() => setEditing(false)} onSubmit={save} />}
    {historyFor && <Modal title={`Historial · Programación #${historyFor.id}`} subtitle="Cada ejecución genera una sola solicitud; las fallidas requieren revisar el catálogo y reactivar." onClose={() => setHistoryFor(null)} size="lg">
      {historyError && <p role="alert">{historyError}</p>}{history.length === 0 && !historyError ? <EmptyState title="Sin ejecuciones" description="Todavía no hay intentos registrados." /> : <div className="schedule-history">{history.map((entry) => <article key={entry.id}><div><strong>{entry.estado} · solicitud {entry.solicitudGeneradaId ? `#${entry.solicitudGeneradaId}` : 'no generada'}</strong><small>Programada {formatDate(entry.fechaProgramada, true)} · procesada {formatDate(entry.ejecutadaEn, true)}</small>{entry.estadoSolicitud && <small>Estado de solicitud: {entry.estadoSolicitud}</small>}{entry.detalleError && <small className="schedule-error">{entry.detalleError}</small>}</div><Clock3 size={17} /></article>)}</div>}
    </Modal>}
  </section>
}

function ScheduleForm({ item, catalogs, busy, onClose, onSubmit }: {
  item: ProgrammedRequest | null
  catalogs: ReturnType<typeof useApp>['catalogs']
  busy: boolean
  onClose: () => void
  onSubmit: (event: FormEvent<HTMLFormElement>) => void
}) {
  const [type, setType] = useState<ScheduleType>(item?.tipoSolicitud || 'AUTOMATICA')
  return <Modal title={item ? `Editar programación #${item.id}` : 'Nueva programación'} subtitle="Las referencias activas se validan al guardar y en cada ejecución." onClose={onClose} size="lg"><form className="form-grid" onSubmit={onSubmit}>
    <label>Tipo<select name="type" value={type} onChange={(event) => setType(event.target.value as ScheduleType)}><option value="AUTOMATICA">Automática · una vez</option><option value="RECURRENTE">Recurrente</option></select></label>
    {type === 'RECURRENTE' && <label>Frecuencia<select name="frequency" defaultValue={item?.frecuencia || 'SEMANAL'}>{frequencies.map((frequency) => <option key={frequency}>{frequency}</option>)}</select></label>}
    <label>Empleado<select name="employee" defaultValue={item?.empleadoId || ''} required><option value="">Selecciona empleado</option>{catalogs.employees.filter((value) => value.active).map((value) => <option key={value.id} value={value.id}>{value.code} · {value.name}</option>)}</select></label>
    <label>Vehículo<select name="vehicle" defaultValue={item?.vehiculoId || ''} required><option value="">Selecciona vehículo</option>{catalogs.vehicles.filter((value) => value.active).map((value) => <option key={value.id} value={value.id}>{value.plate} · {value.brand} {value.model}</option>)}</select></label>
    <label>Departamento<select name="department" defaultValue={item?.departamentoId || ''} required><option value="">Selecciona departamento</option>{catalogs.departments.filter((value) => value.active).map((value) => <option key={value.id} value={value.id}>{value.name}</option>)}</select></label>
    <label>Combustible<select name="fuel" defaultValue={item?.tipoCombustibleId || ''} required><option value="">Selecciona combustible</option>{catalogs.fuelTypes.map((value) => <option key={value.id} value={value.id}>{value.name}</option>)}</select></label>
    <label>Galones<input name="gallons" type="number" min="0.01" max="99999999.99" step="0.01" defaultValue={item?.cantidadSolicitadaGalones ?? ''} required /></label>
    <label>Fecha y hora inicial (hora local; se guarda en UTC)<input name="start" type="datetime-local" defaultValue={localInput(item?.fechaInicial || defaultStart())} required /></label>
    <label>Fecha final (opcional; hora local)<input name="end" type="datetime-local" min={localInput(item?.fechaInicial || defaultStart())} defaultValue={item?.fechaFinal ? localInput(item.fechaFinal) : ''} /></label>
    <div className="form-actions"><button type="button" className="secondary-button" onClick={onClose}>Cancelar</button><button type="submit" className="primary-button" disabled={busy}>{busy ? 'Guardando…' : item ? 'Guardar cambios' : 'Crear programación'}</button></div>
  </form></Modal>
}

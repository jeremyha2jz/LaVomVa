import { Building2, Car, Pencil, Plus, UserRound } from 'lucide-react'
import { useMemo, useState, type FormEvent } from 'react'
import { useApp } from '../context/AppContext'
import { Modal, PageHeader, SearchBox, StatusBadge } from '../components/ui'
import { api } from '../services/api'
import type { Department, Employee, Vehicle } from '../types'

type Catalog = 'employees' | 'vehicles' | 'departments'
type Item = Employee | Vehicle | Department
const routes: Record<Catalog, string> = { employees: 'empleados', vehicles: 'vehiculos', departments: 'departamentos' }

export function Catalogs() {
  const { catalogs: { departments, employees, vehicles }, session, refresh, notify } = useApp()
  const [tab, setTab] = useState<Catalog>('employees')
  const [search, setSearch] = useState('')
  const [editing, setEditing] = useState<Item | null | 'new'>(null)
  const [busy, setBusy] = useState(false)
  const canManage = ['ADMINISTRADOR', 'SUPERVISOR'].includes(session?.role || '')
  const filteredEmployees = useMemo(() => employees.filter((e) => `${e.code} ${e.name} ${e.department}`.toLowerCase().includes(search.toLowerCase())), [employees, search])
  const filteredVehicles = useMemo(() => vehicles.filter((v) => `${v.plate} ${v.code} ${v.brand} ${v.model} ${v.department}`.toLowerCase().includes(search.toLowerCase())), [vehicles, search])
  const filteredDepartments = useMemo(() => departments.filter((d) => `${d.code} ${d.name}`.toLowerCase().includes(search.toLowerCase())), [departments, search])
  const selected = editing === 'new' ? null : editing
  const departmentId = selected && 'department' in selected ? departments.find((d) => d.name === selected.department)?.id : undefined

  async function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const data = new FormData(event.currentTarget)
    const field = (name: string) => String(data.get(name) || '').trim()
    const body = tab === 'departments' ? {
      codigo: field('code'), nombre: field('name'), descripcion: field('description'), activo: true,
    } : tab === 'employees' ? {
      codigoEmpleado: field('code'), nombreCompleto: field('name'), cedula: field('document'),
      departamentoId: Number(data.get('department')), cargo: field('position'), correo: field('email'), telefonoMovil: field('phone'), activo: true,
    } : {
      placa: field('plate'), ficha: field('code'), marca: field('brand'), modelo: field('model'),
      anio: field('year') ? Number(data.get('year')) : null, tipo: field('type'), departamentoId: Number(data.get('department')),
      capacidadTanqueGalones: Number(data.get('capacity')), odometroKm: Number(data.get('odometer')), activo: true,
    }
    setBusy(true)
    try {
      if (selected) await api.catalogUpdate(routes[tab], selected.id, body)
      else await api.catalogCreate(routes[tab], body)
      await refresh()
      setEditing(null)
      notify('Catálogo actualizado', 'Los cambios quedaron guardados.')
    } catch (cause) { notify('No se pudo guardar', cause instanceof Error ? cause.message : 'Ocurrió un error inesperado.', 'error') }
    finally { setBusy(false) }
  }
  async function deactivate() {
    if (!selected || !window.confirm('¿Desactivar este registro?')) return
    setBusy(true)
    try { await api.catalogDeactivate(routes[tab], selected.id); await refresh(); setEditing(null); notify('Registro desactivado', 'El catálogo fue actualizado.') }
    catch (cause) { notify('No se pudo desactivar', cause instanceof Error ? cause.message : 'Ocurrió un error inesperado.', 'error') }
    finally { setBusy(false) }
  }
  const editorTitle = tab === 'employees' ? 'empleado' : tab === 'vehicles' ? 'vehículo' : 'departamento'
  return <div className="page"><PageHeader eyebrow="Configuración" title="Catálogos organizacionales" description="Gestiona empleados, vehículos y departamentos registrados." actions={canManage ? <button className="primary-button" onClick={() => setEditing('new')}><Plus size={17} /> Nuevo {editorTitle}</button> : undefined} />
    <div className="tabs"><button className={tab === 'employees' ? 'active' : ''} onClick={() => setTab('employees')}><UserRound size={17} /> Empleados <span>{employees.length}</span></button><button className={tab === 'vehicles' ? 'active' : ''} onClick={() => setTab('vehicles')}><Car size={17} /> Vehículos <span>{vehicles.length}</span></button><button className={tab === 'departments' ? 'active' : ''} onClick={() => setTab('departments')}><Building2 size={17} /> Departamentos <span>{departments.length}</span></button></div>
    <section className="panel table-panel"><div className="table-toolbar"><SearchBox value={search} onChange={setSearch} placeholder={`Buscar ${tab === 'employees' ? 'empleados' : tab === 'vehicles' ? 'vehículos' : 'departamentos'}…`} /></div><div className="table-scroll">
      {tab === 'employees' && <table><thead><tr><th>Empleado</th><th>Documento</th><th>Departamento / cargo</th><th>Contacto</th><th>Estado</th>{canManage && <th />}</tr></thead><tbody>{filteredEmployees.map((e) => <tr key={e.id}><td><div className="person-cell"><span className="initials">{e.name.split(' ').map((n) => n[0]).slice(0, 2).join('')}</span><span><strong>{e.name}</strong><small>{e.code}</small></span></div></td><td>{e.document}</td><td><strong>{e.department}</strong><small>{e.position}</small></td><td>{e.email}<small>{e.phone}</small></td><td><StatusBadge value={e.active ? 'ACTIVO' : 'INACTIVO'} /></td>{canManage && <td><button className="icon-button" aria-label={`Editar ${e.name}`} onClick={() => setEditing(e)}><Pencil size={16} /></button></td>}</tr>)}</tbody></table>}
      {tab === 'vehicles' && <table><thead><tr><th>Vehículo</th><th>Ficha</th><th>Departamento</th><th>Capacidad</th><th>Odómetro</th><th>Estado</th>{canManage && <th />}</tr></thead><tbody>{filteredVehicles.map((v) => <tr key={v.id}><td><strong>{v.plate}</strong><small>{v.brand} {v.model} · {v.year}</small></td><td>{v.code}<small>{v.type}</small></td><td>{v.department}</td><td>{v.tankCapacity} gal</td><td>{v.odometer.toLocaleString()} km</td><td><StatusBadge value={v.active ? 'ACTIVO' : 'INACTIVO'} /></td>{canManage && <td><button className="icon-button" aria-label={`Editar ${v.plate}`} onClick={() => setEditing(v)}><Pencil size={16} /></button></td>}</tr>)}</tbody></table>}
      {tab === 'departments' && <table><thead><tr><th>Departamento</th><th>Código</th><th>Empleados</th><th>Vehículos</th><th>Estado</th>{canManage && <th />}</tr></thead><tbody>{filteredDepartments.map((d) => <tr key={d.id}><td><strong>{d.name}</strong></td><td>{d.code}</td><td>{d.employees}</td><td>{d.vehicles}</td><td><StatusBadge value={d.active ? 'ACTIVO' : 'INACTIVO'} /></td>{canManage && <td><button className="icon-button" aria-label={`Editar ${d.name}`} onClick={() => setEditing(d)}><Pencil size={16} /></button></td>}</tr>)}</tbody></table>}
    </div></section>
    {editing && <Modal title={`${selected ? 'Editar' : 'Nuevo'} ${editorTitle}`} subtitle="Completa los datos del registro." onClose={() => setEditing(null)} size="lg"><form className="form-grid" onSubmit={(event) => void save(event)}>
      {tab === 'departments' && <><label>Código<input name="code" defaultValue={selected && 'code' in selected ? selected.code : ''} required /></label><label>Nombre<input name="name" defaultValue={selected && 'name' in selected ? selected.name : ''} required /></label><label className="form-wide">Descripción<input name="description" /></label></>}
      {tab === 'employees' && <><label>Código empleado<input name="code" defaultValue={selected && 'code' in selected ? selected.code : ''} required /></label><label>Nombre completo<input name="name" defaultValue={selected && 'name' in selected ? selected.name : ''} required /></label><label>Cédula<input name="document" defaultValue={selected && 'document' in selected ? selected.document : ''} required /></label><label>Departamento<select name="department" defaultValue={departmentId || ''} required><option value="">Selecciona</option>{departments.map((d) => <option key={d.id} value={d.id}>{d.name}</option>)}</select></label><label>Cargo<input name="position" defaultValue={selected && 'position' in selected ? selected.position : ''} /></label><label>Correo<input name="email" type="email" defaultValue={selected && 'email' in selected ? selected.email : ''} /></label><label>Teléfono<input name="phone" defaultValue={selected && 'phone' in selected ? selected.phone : ''} /></label></>}
      {tab === 'vehicles' && <><label>Placa<input name="plate" defaultValue={selected && 'plate' in selected ? selected.plate : ''} required /></label><label>Ficha<input name="code" defaultValue={selected && 'code' in selected ? selected.code : ''} required /></label><label>Marca<input name="brand" defaultValue={selected && 'brand' in selected ? selected.brand : ''} required /></label><label>Modelo<input name="model" defaultValue={selected && 'model' in selected ? selected.model : ''} required /></label><label>Año<input name="year" type="number" min="1900" max="2100" defaultValue={selected && 'year' in selected ? selected.year : ''} /></label><label>Tipo<input name="type" defaultValue={selected && 'type' in selected ? selected.type : ''} /></label><label>Departamento<select name="department" defaultValue={departmentId || ''} required><option value="">Selecciona</option>{departments.map((d) => <option key={d.id} value={d.id}>{d.name}</option>)}</select></label><label>Capacidad del tanque (gal)<input name="capacity" type="number" min="0" step="0.1" defaultValue={selected && 'tankCapacity' in selected ? selected.tankCapacity : ''} /></label><label>Odómetro (km)<input name="odometer" type="number" min="0" step="0.1" defaultValue={selected && 'odometer' in selected ? selected.odometer : 0} /></label></>}
      <div className="form-actions">{selected && <button type="button" className="danger-button" disabled={busy} onClick={() => void deactivate()}>Desactivar</button>}<button type="button" className="secondary-button" onClick={() => setEditing(null)}>Cancelar</button><button className="primary-button" disabled={busy}>{busy ? 'Guardando…' : 'Guardar'}</button></div>
    </form></Modal>}
  </div>
}

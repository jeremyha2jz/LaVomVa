import { useEffect, useState, type FormEvent } from 'react'
import { Fuel, KeyRound, Pencil, UserCog } from 'lucide-react'
import { useApp } from '../context/AppContext'
import { Modal, PageHeader } from '../components/ui'
import { api } from '../services/api'

type UserRow = { id: number; nombreUsuario: string; correo: string; nombreCompleto: string; telefono?: string; activo: boolean; rolId?: number; rol?: string }
type RoleRow = { id: number; nombre: string }

export function Admin() {
  const { session, notify, catalogs, tanks, refresh } = useApp()
  const [users, setUsers] = useState<UserRow[]>([])
  const [roles, setRoles] = useState<RoleRow[]>([])
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [selected, setSelected] = useState<UserRow | null>(null)
  const [newPassword, setNewPassword] = useState('')
  const admin = session?.role === 'ADMINISTRADOR'
  useEffect(() => {
    if (!admin) return
    void Promise.all([api.users(), api.roles()]).then(([nextUsers, nextRoles]) => {
      setUsers(nextUsers); setRoles(nextRoles)
    }).catch((cause) => setError(cause instanceof Error ? cause.message : 'No se pudo cargar usuarios.'))
  }, [admin])
  async function create(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const form = event.currentTarget
    const data = new FormData(form)
    if (data.get('password') !== data.get('confirm')) { setError('Las contraseñas no coinciden.'); return }
    setBusy(true); setError('')
    try {
      await api.createUser({ nombreUsuario: data.get('username'), correo: data.get('email'), nombreCompleto: data.get('name'), telefono: data.get('phone') || null, password: data.get('password'), rolId: Number(data.get('role')) })
      setUsers(await api.users()); form.reset(); notify('Cuenta creada', 'El nuevo usuario ya puede iniciar sesión.')
    } catch (cause) { setError(cause instanceof Error ? cause.message : 'No se pudo crear la cuenta.') }
    finally { setBusy(false) }
  }
  async function update(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!selected) return
    const data = new FormData(event.currentTarget)
    setBusy(true); setError('')
    try {
      await api.updateUser(selected.id, { nombreCompleto: data.get('name'), correo: data.get('email'), telefono: data.get('phone') || null, rolId: Number(data.get('role')) })
      setUsers(await api.users()); setSelected(null); notify('Usuario actualizado', 'Los cambios quedaron guardados.')
    } catch (cause) { setError(cause instanceof Error ? cause.message : 'No se pudo actualizar.') }
    finally { setBusy(false) }
  }
  async function resetPassword() {
    if (!selected) return
    try { await api.resetUserPassword(selected.id, newPassword); setNewPassword(''); notify('Contraseña restablecida', 'Entrega la nueva contraseña al usuario por un medio seguro.') }
    catch (cause) { setError(cause instanceof Error ? cause.message : 'No se pudo restablecer.') }
  }
  async function deactivate() {
    if (!selected || !window.confirm('¿Desactivar esta cuenta?')) return
    try { await api.deactivateUser(selected.id); setUsers(await api.users()); setSelected(null); notify('Cuenta desactivada', 'El usuario ya no puede iniciar sesión.') }
    catch (cause) { setError(cause instanceof Error ? cause.message : 'No se pudo desactivar.') }
  }
  async function activate() {
    if (!selected) return
    try { await api.activateUser(selected.id); setUsers(await api.users()); setSelected(null); notify('Cuenta activada', 'El usuario ya puede iniciar sesión.') }
    catch (cause) { setError(cause instanceof Error ? cause.message : 'No se pudo activar.') }
  }
  return <div className="page"><PageHeader eyebrow="Configuración" title="Administración del sistema" description="Cuenta actual y gestión de usuarios." />
    <section className="settings-grid"><article className="panel settings-card"><span><UserCog size={21} /></span><h2>Sesión actual</h2><dl><div><dt>Usuario</dt><dd>{session?.name}</dd></div><div><dt>Rol</dt><dd>{session?.role}</dd></div><div><dt>Origen</dt><dd>API / PostgreSQL</dd></div></dl></article>
      {admin && <article className="panel settings-card"><span><KeyRound size={21} /></span><h2>Crear usuario</h2><form onSubmit={(event) => void create(event)} className="admin-user-form">
        <label>Nombre completo<input name="name" required minLength={3} /></label>
        <label>Usuario<input name="username" required minLength={3} /></label>
        <label>Correo<input name="email" type="email" required /></label>
        <label>Teléfono<input name="phone" /></label>
        <label>Rol<select name="role" required defaultValue=""><option value="">Selecciona un rol</option>{roles.map((role) => <option key={role.id} value={role.id}>{role.nombre}</option>)}</select></label>
        <label>Contraseña<input name="password" type="password" minLength={12} required autoComplete="new-password" /></label>
        <label>Confirmar contraseña<input name="confirm" type="password" minLength={12} required autoComplete="new-password" /></label>
        <button className="primary-button" disabled={busy || roles.length === 0}>{busy ? 'Guardando…' : 'Crear cuenta'}</button>
      </form></article>}
    </section>
    {error && <p className="login-error" role="alert">{error}</p>}
    {admin && <section className="panel table-panel"><div className="table-scroll"><table><thead><tr><th>Usuario</th><th>Nombre</th><th>Correo</th><th>Rol</th><th>Estado</th><th /></tr></thead><tbody>{users.map((user) => <tr key={user.id}><td>{user.nombreUsuario}</td><td>{user.nombreCompleto}</td><td>{user.correo}</td><td>{user.rol || '—'}</td><td>{user.activo ? 'Activo' : 'Inactivo'}</td><td><button className="icon-button" aria-label={`Editar ${user.nombreUsuario}`} onClick={() => setSelected(user)}><Pencil size={16} /></button></td></tr>)}</tbody></table></div></section>}
    {selected && <Modal title={`Editar ${selected.nombreUsuario}`} subtitle="Gestiona el perfil y los permisos de esta cuenta." onClose={() => { setSelected(null); setError('') }}><form className="form-grid" onSubmit={(event) => void update(event)}>
      {error && <p className="login-error form-wide" role="alert">{error}</p>}
      <label>Nombre completo<input name="name" defaultValue={selected.nombreCompleto} required /></label>
      <label>Correo<input name="email" type="email" defaultValue={selected.correo} required /></label>
      <label>Teléfono<input name="phone" defaultValue={selected.telefono || ''} /></label>
      <label>Rol<select name="role" defaultValue={selected.rolId} required>{roles.map((role) => <option key={role.id} value={role.id}>{role.nombre}</option>)}</select></label>
      <label className="form-wide">Nueva contraseña<input type="password" value={newPassword} onChange={(event) => setNewPassword(event.target.value)} minLength={12} autoComplete="new-password" placeholder="Mínimo 12 caracteres" /></label>
      <div className="form-actions"><button type="button" className="secondary-button" disabled={newPassword.length < 12} onClick={() => void resetPassword()}>Restablecer contraseña</button>{selected.activo ? selected.id !== session?.id && <button type="button" className="danger-button" onClick={() => void deactivate()}>Desactivar</button> : <button type="button" className="secondary-button" onClick={() => void activate()}>Activar cuenta</button>}<button className="primary-button" disabled={busy}>Guardar</button></div>
    </form></Modal>}
    {['ADMINISTRADOR', 'SUPERVISOR'].includes(session?.role || '') && <OperationalSetup catalogs={catalogs} tanks={tanks} refresh={refresh} notify={notify} />}
  </div>
}

function OperationalSetup({ catalogs, tanks, refresh, notify }: {
  catalogs: ReturnType<typeof useApp>['catalogs']; tanks: ReturnType<typeof useApp>['tanks']; refresh: () => Promise<void>;
  notify: ReturnType<typeof useApp>['notify']
}) {
  const [kind, setKind] = useState<'proveedor' | 'estacion' | 'tanque' | null>(null)
  const [busy, setBusy] = useState(false)
  async function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!kind) return
    const data = new FormData(event.currentTarget)
    const field = (name: string) => String(data.get(name) || '').trim()
    setBusy(true)
    try {
      if (kind === 'proveedor') await api.createSupplier({ nombre: field('name'), rnc: field('rnc'), telefono: field('phone'), correo: field('email'), activo: true })
      if (kind === 'estacion') await api.catalogCreate('estaciones', { nombre: field('name'), ubicacion: field('location'), activo: true })
      if (kind === 'tanque') await api.catalogCreate('tanques', { codigo: field('code'), nombre: field('name'), estacionId: Number(data.get('station')), tipoCombustibleId: Number(data.get('fuel')), capacidadGalones: Number(data.get('capacity')), existenciaActualGalones: 0, nivelCriticoGalones: Number(data.get('critical')), activo: true })
      await refresh(); setKind(null); notify('Configuración guardada', 'El nuevo registro ya está disponible.')
    } catch (cause) { notify('No se pudo guardar', cause instanceof Error ? cause.message : 'Error de la API.', 'error') }
    finally { setBusy(false) }
  }
  return <section className="panel settings-card" style={{ marginTop: 20 }}><span><Fuel size={21} /></span><h2>Configuración operativa</h2><p>Crea proveedores, estaciones y tanques antes de registrar recepciones y despachos.</p>
    <div className="load-actions"><button className="secondary-button" onClick={() => setKind('proveedor')}>Nuevo proveedor</button><button className="secondary-button" onClick={() => setKind('estacion')}>Nueva estación</button><button className="secondary-button" onClick={() => setKind('tanque')} disabled={catalogs.stations.length === 0 || catalogs.fuelTypes.length === 0}>Nuevo tanque</button></div>
    <p>{catalogs.suppliers.length} proveedores · {catalogs.stations.length} estaciones · {tanks.length} tanques</p>
    {kind && <Modal title={`Nuevo ${kind}`} subtitle="El registro quedará disponible en la API." onClose={() => setKind(null)}><form className="form-grid" onSubmit={(event) => void save(event)}>
      {kind === 'proveedor' && <><label>Nombre<input name="name" required /></label><label>RNC<input name="rnc" required /></label><label>Teléfono<input name="phone" /></label><label>Correo<input name="email" type="email" /></label></>}
      {kind === 'estacion' && <><label>Nombre<input name="name" required /></label><label>Ubicación<input name="location" required /></label></>}
      {kind === 'tanque' && <><label>Código<input name="code" required /></label><label>Nombre<input name="name" required /></label><label>Estación<select name="station" required defaultValue=""><option value="">Selecciona</option>{catalogs.stations.map((station) => <option key={station.id} value={station.id}>{station.name}</option>)}</select></label><label>Combustible<select name="fuel" required defaultValue=""><option value="">Selecciona</option>{catalogs.fuelTypes.map((fuel) => <option key={fuel.id} value={fuel.id}>{fuel.name}</option>)}</select></label><label>Capacidad (gal)<input name="capacity" type="number" min="0.1" step="0.1" required /></label><label>Nivel crítico (gal)<input name="critical" type="number" min="0" step="0.1" required /></label></>}
      <div className="form-actions"><button type="button" className="secondary-button" onClick={() => setKind(null)}>Cancelar</button><button className="primary-button" disabled={busy}>{busy ? 'Guardando…' : 'Guardar'}</button></div>
    </form></Modal>}
  </section>
}

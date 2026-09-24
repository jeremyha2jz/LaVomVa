import { KeyRound, ShieldCheck, UserCog } from 'lucide-react'
import { useApp } from '../context/AppContext'
import { PageHeader } from '../components/ui'

export function Admin() {
  const { live, session } = useApp()
  return <div className="page"><PageHeader eyebrow="Configuración" title="Administración del sistema" description="Estado de la sesión y funciones administrativas disponibles." />
    <section className="settings-grid"><article className="panel settings-card"><span><UserCog size={21} /></span><h2>Sesión actual</h2><p>{live ? 'Cuenta autenticada en el backend.' : 'Datos de muestra, sin conexión con la base de datos.'}</p><dl><div><dt>Usuario</dt><dd>{session?.name || 'Demostración'}</dd></div><div><dt>Rol</dt><dd>{session?.role || 'Sin rol real'}</dd></div><div><dt>Origen</dt><dd>{live ? 'API / PostgreSQL' : 'Navegador local'}</dd></div></dl></article>
      <article className="panel settings-card"><span><KeyRound size={21} /></span><h2>Usuarios y roles</h2><p>El backend dispone de endpoints para cuentas y roles. Esta interfaz aún no permite administrarlos; no se simularán cambios de seguridad.</p></article>
      <article className="panel settings-card"><span><ShieldCheck size={21} /></span><h2>Seguridad pendiente</h2><p>Antes de usar datos reales, se deben restringir los endpoints por rol, definir el alta inicial de administrador y configurar HTTPS y secretos fuera del repositorio.</p></article></section>
  </div>
}

import { useRef, useState, type FormEvent, type KeyboardEvent } from 'react'
import { ArrowRight, Check, Eye, EyeOff, Fuel, LockKeyhole, Mail, QrCode, ShieldCheck, UserRound } from 'lucide-react'
import { useApp } from '../context/AppContext'

type Mode = 'login' | 'register'
type Field = 'name' | 'email' | 'username' | 'password' | 'confirm'

function friendlyError(cause: unknown) {
  if (cause instanceof TypeError) return 'No pudimos conectar con el servidor. Comprueba tu conexión e inténtalo de nuevo.'
  if (cause instanceof Error) return cause.message
  return 'Ocurrió un error. Inténtalo de nuevo.'
}

export function Login() {
  const { login, register } = useApp()
  const [mode, setMode] = useState<Mode>('login')
  const [busy, setBusy] = useState(false)
  const [showPassword, setShowPassword] = useState(false)
  const [capsLock, setCapsLock] = useState(false)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const [fieldErrors, setFieldErrors] = useState<Partial<Record<Field, string>>>({})
  const submitting = useRef(false)

  function changeMode(next: Mode) {
    if (submitting.current || next === mode) return
    setMode(next)
    setError('')
    setNotice('')
    setFieldErrors({})
    setShowPassword(false)
    setCapsLock(false)
  }

  function clearField(field: Field) {
    setFieldErrors((current) => ({ ...current, [field]: undefined }))
    if (error) setError('')
  }

  function detectCapsLock(event: KeyboardEvent<HTMLInputElement>) {
    setCapsLock(event.getModifierState('CapsLock'))
  }

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (submitting.current) return
    const form = event.currentTarget
    const data = new FormData(form)
    const username = String(data.get('username') || '').trim()
    const password = String(data.get('password') || '')
    const name = String(data.get('name') || '').trim()
    const email = String(data.get('email') || '').trim().toLowerCase()
    const confirm = String(data.get('confirm') || '')
    const nextErrors: Partial<Record<Field, string>> = {}

    if (!username) nextErrors.username = 'Escribe tu usuario.'
    else if (mode === 'register' && username.length < 3) nextErrors.username = 'Usa al menos 3 caracteres.'
    if (!password) nextErrors.password = 'Escribe tu contraseña.'
    else if (mode === 'register' && password.length < 12) nextErrors.password = 'Usa al menos 12 caracteres.'
    if (mode === 'register') {
      if (name.length < 3) nextErrors.name = 'Escribe tu nombre completo.'
      if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email)) nextErrors.email = 'Escribe un correo válido.'
      if (confirm !== password) nextErrors.confirm = 'Las contraseñas no coinciden.'
    }
    setFieldErrors(nextErrors)
    const firstInvalid = (Object.keys(nextErrors) as Field[])[0]
    if (firstInvalid) { document.getElementById(`auth-${firstInvalid}`)?.focus(); return }

    submitting.current = true
    setBusy(true)
    setError('')
    setNotice('')
    try {
      if (mode === 'register') {
        await register(username, email, name, password)
        form.reset()
        setMode('login')
        setNotice('Cuenta creada. Un administrador debe activarla antes de que puedas iniciar sesión.')
      } else {
        await login(username, password)
      }
    } catch (cause) {
      setError(friendlyError(cause))
    } finally {
      submitting.current = false
      setBusy(false)
    }
  }

  return <main className="auth-layout">
    <section className="auth-story" aria-label="LaVomVa, gestión de combustible">
      <div className="auth-story-top"><span className="auth-brand-mark"><Fuel size={25} strokeWidth={2.3} /></span><span><strong>LaVomVa</strong><small>GESTIÓN DE COMBUSTIBLE</small></span></div>
      <div className="auth-story-main">
        <span className="auth-story-eyebrow"><span /> PLATAFORMA DE CONTROL</span>
        <h1>Cada galón,<br /><em>bajo control.</em></h1>
        <p>Solicitudes, tickets digitales e inventario en un solo lugar. Información clara en cada paso del proceso.</p>
        <div className="auth-ticket-art" aria-hidden="true">
          <div className="auth-ticket-head"><span className="auth-ticket-symbol"><Fuel size={21} /></span><span>LaVomVa<small>TICKET DIGITAL</small></span><span className="auth-ticket-status"><i /> CONTROLADO</span></div>
          <div className="auth-ticket-main"><div className="auth-ticket-lines"><i /><i /><i /></div><div className="auth-ticket-qr"><QrCode size={84} strokeWidth={1.25} /></div></div>
          <div className="auth-ticket-foot"><span>Solicitud</span><span className="auth-ticket-arrow">→</span><span>Autorización</span><span className="auth-ticket-arrow">→</span><span>Despacho</span></div>
        </div>
      </div>
      <div className="auth-story-bottom"><ShieldCheck size={17} /><span>Acceso protegido para cada perfil</span></div>
    </section>

    <section className="auth-panel" aria-label="Acceso a la plataforma">
      <div className="auth-mobile-brand"><span className="auth-brand-mark"><Fuel size={20} /></span><strong>LaVomVa</strong></div>
      <div className="auth-card">
        <div className="auth-header"><span className="auth-header-icon">{mode === 'login' ? <LockKeyhole size={22} /> : <UserRound size={22} />}</span><span className="auth-kicker">{mode === 'login' ? 'ACCESO A LA PLATAFORMA' : 'NUEVA CUENTA'}</span><h2>{mode === 'login' ? 'Bienvenido de nuevo' : 'Crea tu cuenta'}</h2><p>{mode === 'login' ? 'Ingresa tus datos para continuar con tu trabajo.' : 'Completa tus datos. Un administrador revisará y activará tu cuenta.'}</p></div>
        <div className="auth-tabs" role="tablist" aria-label="Tipo de acceso" onKeyDown={(event) => {
          if (event.key !== 'ArrowLeft' && event.key !== 'ArrowRight') return
          event.preventDefault()
          const next = event.key === 'ArrowLeft' ? 'login' : 'register'
          changeMode(next)
          event.currentTarget.querySelector<HTMLButtonElement>(`#auth-tab-${next}`)?.focus()
        }}><button id="auth-tab-login" type="button" role="tab" aria-selected={mode === 'login'} className={mode === 'login' ? 'active' : ''} onClick={() => changeMode('login')} disabled={busy}>Iniciar sesión</button><button id="auth-tab-register" type="button" role="tab" aria-selected={mode === 'register'} className={mode === 'register' ? 'active' : ''} onClick={() => changeMode('register')} disabled={busy}>Crear cuenta</button></div>
        {notice && <div className="auth-notice" role="status"><Check size={18} /><span>{notice}</span></div>}
        <form key={mode} className="auth-form" role="tabpanel" aria-labelledby={`auth-tab-${mode}`} onSubmit={(event) => void submit(event)} noValidate>
          {mode === 'register' && <>
            <div className="auth-field"><label htmlFor="auth-name">Nombre completo</label><div className={`auth-input ${fieldErrors.name ? 'invalid' : ''}`}><UserRound size={18} /><input id="auth-name" name="name" type="text" autoComplete="name" placeholder="Tu nombre y apellido" maxLength={150} autoFocus aria-invalid={!!fieldErrors.name} aria-describedby={fieldErrors.name ? 'auth-name-error' : undefined} onChange={() => clearField('name')} disabled={busy} /></div>{fieldErrors.name && <small id="auth-name-error" className="auth-field-error">{fieldErrors.name}</small>}</div>
            <div className="auth-field"><label htmlFor="auth-email">Correo electrónico</label><div className={`auth-input ${fieldErrors.email ? 'invalid' : ''}`}><Mail size={18} /><input id="auth-email" name="email" type="email" autoComplete="email" placeholder="nombre@empresa.com" maxLength={150} aria-invalid={!!fieldErrors.email} aria-describedby={fieldErrors.email ? 'auth-email-error' : undefined} onChange={() => clearField('email')} disabled={busy} /></div>{fieldErrors.email && <small id="auth-email-error" className="auth-field-error">{fieldErrors.email}</small>}</div>
          </>}
          <div className="auth-field"><label htmlFor="auth-username">Usuario</label><div className={`auth-input ${fieldErrors.username ? 'invalid' : ''}`}><UserRound size={18} /><input id="auth-username" name="username" type="text" autoComplete="username" placeholder="Tu nombre de usuario" maxLength={80} autoFocus={mode === 'login'} aria-invalid={!!fieldErrors.username} aria-describedby={fieldErrors.username ? 'auth-username-error' : undefined} onChange={() => clearField('username')} disabled={busy} /></div>{fieldErrors.username && <small id="auth-username-error" className="auth-field-error">{fieldErrors.username}</small>}</div>
          <div className="auth-field"><label htmlFor="auth-password">Contraseña</label><div className={`auth-input ${fieldErrors.password ? 'invalid' : ''}`}><LockKeyhole size={18} /><input id="auth-password" name="password" type={showPassword ? 'text' : 'password'} autoComplete={mode === 'login' ? 'current-password' : 'new-password'} placeholder={mode === 'login' ? 'Introduce tu contraseña' : 'Mínimo 12 caracteres'} aria-invalid={!!fieldErrors.password} aria-describedby={fieldErrors.password ? 'auth-password-error' : undefined} onChange={() => clearField('password')} onKeyUp={detectCapsLock} onKeyDown={detectCapsLock} disabled={busy} /><button type="button" className="auth-eye" aria-label={showPassword ? 'Ocultar contraseña' : 'Mostrar contraseña'} aria-pressed={showPassword} onClick={() => setShowPassword(!showPassword)} disabled={busy}>{showPassword ? <EyeOff size={18} /> : <Eye size={18} />}</button></div>{fieldErrors.password && <small id="auth-password-error" className="auth-field-error">{fieldErrors.password}</small>}{capsLock && <small className="auth-caps">Bloq Mayús está activado.</small>}</div>
          {mode === 'register' && <div className="auth-field"><label htmlFor="auth-confirm">Confirmar contraseña</label><div className={`auth-input ${fieldErrors.confirm ? 'invalid' : ''}`}><LockKeyhole size={18} /><input id="auth-confirm" name="confirm" type={showPassword ? 'text' : 'password'} autoComplete="new-password" placeholder="Repite tu contraseña" aria-invalid={!!fieldErrors.confirm} aria-describedby={fieldErrors.confirm ? 'auth-confirm-error' : undefined} onChange={() => clearField('confirm')} disabled={busy} /></div>{fieldErrors.confirm && <small id="auth-confirm-error" className="auth-field-error">{fieldErrors.confirm}</small>}</div>}
          {error && <div className="auth-error" role="alert">{error}</div>}
          <button className="auth-submit" type="submit" disabled={busy}><span>{busy ? 'Procesando…' : mode === 'login' ? 'Ingresar a la plataforma' : 'Solicitar cuenta'}</span>{busy ? <span className="auth-spinner" aria-hidden="true" /> : <ArrowRight size={19} />}</button>
        </form>
        <div className="auth-footer">{mode === 'login' ? <>¿Aún no tienes cuenta? <button type="button" onClick={() => changeMode('register')}>Solicita acceso</button></> : <>¿Ya tienes cuenta? <button type="button" onClick={() => changeMode('login')}>Inicia sesión</button></>}</div>
        <p className="auth-help">Si no puedes acceder, solicita ayuda a un administrador del sistema.</p>
      </div>
      <div className="auth-panel-bottom">LaVomVa · Plataforma de gestión de combustible</div>
    </section>
  </main>
}

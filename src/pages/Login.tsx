import { useState, type FormEvent } from 'react'
import { Fuel, LockKeyhole } from 'lucide-react'
import { useApp } from '../context/AppContext'

export function Login() {
  const { login, register } = useApp()
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [mode, setMode] = useState<'login' | 'register'>('login')
  const [notice, setNotice] = useState('')

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const data = new FormData(event.currentTarget)
    setBusy(true)
    setError('')
    try {
      if (mode === 'register') {
        if (data.get('password') !== data.get('confirm')) throw new Error('Las contraseñas no coinciden.')
        await register(String(data.get('username')), String(data.get('email')), String(data.get('name')), String(data.get('password')))
        setMode('login'); setNotice('Cuenta creada. Un administrador debe activarla antes de que puedas ingresar.')
      } else await login(String(data.get('username')), String(data.get('password')))
    }
    catch (cause) { setError(cause instanceof Error ? cause.message : 'No se pudo iniciar sesión.') }
    finally { setBusy(false) }
  }

  return <main className="login-screen"><form className="login-card" onSubmit={submit}>
    <span className="login-mark"><Fuel size={25} /></span>
    <p className="eyebrow">Gestión de combustible</p>
    <h1>{mode === 'login' ? 'Bienvenido a LaVomVa' : 'Crear cuenta'}</h1>
    <p>{mode === 'login' ? 'Ingresa con tu cuenta para consultar tickets, solicitudes e inventario.' : 'La cuenta nueva tendrá acceso de consulta después de que un administrador la active.'}</p>
    {mode === 'register' && <><label>Nombre completo<input name="name" autoComplete="name" required minLength={3} /></label><label>Correo<input name="email" type="email" autoComplete="email" required /></label></>}
    <label>Usuario<input name="username" autoComplete="username" required autoFocus /></label>
    <label>Contraseña<input name="password" type="password" autoComplete={mode === 'login' ? 'current-password' : 'new-password'} required minLength={mode === 'register' ? 12 : undefined} /></label>
    {mode === 'register' && <label>Confirmar contraseña<input name="confirm" type="password" autoComplete="new-password" required /></label>}
    {error && <p className="login-error" role="alert">{error}</p>}
    {notice && <p role="status">{notice}</p>}
    <button className="primary-button" disabled={busy} type="submit"><LockKeyhole size={17} /> {busy ? 'Procesando…' : mode === 'login' ? 'Iniciar sesión' : 'Crear cuenta'}</button>
    <button className="secondary-button" type="button" onClick={() => { setMode(mode === 'login' ? 'register' : 'login'); setError(''); setNotice('') }}>{mode === 'login' ? 'Crear una cuenta' : 'Ya tengo cuenta'}</button>
  </form></main>
}

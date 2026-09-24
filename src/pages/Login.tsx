import { useState, type FormEvent } from 'react'
import { Fuel, LockKeyhole } from 'lucide-react'
import { useApp } from '../context/AppContext'

export function Login() {
  const { login } = useApp()
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const data = new FormData(event.currentTarget)
    setBusy(true)
    setError('')
    try { await login(String(data.get('username')), String(data.get('password'))) }
    catch (cause) { setError(cause instanceof Error ? cause.message : 'No se pudo iniciar sesión.') }
    finally { setBusy(false) }
  }

  return <main className="login-screen"><form className="login-card" onSubmit={submit}>
    <span className="login-mark"><Fuel size={25} /></span>
    <p className="eyebrow">Gestión de combustible</p>
    <h1>Bienvenido a LaVomVa</h1>
    <p>Ingresa con tu cuenta para consultar tickets, solicitudes e inventario.</p>
    <label>Usuario<input name="username" autoComplete="username" required autoFocus /></label>
    <label>Contraseña<input name="password" type="password" autoComplete="current-password" required /></label>
    {error && <p className="login-error" role="alert">{error}</p>}
    <button className="primary-button" disabled={busy} type="submit"><LockKeyhole size={17} /> {busy ? 'Ingresando…' : 'Iniciar sesión'}</button>
    <small>La conexión requiere la API y PostgreSQL configurados.</small>
  </form></main>
}

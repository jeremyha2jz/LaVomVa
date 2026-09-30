import { useEffect, useState } from 'react'
import { AppShell, type PageKey } from './components/AppShell'
import { Dashboard } from './pages/Dashboard'
import { Requests } from './pages/Requests'
import { Tickets } from './pages/Tickets'
import { Dispatch } from './pages/Dispatch'
import { Inventory } from './pages/Inventory'
import { Operations } from './pages/Operations'
import { Catalogs } from './pages/Catalogs'
import { Reports } from './pages/Reports'
import { Closings } from './pages/Closings'
import { Admin } from './pages/Admin'
import { Login } from './pages/Login'
import { useApp } from './context/AppContext'

const pages: PageKey[] = ['dashboard', 'solicitudes', 'tickets', 'despacho', 'inventario', 'operaciones', 'cierres', 'reportes', 'catalogos', 'administracion']

export default function App() {
  const { session, loading, error, refresh, logout } = useApp()
  const initial = window.location.hash.replace('#', '') as PageKey
  const [page, setPage] = useState<PageKey>(pages.includes(initial) ? initial : 'dashboard')
  useEffect(() => {
    const onHash = () => {
      const next = window.location.hash.replace('#', '') as PageKey
      if (pages.includes(next)) setPage(next)
    }
    window.addEventListener('hashchange', onHash)
    return () => window.removeEventListener('hashchange', onHash)
  }, [])
  const navigate = (next: PageKey) => { window.location.hash = next; setPage(next) }
  const content = {
    dashboard: <Dashboard onNavigate={navigate} />,
    solicitudes: <Requests />,
    tickets: <Tickets />,
    despacho: <Dispatch />,
    inventario: <Inventory />,
    operaciones: <Operations />,
    cierres: <Closings />,
    catalogos: <Catalogs />,
    reportes: <Reports />,
    administracion: <Admin />,
  }[page]
  if (!session) return <Login />
  if (loading) return <div className="load-screen"><span className="login-mark">L</span><h1>Cargando LaVomVa</h1><p>Cargando…</p></div>
  if (error) return <div className="load-screen"><h1>No se pudo conectar</h1><p>{error}</p><div className="load-actions"><button className="primary-button" onClick={() => void refresh().catch(() => {})}>Reintentar</button><button className="secondary-button" onClick={logout}>Cerrar sesión</button></div></div>
  return <AppShell page={page} onNavigate={navigate}>{content}</AppShell>
}

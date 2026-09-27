import { useCallback, useEffect, useState, type ReactNode } from 'react'
import { Activity, Bell, Boxes, CalendarCheck, ChevronDown, ClipboardList, FileBarChart, Fuel, Gauge, LayoutDashboard, Menu, QrCode, Search, Settings, ShieldCheck, TicketCheck, Users, X } from 'lucide-react'
import { useApp } from '../context/AppContext'
import { listNotifications, markAllNotificationsRead, markNotificationRead, unreadNotificationCount, type AppNotification } from '../services/api'

export type PageKey = 'dashboard' | 'solicitudes' | 'tickets' | 'despacho' | 'inventario' | 'operaciones' | 'cierres' | 'catalogos' | 'reportes' | 'administracion'

const navigation: { label: string; items: { key: PageKey; label: string; icon: typeof Gauge }[] }[] = [
  { label: 'Operación', items: [
    { key: 'dashboard', label: 'Resumen', icon: LayoutDashboard },
    { key: 'solicitudes', label: 'Solicitudes', icon: ClipboardList },
    { key: 'tickets', label: 'Tickets digitales', icon: TicketCheck },
    { key: 'despacho', label: 'Punto de despacho', icon: QrCode },
  ] },
  { label: 'Combustible', items: [
    { key: 'inventario', label: 'Inventario', icon: Fuel },
    { key: 'operaciones', label: 'Recepciones y movimientos', icon: Activity },
    { key: 'cierres', label: 'Cierre diario', icon: CalendarCheck },
    { key: 'reportes', label: 'Reportes', icon: FileBarChart },
  ] },
  { label: 'Configuración', items: [
    { key: 'catalogos', label: 'Empleados y vehículos', icon: Users },
    { key: 'administracion', label: 'Administración', icon: Settings },
  ] },
]

function Logo() { return <div className="brand"><div className="brand-mark"><img src="/lavomva-logo.png" alt="" /></div><div><strong>LaVomVa</strong><span>Gestión de combustible</span></div></div> }

export function AppShell({ page, onNavigate, children }: { page: PageKey; onNavigate: (page: PageKey) => void; children: ReactNode }) {
  const [sidebarOpen, setSidebarOpen] = useState(false)
  const [notificationsOpen, setNotificationsOpen] = useState(false)
  const [notifications, setNotifications] = useState<AppNotification[]>([])
  const [unreadCount, setUnreadCount] = useState(0)
  const [notificationError, setNotificationError] = useState('')
  const { toasts, removeToast, tanks, tickets, requests, session, logout, realtimeRevision } = useApp()
  const profileName = session?.name || ''
  const profileRole = session?.role || ''
  const initials = profileName.split(' ').map((part) => part[0]).slice(0, 2).join('').toUpperCase()
  const pending = requests.filter((request) => request.status === 'PENDIENTE').length

  const syncNotifications = useCallback(async () => {
    if (!session) { setNotifications([]); setUnreadCount(0); return }
    try {
      const [page, count] = await Promise.all([listNotifications(), unreadNotificationCount()])
      setNotifications((current) => {
        const byId = new Map(current.map((item) => [item.id, item]))
        page.items.forEach((item) => byId.set(item.id, item))
        return [...byId.values()].sort((a, b) => Date.parse(b.fechaCreacion) - Date.parse(a.fechaCreacion)).slice(0, 30)
      })
      setUnreadCount(count.cantidad)
      setNotificationError('')
    } catch (error) { setNotificationError(error instanceof Error ? error.message : 'No se pudieron cargar las notificaciones.') }
  }, [session])

  useEffect(() => setSidebarOpen(false), [page])
  useEffect(() => {
    if (!session) return
    void syncNotifications()
    const timer = window.setInterval(() => void syncNotifications(), 30_000)
    const onFocus = () => void syncNotifications()
    window.addEventListener('focus', onFocus)
    return () => { window.clearInterval(timer); window.removeEventListener('focus', onFocus) }
  }, [session, syncNotifications])
  useEffect(() => {
    if (realtimeRevision > 0) void syncNotifications()
  }, [realtimeRevision, syncNotifications])
  const navigate = (target: PageKey) => { window.location.hash = target; onNavigate(target) }
  const openNotification = async (item: AppNotification) => {
    if (!item.leida) {
      try { await markNotificationRead(item.id); setNotifications((current) => current.map((x) => x.id === item.id ? { ...x, leida: true, fechaLectura: new Date().toISOString() } : x)); setUnreadCount((n) => Math.max(0, n - 1)) }
      catch (error) { setNotificationError(error instanceof Error ? error.message : 'No se pudo marcar como leída.'); return }
    }
    const target: PageKey = item.tipo === 'INVENTARIO_BAJO' ? 'inventario' : item.tipo === 'AJUSTE_INVENTARIO' ? 'operaciones' : 'tickets'
    navigate(target); setNotificationsOpen(false)
  }
  const markAllRead = async () => {
    try { await markAllNotificationsRead(); setNotifications((current) => current.map((item) => ({ ...item, leida: true, fechaLectura: new Date().toISOString() }))); setUnreadCount(0); setNotificationError('') }
    catch (error) { setNotificationError(error instanceof Error ? error.message : 'No se pudieron marcar como leídas.') }
  }

  return <div className="app-shell">
    <aside className={`sidebar ${sidebarOpen ? 'sidebar-open' : ''}`}>
      <div className="sidebar-top"><Logo /><button className="icon-button sidebar-close" onClick={() => setSidebarOpen(false)}><X size={20} /></button></div>
      <nav>{navigation.map((section) => <div className="nav-section" key={section.label}><span className="nav-label">{section.label}</span>{section.items.map((item) => <button key={item.key} className={`nav-item ${page === item.key ? 'active' : ''}`} onClick={() => navigate(item.key)}><item.icon size={19} /><span>{item.label}</span>{item.key === 'solicitudes' && pending > 0 && <b>{pending}</b>}</button>)}</div>)}</nav>
      <div className="system-status"><div><ShieldCheck size={18} /><span><strong>Conectado a la API</strong><small>Datos de PostgreSQL</small></span></div><i /></div>
      <button className="profile-card" onClick={logout} title="Cerrar sesión"><div className="avatar">{initials}</div><span><strong>{profileName}</strong><small>{profileRole} · salir</small></span><ChevronDown size={17} /></button>
    </aside>
    {sidebarOpen && <button className="sidebar-overlay" onClick={() => setSidebarOpen(false)} aria-label="Cerrar menú" />}
    <section className="main-column">
      <header className="topbar"><button className="icon-button menu-button" onClick={() => setSidebarOpen(true)}><Menu size={21} /></button><div className="topbar-context"><span>Plataforma</span><strong>LaVomVa</strong></div><button className="topbar-search topbar-search-button" onClick={() => navigate('tickets')}><Search size={17} /><span>Buscar tickets</span></button><div className="topbar-actions"><button aria-label={`Notificaciones${unreadCount ? `, ${unreadCount} sin leer` : ''}`} aria-expanded={notificationsOpen} className="icon-button notification-button" onClick={() => { setNotificationsOpen(!notificationsOpen); void syncNotifications() }}><Bell size={20} />{unreadCount > 0 && <b className="notification-badge">{unreadCount > 99 ? '99+' : unreadCount}</b>}</button><div className="topbar-avatar">{initials}</div></div>
        {notificationsOpen && <div className="notifications-panel" role="region" aria-label="Notificaciones"><header><strong>Notificaciones</strong><span>{unreadCount} sin leer</span></header>{unreadCount > 0 && <button className="notification-mark-all" onClick={() => void markAllRead()}>Marcar todas como leídas</button>}{notificationError && <p className="notification-error" role="alert">{notificationError}</p>}{notifications.length === 0 && !notificationError && <p className="notification-empty">No tienes notificaciones.</p>}{notifications.map((item) => <button className={`notification-item ${item.leida ? 'read' : 'unread'}`} key={item.id} onClick={() => void openNotification(item)}><span className={`notice-icon ${item.tipo === 'INVENTARIO_BAJO' ? 'notice-red' : 'notice-amber'}`}>{item.tipo === 'INVENTARIO_BAJO' ? <Boxes size={18} /> : <TicketCheck size={18} />}</span><span><strong>{item.titulo}</strong><small>{item.mensaje}</small><small>{new Date(item.fechaCreacion).toLocaleString()}</small></span>{!item.leida && <i className="notification-unread-dot" />}</button>)}</div>}
      </header>
      <main>{children}</main>
    </section>
    <div className="toast-stack">{toasts.map((toast) => <button key={toast.id} onClick={() => removeToast(toast.id)} className={`toast toast-${toast.tone}`}><i /><span><strong>{toast.title}</strong><small>{toast.description}</small></span><X size={16} /></button>)}</div>
  </div>
}

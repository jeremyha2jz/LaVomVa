import { useEffect, useState, type ReactNode } from 'react'
import { Activity, Bell, CalendarCheck, ChevronDown, ClipboardList, FileBarChart, Fuel, Gauge, LayoutDashboard, Menu, QrCode, Search, Settings, ShieldCheck, TicketCheck, Users, X } from 'lucide-react'
import { useApp } from '../context/AppContext'
import { useNotifications } from './useNotifications'
import { NotificationPanel } from './NotificationPanel'

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
  const { toasts, removeToast, requests, session, logout, realtimeRevision } = useApp()
  const profileName = session?.name || ''
  const profileRole = session?.role || ''
  const initials = profileName.split(' ').map((part) => part[0]).slice(0, 2).join('').toUpperCase()
  const pending = requests.filter((request) => request.status === 'PENDIENTE').length

  useEffect(() => setSidebarOpen(false), [page])
  const navigate = (target: PageKey) => { window.location.hash = target; onNavigate(target) }
  const { notificationsOpen, setNotificationsOpen, notifications, unreadCount, notificationError, syncNotifications, openNotification, markAllRead } = useNotifications(session, realtimeRevision, navigate)

  return <div className="app-shell">
    <aside className={`sidebar ${sidebarOpen ? 'sidebar-open' : ''}`}>
      <div className="sidebar-top"><Logo /><button className="icon-button sidebar-close" onClick={() => setSidebarOpen(false)}><X size={20} /></button></div>
      <nav>{navigation.map((section) => <div className="nav-section" key={section.label}><span className="nav-label">{section.label}</span>{section.items.map((item) => <button key={item.key} className={`nav-item ${page === item.key ? 'active' : ''}`} onClick={() => navigate(item.key)}><item.icon size={19} /><span>{item.label}</span>{item.key === 'solicitudes' && pending > 0 && <b>{pending}</b>}</button>)}</div>)}</nav>
      <div className="system-status"><div><ShieldCheck size={18} /><span><strong>En línea</strong></span></div><i /></div>
      <button className="profile-card" onClick={logout} title="Cerrar sesión"><div className="avatar">{initials}</div><span><strong>{profileName}</strong><small>{profileRole} · salir</small></span><ChevronDown size={17} /></button>
    </aside>
    {sidebarOpen && <button className="sidebar-overlay" onClick={() => setSidebarOpen(false)} aria-label="Cerrar menú" />}
    <section className="main-column">
      <header className="topbar"><button className="icon-button menu-button" onClick={() => setSidebarOpen(true)}><Menu size={21} /></button><div className="topbar-context"><span>Plataforma</span><strong>LaVomVa</strong></div><button className="topbar-search topbar-search-button" onClick={() => navigate('tickets')}><Search size={17} /><span>Buscar tickets</span></button><div className="topbar-actions"><button aria-label={`Notificaciones${unreadCount ? `, ${unreadCount} sin leer` : ''}`} aria-expanded={notificationsOpen} className="icon-button notification-button" onClick={() => { setNotificationsOpen(!notificationsOpen); void syncNotifications() }}><Bell size={20} />{unreadCount > 0 && <b className="notification-badge">{unreadCount > 99 ? '99+' : unreadCount}</b>}</button><div className="topbar-avatar">{initials}</div></div>
        {notificationsOpen && <NotificationPanel notifications={notifications} unreadCount={unreadCount} error={notificationError} onOpen={(item) => void openNotification(item)} onMarkAllRead={() => void markAllRead()} />}
      </header>
      <main>{children}</main>
    </section>
    <div className="toast-stack">{toasts.map((toast) => <button key={toast.id} onClick={() => removeToast(toast.id)} className={`toast toast-${toast.tone}`}><i /><span><strong>{toast.title}</strong><small>{toast.description}</small></span><X size={16} /></button>)}</div>
  </div>
}

import { useEffect, useState, type ReactNode } from 'react'
import { Activity, Bell, Boxes, ChevronDown, ClipboardList, FileBarChart, Fuel, Gauge, LayoutDashboard, Menu, QrCode, Search, Settings, ShieldCheck, TicketCheck, Users, X } from 'lucide-react'
import { useApp } from '../context/AppContext'

export type PageKey = 'dashboard' | 'solicitudes' | 'tickets' | 'despacho' | 'inventario' | 'operaciones' | 'catalogos' | 'reportes' | 'administracion'

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
    { key: 'reportes', label: 'Reportes', icon: FileBarChart },
  ] },
  { label: 'Configuración', items: [
    { key: 'catalogos', label: 'Empleados y vehículos', icon: Users },
    { key: 'administracion', label: 'Administración', icon: Settings },
  ] },
]

function Logo() { return <div className="brand"><div className="brand-mark"><Fuel size={21} /></div><div><strong>LaVomVa</strong><span>Gestión de combustible</span></div></div> }

export function AppShell({ page, onNavigate, children }: { page: PageKey; onNavigate: (page: PageKey) => void; children: ReactNode }) {
  const [sidebarOpen, setSidebarOpen] = useState(false)
  const [notificationsOpen, setNotificationsOpen] = useState(false)
  const { toasts, removeToast, tanks, tickets, requests, session, logout } = useApp()
  const profileName = session?.name || ''
  const profileRole = session?.role || ''
  const initials = profileName.split(' ').map((part) => part[0]).slice(0, 2).join('').toUpperCase()
  const critical = tanks.filter((tank) => tank.stock <= tank.criticalLevel).length
  const expiring = tickets.filter((ticket) => ticket.status === 'PROXIMO_A_VENCER' || ticket.status === 'VENCIDO').length
  const pending = requests.filter((request) => request.status === 'PENDIENTE').length

  useEffect(() => setSidebarOpen(false), [page])
  const navigate = (target: PageKey) => { window.location.hash = target; onNavigate(target) }

  return <div className="app-shell">
    <aside className={`sidebar ${sidebarOpen ? 'sidebar-open' : ''}`}>
      <div className="sidebar-top"><Logo /><button className="icon-button sidebar-close" onClick={() => setSidebarOpen(false)}><X size={20} /></button></div>
      <nav>{navigation.map((section) => <div className="nav-section" key={section.label}><span className="nav-label">{section.label}</span>{section.items.map((item) => <button key={item.key} className={`nav-item ${page === item.key ? 'active' : ''}`} onClick={() => navigate(item.key)}><item.icon size={19} /><span>{item.label}</span>{item.key === 'solicitudes' && pending > 0 && <b>{pending}</b>}</button>)}</div>)}</nav>
      <div className="system-status"><div><ShieldCheck size={18} /><span><strong>Conectado a la API</strong><small>Datos de PostgreSQL</small></span></div><i /></div>
      <button className="profile-card" onClick={logout} title="Cerrar sesión"><div className="avatar">{initials}</div><span><strong>{profileName}</strong><small>{profileRole} · salir</small></span><ChevronDown size={17} /></button>
    </aside>
    {sidebarOpen && <button className="sidebar-overlay" onClick={() => setSidebarOpen(false)} aria-label="Cerrar menú" />}
    <section className="main-column">
      <header className="topbar"><button className="icon-button menu-button" onClick={() => setSidebarOpen(true)}><Menu size={21} /></button><div className="topbar-context"><span>Plataforma</span><strong>LaVomVa</strong></div><button className="topbar-search topbar-search-button" onClick={() => navigate('tickets')}><Search size={17} /><span>Buscar tickets</span></button><div className="topbar-actions"><button className="icon-button notification-button" onClick={() => setNotificationsOpen(!notificationsOpen)}><Bell size={20} />{critical + expiring > 0 && <i />}</button><div className="topbar-avatar">{initials}</div></div>
        {notificationsOpen && <div className="notifications-panel"><header><strong>Notificaciones</strong><span>{critical + expiring} nuevas</span></header>{critical > 0 && <button onClick={() => { navigate('inventario'); setNotificationsOpen(false) }}><span className="notice-icon notice-red"><Boxes size={18} /></span><span><strong>Inventario bajo</strong><small>{critical} tanque requiere atención</small></span></button>}{expiring > 0 && <button onClick={() => { navigate('tickets'); setNotificationsOpen(false) }}><span className="notice-icon notice-amber"><TicketCheck size={18} /></span><span><strong>Tickets por revisar</strong><small>{expiring} próximos a vencer o vencidos</small></span></button>}</div>}
      </header>
      <main>{children}</main>
    </section>
    <div className="toast-stack">{toasts.map((toast) => <button key={toast.id} onClick={() => removeToast(toast.id)} className={`toast toast-${toast.tone}`}><i /><span><strong>{toast.title}</strong><small>{toast.description}</small></span><X size={16} /></button>)}</div>
  </div>
}

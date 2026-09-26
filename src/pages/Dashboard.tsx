import { AlertTriangle, ArrowRight, Clock3, Droplets, Fuel, TicketCheck, TrendingDown, TrendingUp } from 'lucide-react'
import { useApp } from '../context/AppContext'
import type { PageKey } from '../components/AppShell'
import { formatDate, MetricCard, PageHeader, StatusBadge } from '../components/ui'

export function Dashboard({ onNavigate }: { onNavigate: (page: PageKey) => void }) {
  const { requests, tickets, tanks, movements, session } = useApp()
  const totalStock = tanks.reduce((sum, tank) => sum + tank.stock, 0)
  const totalCapacity = tanks.reduce((sum, tank) => sum + tank.capacity, 0)
  const pending = requests.filter((request) => request.status === 'PENDIENTE').length
  const active = tickets.filter((ticket) => ['CREADO', 'ENVIADO', 'PENDIENTE', 'PROXIMO_A_VENCER'].includes(ticket.status)).length
  const today = new Date().toLocaleDateString('en-CA')
  const dispatched = movements.filter((movement) => movement.type === 'SALIDA' && new Date(movement.date).toLocaleDateString('en-CA') === today).reduce((sum, movement) => sum + movement.gallons, 0)
  const critical = tanks.filter((tank) => tank.stock <= tank.criticalLevel)
  const days = Array.from({ length: 7 }, (_, index) => { const date = new Date(); date.setDate(date.getDate() - 6 + index); return date })
  const consumption = days.map((date) => movements.filter((movement) => movement.type === 'SALIDA' && new Date(movement.date).toDateString() === date.toDateString()).reduce((sum, movement) => sum + movement.gallons, 0))
  const labels = days.map((date) => date.toLocaleDateString('es-DO', { day: 'numeric', month: 'short' }))
  const peak = Math.max(1, ...consumption)

  return <div className="page dashboard-page">
    <PageHeader eyebrow={new Date().toLocaleDateString('es-DO', { weekday: 'long', day: 'numeric', month: 'long' })} title={`Hola, ${session?.name?.split(' ')[0] || 'equipo'}`} description="Este es el estado operativo de combustible en este momento." actions={<button className="primary-button" onClick={() => onNavigate('solicitudes')}>Revisar solicitudes <ArrowRight size={17} /></button>} />
    <section className="metrics-grid">
      <MetricCard label="Inventario total" value={`${totalStock.toLocaleString('es-DO')} gal`} detail={`${totalCapacity ? Math.round(totalStock / totalCapacity * 100) : 0}% de capacidad disponible`} icon={<Droplets size={22} />} />
      <MetricCard label="Despachado hoy" value={`${dispatched} gal`} detail="Según movimientos registrados" icon={<Fuel size={22} />} tone="blue" />
      <MetricCard label="Tickets activos" value={String(active)} detail="Consulta su vigencia en Tickets" icon={<TicketCheck size={22} />} tone="amber" />
      <MetricCard label="Solicitudes pendientes" value={String(pending)} detail="En espera de aprobación" icon={<Clock3 size={22} />} tone="red" />
    </section>

    <section className="dashboard-grid">
      <article className="panel chart-panel"><header className="panel-header"><div><h2>Consumo de combustible</h2><p>Salidas registradas en los últimos 7 días</p></div><span className="select-button">Últimos 7 días</span></header><div className="bar-chart">{consumption.map((value, index) => <div className="bar-column" key={labels[index]}><span className="bar-value">{value}</span><div className="bar-track"><div className={`bar-fill ${index === consumption.length - 1 ? 'current' : ''}`} style={{ height: `${value / peak * 100}%` }} /></div><small>{labels[index]}</small></div>)}</div><div className="chart-summary"><span><TrendingDown size={16} /> Movimientos del sistema</span><strong>{consumption.reduce((sum, value) => sum + value, 0)} galones</strong></div></article>
      <article className="panel stock-panel"><header className="panel-header"><div><h2>Nivel por tanque</h2><p>Existencia frente a capacidad</p></div><button className="text-button" onClick={() => onNavigate('inventario')}>Ver inventario <ArrowRight size={15} /></button></header><div className="tank-list">{tanks.map((tank) => { const percent = Math.round(tank.stock / tank.capacity * 100); const isCritical = tank.stock <= tank.criticalLevel; return <div className="tank-row" key={tank.id}><div className="tank-row-head"><span><strong>{tank.code}</strong><small>{tank.fuelType}</small></span><span><b className={isCritical ? 'text-red' : ''}>{tank.stock.toLocaleString()} gal</b><small>de {tank.capacity.toLocaleString()}</small></span></div><div className="progress"><i className={isCritical ? 'critical' : ''} style={{ width: `${percent}%` }} /></div><small>{percent}% disponible {isCritical && '· Nivel crítico'}</small></div> })}</div></article>
    </section>

    {critical.length > 0 && <button className="alert-banner" onClick={() => onNavigate('inventario')}><span className="alert-banner-icon"><AlertTriangle size={21} /></span><span><strong>Atención: inventario por debajo del nivel crítico</strong><small>{critical.map((tank) => `${tank.code} (${tank.stock} gal)`).join(', ')}. Programa una recepción para evitar interrupciones.</small></span><ArrowRight size={19} /></button>}

    <section className="dashboard-grid lower-grid">
      <article className="panel"><header className="panel-header"><div><h2>Solicitudes recientes</h2><p>Últimos movimientos del flujo de aprobación</p></div><button className="text-button" onClick={() => onNavigate('solicitudes')}>Ver todas <ArrowRight size={15} /></button></header><div className="compact-list">{requests.slice(0, 4).map((request) => <button key={request.id} onClick={() => onNavigate('solicitudes')}><span className="initials">{request.employee.split(' ').map((word) => word[0]).slice(0, 2).join('')}</span><span><strong>{request.employee}</strong><small>{request.vehicle.split(' · ')[0]} · {request.requestedGallons} gal</small></span><span><StatusBadge value={request.status} /><small>{formatDate(request.requestedAt, true)}</small></span></button>)}</div></article>
      <article className="panel"><header className="panel-header"><div><h2>Actividad reciente</h2><p>Inventario y despachos registrados</p></div><span className="live-pill"><i /> API CONECTADA</span></header><div className="timeline">{movements.slice(0, 4).map((movement) => { const out = movement.type === 'SALIDA' || movement.type === 'AJUSTE_NEGATIVO' || movement.type === 'MERMA'; return <div key={movement.id}><span className={`timeline-icon ${out ? 'out' : 'in'}`}>{out ? <TrendingDown size={16} /> : <TrendingUp size={16} />}</span><span><strong>{movement.type.replace('_', ' ')}</strong><small>{movement.reference} · {movement.user}</small></span><span><b>{out ? '-' : '+'}{movement.gallons} gal</b><small>{formatDate(movement.date, true)}</small></span></div> })}</div></article>
    </section>
  </div>
}

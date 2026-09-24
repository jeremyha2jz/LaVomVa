import { Download, FileSpreadsheet, Fuel, PieChart, TrendingUp } from 'lucide-react'
import { useMemo, useState } from 'react'
import { useApp } from '../context/AppContext'
import { downloadCsv, PageHeader } from '../components/ui'

export function Reports() {
  const { tickets, movements, catalogs } = useApp()
  const [from, setFrom] = useState('')
  const [to, setTo] = useState('')
  const [department, setDepartment] = useState('')
  const [fuel, setFuel] = useState('')
  const filtered = useMemo(() => tickets.filter((ticket) => (!from || ticket.createdAt.slice(0, 10) >= from) && (!to || ticket.createdAt.slice(0, 10) <= to) && (!department || ticket.department === department) && (!fuel || ticket.fuelType === fuel)), [tickets, from, to, department, fuel])
  const totalAuthorized = filtered.reduce((sum, ticket) => sum + ticket.gallons, 0)
  const consumed = filtered.filter((ticket) => ticket.status === 'CONSUMIDO').length
  const byDepartment = Object.entries(filtered.reduce<Record<string, number>>((totals, ticket) => { totals[ticket.department] = (totals[ticket.department] || 0) + ticket.gallons; return totals }, {})).sort((a, b) => b[1] - a[1])
  const max = Math.max(1, ...byDepartment.map(([, value]) => value))
  const byFuel = Object.entries(filtered.reduce<Record<string, number>>((totals, ticket) => { totals[ticket.fuelType] = (totals[ticket.fuelType] || 0) + ticket.gallons; return totals }, {})).sort((a, b) => b[1] - a[1])
  const maxFuel = Math.max(1, ...byFuel.map(([, value]) => value))
  const actualDispatched = movements.filter((movement) => movement.type === 'SALIDA' && (!from || movement.date.slice(0, 10) >= from) && (!to || movement.date.slice(0, 10) <= to)).reduce((sum, movement) => sum + movement.gallons, 0)
  const exportTickets = () => downloadCsv('lavomva-tickets.csv', [['Ticket', 'Fecha', 'Empleado', 'Vehículo', 'Departamento', 'Combustible', 'Galones autorizados', 'Estado'], ...filtered.map((ticket) => [ticket.sequence, ticket.createdAt, ticket.employee, ticket.vehicle, ticket.department, ticket.fuelType, ticket.gallons, ticket.status])])

  return <div className="page"><PageHeader eyebrow="Análisis" title="Reportes gerenciales" description="Filtra tickets y exporta los resultados visibles a CSV." actions={<button className="primary-button" onClick={exportTickets}><Download size={17} /> Exportar CSV</button>} />
    <section className="report-filters panel"><label>Desde<input type="date" value={from} onChange={(event) => setFrom(event.target.value)} /></label><label>Hasta<input type="date" value={to} min={from || undefined} onChange={(event) => setTo(event.target.value)} /></label><label>Departamento<select value={department} onChange={(event) => setDepartment(event.target.value)}><option value="">Todos</option>{catalogs.departments.map((item) => <option key={item.id} value={item.name}>{item.name}</option>)}</select></label><label>Combustible<select value={fuel} onChange={(event) => setFuel(event.target.value)}><option value="">Todos</option>{catalogs.fuelTypes.map((item) => <option key={item.id} value={item.name}>{item.name}</option>)}</select></label></section>
    <section className="metrics-grid metrics-3"><article className="report-kpi"><span><Fuel size={20} /></span><div><small>Galones autorizados</small><strong>{totalAuthorized.toLocaleString()} gal</strong><em>Tickets del filtro actual</em></div></article><article className="report-kpi"><span><TrendingUp size={20} /></span><div><small>Despacho registrado</small><strong>{actualDispatched.toLocaleString()} gal</strong><em>Salidas de inventario por fecha</em></div></article><article className="report-kpi"><span><FileSpreadsheet size={20} /></span><div><small>Tickets procesados</small><strong>{consumed} / {filtered.length}</strong><em>Consumidos / total filtrado</em></div></article></section>
    <section className="dashboard-grid"><article className="panel report-chart"><header className="panel-header"><div><h2>Autorizado por departamento</h2><p>Galones de tickets filtrados</p></div><PieChart size={20} /></header><div className="horizontal-chart">{byDepartment.length ? byDepartment.map(([name, value]) => <div key={name}><span>{name}</span><div><i style={{ width: `${value / max * 100}%` }} /></div><strong>{value.toLocaleString()} gal</strong></div>) : <p>Sin tickets para estos filtros.</p>}</div></article><article className="panel report-chart"><header className="panel-header"><div><h2>Autorizado por combustible</h2><p>Galones de tickets filtrados</p></div></header><div className="horizontal-chart">{byFuel.length ? byFuel.map(([name, value]) => <div key={name}><span>{name}</span><div><i style={{ width: `${value / maxFuel * 100}%` }} /></div><strong>{value.toLocaleString()} gal</strong></div>) : <p>Sin tickets para estos filtros.</p>}</div></article></section>
  </div>
}

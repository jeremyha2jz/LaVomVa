import { Download, FileSpreadsheet, FileText, Fuel, TrendingUp } from 'lucide-react'
import { useEffect, useState } from 'react'
import { downloadBlob, EmptyState, PageHeader } from '../components/ui'
import { exportReport, getReport, type ReportFilters, type ReportFormat, type ReportResult } from '../services/api'
import { initialReportDraft, ReportFilters as ReportFiltersPanel } from './ReportFilters'

const PAGE_SIZE = 50
const formatNumber = (value: number) => Number(value || 0).toLocaleString('es-DO', { maximumFractionDigits: 2 })

export function Reports() {
  const [draft, setDraft] = useState(initialReportDraft)
  const [filters, setFilters] = useState<ReportFilters>({ tipo: 'consumo', pagina: 1, tamanoPagina: PAGE_SIZE })
  const [page, setPage] = useState(1)
  const [report, setReport] = useState<ReportResult | null>(null)
  const [loading, setLoading] = useState(true)
  const [exporting, setExporting] = useState<ReportFormat | null>(null)
  const [error, setError] = useState('')

  useEffect(() => {
    let active = true
    setLoading(true)
    setError('')
    void getReport({ ...filters, pagina: page, tamanoPagina: PAGE_SIZE }).then((result) => {
      if (active) setReport(result)
    }).catch((reason: unknown) => {
      if (active) setError(reason instanceof Error ? reason.message : 'No se pudo cargar el reporte.')
    }).finally(() => { if (active) setLoading(false) })
    return () => { active = false }
  }, [filters, page])

  const applyFilters = () => {
    const applied: ReportFilters = { tipo: draft.tipo, pagina: 1, tamanoPagina: PAGE_SIZE }
    for (const key of ['desde', 'hasta', 'estado'] as const) if (draft[key]) applied[key] = draft[key]
    for (const key of ['departamentoId', 'combustibleId', 'empleadoId', 'vehiculoId', 'estacionId'] as const) {
      if (draft[key]) applied[key] = Number(draft[key])
    }
    setPage(1)
    setFilters(applied)
  }

  const clearFilters = () => {
    setDraft({ ...initialReportDraft })
    setPage(1)
    setFilters({ tipo: 'consumo', pagina: 1, tamanoPagina: PAGE_SIZE })
  }

  const download = async (format: ReportFormat) => {
    setExporting(format)
    setError('')
    try {
      const { blob, filename } = await exportReport({ ...filters, pagina: undefined, tamanoPagina: undefined }, format)
      downloadBlob(filename, blob)
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'No se pudo exportar el reporte.')
    } finally { setExporting(null) }
  }

  const title = filters.tipo === 'consumo' || filters.tipo === 'despachos' ? 'Galones despachados' : filters.tipo === 'tickets' ? 'Galones autorizados' : 'Galones movidos'
  const groups = report?.porDepartamento ?? []
  const fuelGroups = report?.porCombustible ?? []
  const max = Math.max(1, ...groups.map((x) => x.galones))
  const maxFuel = Math.max(1, ...fuelGroups.map((x) => x.galones))
  const totalPages = Math.max(1, Math.ceil((report?.totalRegistros ?? 0) / PAGE_SIZE))
  const percent = (value: number, maximum: number) => String(value / maximum * 100) + '%'

  return <div className="page">
    <PageHeader eyebrow="Análisis" title="Reportes gerenciales" description="Consulta datos consolidados y expórtalos con los mismos filtros." actions={<div className="header-actions"><button className="secondary-button" disabled={loading || exporting !== null} onClick={() => void download('csv')}><Download size={16} /> CSV</button><button className="secondary-button" disabled={loading || exporting !== null} onClick={() => void download('xlsx')}><FileSpreadsheet size={16} /> Excel</button><button className="primary-button" disabled={loading || exporting !== null} onClick={() => void download('pdf')}><FileText size={16} /> PDF</button></div>} />
    <ReportFiltersPanel draft={draft} setDraft={setDraft} loading={loading} onApply={applyFilters} onClear={clearFilters} />
    {error && <div className="error-banner" role="alert">{error}</div>}
    <p className="muted report-range">{report?.rangoUtc ?? 'Fechas operacionales UTC, inicio y fin inclusivos.'}</p>
    <section className="metrics-grid metrics-3">
      <article className="report-kpi"><span><Fuel size={20} /></span><div><small>{title}</small><strong>{formatNumber(report?.totales.galones ?? 0)} gal</strong><em>{report?.totales.registros ?? 0} registros del filtro</em></div></article>
      <article className="report-kpi"><span><TrendingUp size={20} /></span><div><small>Despachos registrados</small><strong>{formatNumber(report?.totales.despachos ?? 0)}</strong><em>{formatNumber(report?.totales.tickets ?? 0)} tickets asociados</em></div></article>
      <article className="report-kpi"><span><FileSpreadsheet size={20} /></span><div><small>Solicitudes / inventario</small><strong>{formatNumber(report?.totales.solicitudes ?? 0)}</strong><em>{formatNumber(report?.totales.inventarioActualGalones ?? 0)} gal en inventario actual</em></div></article>
    </section>
    <section className="dashboard-grid">
      <article className="panel report-chart"><header className="panel-header"><div><h2>{title} por departamento</h2></div></header><div className="horizontal-chart">{groups.length ? groups.map((group) => <div key={group.nombre}><span>{group.nombre}</span><div><i style={{ width: percent(group.galones, max) }} /></div><strong>{formatNumber(group.galones)} gal</strong></div>) : <p>Sin tickets para estos filtros.</p>}</div></article>
      <article className="panel report-chart"><header className="panel-header"><div><h2>{title} por combustible</h2></div></header><div className="horizontal-chart">{fuelGroups.length ? fuelGroups.map((group) => <div key={group.nombre}><span>{group.nombre}</span><div><i style={{ width: percent(group.galones, maxFuel) }} /></div><strong>{formatNumber(group.galones)} gal</strong></div>) : <p>Sin tickets para estos filtros.</p>}</div></article>
    </section>
    <section className="panel report-details">
      <header className="table-toolbar"><div><h2>Detalle</h2><p>{report?.totalRegistros ?? 0} filas; exportaciones incluyen todos los resultados filtrados.</p></div><span>{loading ? 'Cargando…' : 'Página ' + page + ' de ' + totalPages}</span></header>
      {loading ? <p role="status">Cargando reporte…</p> : !report?.items.length ? <EmptyState title="Sin resultados" description="No hay datos para los filtros seleccionados." /> : <div className="table-scroll"><table><thead><tr><th>Fecha UTC</th><th>Tipo</th><th>Ticket</th><th>Empleado</th><th>Vehículo</th><th>Departamento</th><th>Combustible</th><th>Estación / tanque</th><th>Estado</th><th>Galones</th></tr></thead><tbody>{report.items.map((row) => <tr key={row.tipo + '-' + row.id}><td>{row.fechaUtc.replace('T', ' ').slice(0, 19)}</td><td>{row.tipo}</td><td>{row.ticket ?? '—'}</td><td>{row.empleado ?? '—'}</td><td>{row.vehiculo ?? '—'}</td><td>{row.departamento ?? '—'}</td><td>{row.combustible ?? '—'}</td><td>{[row.estacion, row.tanque].filter(Boolean).join(' / ') || '—'}</td><td>{row.estado ?? '—'}</td><td>{formatNumber(row.galones)}</td></tr>)}</tbody></table></div>}
      <div className="report-pagination"><button className="secondary-button" disabled={loading || page <= 1} onClick={() => setPage((value) => value - 1)}>Anterior</button><span>{report ? (report.totalRegistros === 0 ? '0 resultados' : (Math.min((page - 1) * PAGE_SIZE + 1, report.totalRegistros) + '–' + Math.min(page * PAGE_SIZE, report.totalRegistros) + ' de ' + report.totalRegistros)) : '—'}</span><button className="secondary-button" disabled={loading || page >= totalPages} onClick={() => setPage((value) => value + 1)}>Siguiente</button></div>
    </section>
  </div>
}

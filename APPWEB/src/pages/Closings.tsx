import { useEffect, useMemo, useState } from 'react'
import { CalendarDays, Download, FileCheck2, RefreshCw } from 'lucide-react'
import { Modal, PageHeader } from '../components/ui'
import { useApp } from '../context/AppContext'
import { api, downloadCierrePdf, type CierreRow, type CierreSummary } from '../services/api'

const dateUtc = () => new Date().toISOString().slice(0, 10)
const gallons = (value: number) => `${value.toLocaleString('es-DO', { minimumFractionDigits: 2, maximumFractionDigits: 2 })} gal`
const setPreview = (result: Awaited<ReturnType<typeof api.closeSummary>>, setSummary: (value: CierreSummary) => void, setClosed: (value: boolean) => void, setPhysical: (value: Record<number, string>) => void) => {
  setSummary(result.resumen)
  setClosed(result.cerrado)
  const savedPhysical = new Map(result.cierre?.detalleTanques.map((tank) => [tank.tanqueId, tank.inventarioFisicoGalones]) ?? [])
  setPhysical(Object.fromEntries(result.resumen.tanques.map((tank) => [tank.tanqueId, String(savedPhysical.get(tank.tanqueId) ?? tank.inventarioTeoricoFinalGalones)])))
}

export function Closings() {
  const { catalogs, session, notify } = useApp()
  const canCreate = ['ADMINISTRADOR', 'SUPERVISOR', 'DESPACHADOR'].includes(session?.role || '')
  const [stationId, setStationId] = useState('')
  const [date, setDate] = useState(dateUtc)
  const [summary, setSummary] = useState<CierreSummary | null>(null)
  const [closed, setClosed] = useState(false)
  const [physical, setPhysical] = useState<Record<number, string>>({})
  const [observations, setObservations] = useState('')
  const [history, setHistory] = useState<CierreRow[]>([])
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(false)
  const [saving, setSaving] = useState(false)
  const [confirmOpen, setConfirmOpen] = useState(false)

  useEffect(() => {
    if (!stationId && catalogs.stations.length) setStationId(String(catalogs.stations[0].id))
  }, [catalogs.stations, stationId])

  const loadHistory = async () => {
    try { setHistory(await api.dailyCloses({ estacionId: stationId ? Number(stationId) : undefined })) }
    catch (cause) { setError(cause instanceof Error ? cause.message : 'No se pudo cargar el historial.') }
  }

  useEffect(() => { void loadHistory() }, [stationId])

  useEffect(() => {
    if (!stationId || !date) { setSummary(null); return }
    let live = true
    setLoading(true)
    setError('')
    api.closeSummary(Number(stationId), date).then((result) => {
      if (!live) return
      setPreview(result, setSummary, setClosed, setPhysical)
    }).catch((cause: unknown) => {
      if (live) { setSummary(null); setError(cause instanceof Error ? cause.message : 'No se pudo calcular el resumen.') }
    }).finally(() => { if (live) setLoading(false) })
    return () => { live = false }
  }, [stationId, date])

  const totalPhysical = useMemo(() => summary?.tanques.reduce((sum, tank) => sum + Number(physical[tank.tanqueId] || 0), 0) ?? 0, [summary, physical])
  const previewDifference = totalPhysical - (summary?.inventarioTeoricoFinalGalones ?? 0)

  async function confirmClose() {
    if (!summary) return
    setSaving(true)
    setError('')
    try {
      await api.createDailyClose({
        estacionId: summary.estacionId,
        fecha: summary.fecha,
        inventariosFisicos: summary.tanques.map((tank) => ({ tanqueId: tank.tanqueId, inventarioFisicoGalones: physical[tank.tanqueId] === '' ? null : Number(physical[tank.tanqueId]) })),
        observaciones: observations,
      })
      setConfirmOpen(false)
      setObservations('')
      notify('Cierre diario guardado', 'El acta quedó cerrada e inmutable.', 'success')
      await Promise.all([loadHistory(), api.closeSummary(summary.estacionId, summary.fecha).then((result) => setPreview(result, setSummary, setClosed, setPhysical))])
    } catch (cause) {
      const message = cause instanceof Error ? cause.message : 'No se pudo guardar el cierre.'
      setError(message)
      notify('No se pudo cerrar el día', message, 'error')
    } finally { setSaving(false) }
  }

  async function download(id: number) {
    try {
      const blob = await downloadCierrePdf(id)
      const url = URL.createObjectURL(blob)
      const anchor = document.createElement('a')
      anchor.href = url
      anchor.download = `acta-cierre-${id}.pdf`
      anchor.click()
      URL.revokeObjectURL(url)
    } catch (cause) {
      const message = cause instanceof Error ? cause.message : 'No se pudo descargar el acta.'
      setError(message)
      notify('No se pudo descargar el PDF', message, 'error')
    }
  }

  return <div className="page">
    <PageHeader eyebrow="Control diario" title="Cierre diario" description="Concilia el libro de movimientos con el conteo físico y genera el acta del día." />
    <section className="panel closing-panel">
      <div className="closing-filters">
        <label>Estación<select aria-label="Estación" value={stationId} onChange={(event) => setStationId(event.target.value)}><option value="">Seleccionar estación</option>{catalogs.stations.map((station) => <option key={station.id} value={station.id}>{station.name}</option>)}</select></label>
        <label>Fecha operacional (UTC)<input aria-label="Fecha operacional" type="date" value={date} max={dateUtc()} onChange={(event) => setDate(event.target.value)} /></label>
        <button className="secondary-button" type="button" onClick={() => { if (stationId && date) { setLoading(true); api.closeSummary(Number(stationId), date).then((result) => { setPreview(result, setSummary, setClosed, setPhysical); setError('') }).catch((cause: unknown) => setError(cause instanceof Error ? cause.message : 'Error de la API.')).finally(() => setLoading(false)) } }}><RefreshCw size={16} /> Actualizar</button>
      </div>
      {error && <div className="error-banner" role="alert">{error}</div>}
      {loading && <p role="status">Calculando movimientos persistidos…</p>}
      {!loading && summary && <>
        <header className="closing-summary-head"><div><span className="eyebrow">{summary.estacion} · {summary.fecha} UTC</span><h2>Resumen del día</h2></div><span className={`status-chip ${closed ? 'is-closed' : ''}`}>{closed ? 'CERRADO' : 'PENDIENTE DE CIERRE'}</span></header>
        <div className="closing-metrics"><article><small>Inventario inicial</small><strong>{gallons(summary.inventarioInicialGalones)}</strong></article><article><small>Entradas</small><strong>{gallons(summary.volumenRecibidoGalones)}</strong></article><article><small>Despachado · {summary.cantidadDespachos} tickets</small><strong>{gallons(summary.volumenDespachadoGalones)}</strong></article><article><small>Ajustes netos</small><strong>{gallons(summary.ajustesGalones)}</strong></article><article><small>Mermas</small><strong>{gallons(summary.mermasGalones)}</strong></article><article><small>Inventario teórico final</small><strong>{gallons(summary.inventarioTeoricoFinalGalones)}</strong></article></div>
        <div className="table-scroll"><table><thead><tr><th>Tanque</th><th>Inicial</th><th>Entradas</th><th>Despachado</th><th>Otras salidas</th><th>Merma</th><th>Ajuste</th><th>Teórico</th><th>Físico contado</th></tr></thead><tbody>{summary.tanques.map((tank) => <tr key={tank.tanqueId}><td><strong>{tank.codigo}</strong><small>{tank.nombre} · capacidad {gallons(tank.capacidadGalones)}</small></td><td>{gallons(tank.inventarioInicialGalones)}</td><td>{gallons(tank.entradasGalones)}</td><td>{gallons(tank.despachadoGalones)}</td><td>{gallons(tank.otrasSalidasGalones)}</td><td>{gallons(tank.mermasGalones)}</td><td>{gallons(tank.ajustesGalones)}</td><td><strong>{gallons(tank.inventarioTeoricoFinalGalones)}</strong></td><td><input aria-label={`Inventario físico ${tank.codigo}`} type="number" min="0" max={tank.capacidadGalones} step="0.01" value={closed ? String(physical[tank.tanqueId] ?? '') : physical[tank.tanqueId] ?? ''} disabled={!canCreate || closed} onChange={(event) => setPhysical((current) => ({ ...current, [tank.tanqueId]: event.target.value }))} /></td></tr>)}</tbody></table></div>
        <div className="closing-difference"><span>Diferencia preliminar (físico − teórico)</span><strong className={previewDifference < 0 ? 'text-red' : ''}>{gallons(previewDifference)}{previewDifference < 0 ? ' · faltante' : previewDifference > 0 ? ' · sobrante' : ' · conciliado'}</strong></div>
        {canCreate && !closed && <div className="closing-confirm-area"><label>Observaciones del acta (opcional)<textarea maxLength={2000} rows={2} value={observations} onChange={(event) => setObservations(event.target.value)} /></label><button className="primary-button" disabled={!summary.tanques.every((tank) => physical[tank.tanqueId] !== '' && Number.isFinite(Number(physical[tank.tanqueId])))} onClick={() => setConfirmOpen(true)}><FileCheck2 size={17} /> Confirmar cierre definitivo</button></div>}
      </>}
    </section>

    <section className="panel table-panel closing-history"><div className="panel-header padded"><div><h2>Historial de cierres</h2><p>Actas definitivas guardadas en la base de datos</p></div></div><div className="table-scroll"><table><thead><tr><th>Fecha UTC</th><th>Estación</th><th>Despachos</th><th>Teórico</th><th>Físico</th><th>Diferencia</th><th>Estado</th><th>Acta</th></tr></thead><tbody>{history.map(({ cierre, estacion }) => <tr key={cierre.id}><td>{cierre.fecha.slice(0, 10)}</td><td>{estacion}</td><td>{cierre.cantidadDespachos}</td><td>{gallons(cierre.inventarioFinalGalones)}</td><td>{gallons(cierre.inventarioFisicoGalones)}</td><td>{gallons(cierre.diferenciaGalones)}</td><td><span className="status-chip is-closed">{cierre.estado}</span></td><td><button className="icon-button" aria-label={`Descargar acta ${cierre.id}`} onClick={() => void download(cierre.id)}><Download size={17} /></button></td></tr>)}{history.length === 0 && <tr><td colSpan={8}><div className="empty-state"><span className="empty-icon"><CalendarDays size={20} /></span><h3>Sin cierres todavía</h3><p>Los cierres guardados aparecerán aquí.</p></div></td></tr>}</tbody></table></div></section>

    {confirmOpen && summary && <Modal title="Confirmar cierre definitivo" subtitle="El acta no podrá editarse después de guardar." onClose={() => setConfirmOpen(false)}><p>Se cerrará {summary.estacion} para el día {summary.fecha} UTC. La diferencia física − teórica es <strong>{gallons(previewDifference)}</strong>.</p><div className="form-actions"><button className="secondary-button" onClick={() => setConfirmOpen(false)} disabled={saving}>Volver al conteo</button><button className="primary-button" disabled={saving} onClick={() => void confirmClose()}>{saving ? 'Guardando…' : 'Cerrar día'}</button></div></Modal>}
  </div>
}

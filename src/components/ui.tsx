import { Search, X } from 'lucide-react'
import type { ReactNode } from 'react'

export function PageHeader({ eyebrow, title, description, actions }: { eyebrow?: string; title: string; description: string; actions?: ReactNode }) {
  return <div className="page-header"><div>{eyebrow && <span className="eyebrow">{eyebrow}</span>}<h1>{title}</h1><p>{description}</p></div>{actions && <div className="header-actions">{actions}</div>}</div>
}

export function StatusBadge({ value }: { value: string }) {
  const tone = ['APROBADA', 'CONSUMIDO', 'ACTIVO', 'NORMAL', 'ENVIADO'].includes(value) ? 'success' : ['PENDIENTE', 'CREADO', 'PROXIMO_A_VENCER'].includes(value) ? 'warning' : ['RECHAZADA', 'VENCIDO', 'ANULADO', 'CRITICO', 'INACTIVO'].includes(value) ? 'danger' : 'neutral'
  return <span className={`badge badge-${tone}`}><i />{value.replaceAll('_', ' ')}</span>
}

export function SearchBox({ value, onChange, placeholder = 'Buscar…' }: { value: string; onChange: (value: string) => void; placeholder?: string }) {
  return <label className="search-box"><Search size={17} /><input value={value} onChange={(event) => onChange(event.target.value)} placeholder={placeholder} /></label>
}

export function EmptyState({ title, description }: { title: string; description: string }) {
  return <div className="empty-state"><div className="empty-icon">⌁</div><h3>{title}</h3><p>{description}</p></div>
}

export function Modal({ title, subtitle, children, onClose, size = 'md' }: { title: string; subtitle?: string; children: ReactNode; onClose: () => void; size?: 'md' | 'lg' }) {
  return <div className="modal-backdrop" onMouseDown={(event) => event.currentTarget === event.target && onClose()}><section className={`modal modal-${size}`} role="dialog" aria-modal="true"><header><div><h2>{title}</h2>{subtitle && <p>{subtitle}</p>}</div><button className="icon-button" onClick={onClose} aria-label="Cerrar"><X size={20} /></button></header><div className="modal-body">{children}</div></section></div>
}

export function MetricCard({ label, value, detail, icon, tone = 'green' }: { label: string; value: string; detail: string; icon: ReactNode; tone?: 'green' | 'amber' | 'blue' | 'red' }) {
  return <article className="metric-card"><div className={`metric-icon metric-${tone}`}>{icon}</div><div><span>{label}</span><strong>{value}</strong><small>{detail}</small></div></article>
}

export function formatDate(value: string, withTime = false) {
  return new Intl.DateTimeFormat('es-DO', { day: '2-digit', month: 'short', year: 'numeric', ...(withTime ? { hour: '2-digit', minute: '2-digit' } : {}) }).format(new Date(value))
}

export function downloadCsv(filename: string, rows: (string | number)[][]) {
  const content = rows.map((row) => row.map((cell) => `"${String(cell).replaceAll('"', '""')}"`).join(',')).join('\n')
  const url = URL.createObjectURL(new Blob([`\uFEFF${content}`], { type: 'text/csv;charset=utf-8' }))
  const anchor = document.createElement('a'); anchor.href = url; anchor.download = filename; anchor.click(); URL.revokeObjectURL(url)
}

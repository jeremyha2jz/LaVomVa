import { ShieldCheck } from 'lucide-react'
import { PageHeader } from '../components/ui'

export function Dispatch() {
  return <div className="page dispatch-page"><PageHeader eyebrow="Punto de despacho" title="Despacho por QR" description="El despacho se registra en el punto de suministro después de validar el QR." /><section className="panel" style={{ padding: 32 }}><ShieldCheck size={36} /><h2>Validación en el punto de suministro</h2><p>Usa el escáner autorizado para registrar un despacho. El número visible del ticket no sustituye su QR.</p></section></div>
}

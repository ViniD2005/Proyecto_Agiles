import { ESTADOS_NOTA, NIVELES } from './formato'
import '../styles-sprint2.css'

export function NivelBadge({ nivel }) {
  const n = NIVELES[nivel] || NIVELES.SinDatos
  return <span className={`nivel ${n.clase}`}>{n.texto}</span>
}

export function EstadoNotaBadge({ estado }) {
  const e = ESTADOS_NOTA[estado] || { texto: estado, clase: 'sinentrega' }
  return <span className={`estado-nota ${e.clase}`}>{e.texto}</span>
}

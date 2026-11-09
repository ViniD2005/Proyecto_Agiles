import { useEffect, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { materiasApi } from '../api/client'

/** Lee la materia de la URL (?materia=ID); si no hay, elige la primera del docente. */
export function useMateriaSeleccionada() {
  const [params, setParams] = useSearchParams()
  const [materias, setMaterias] = useState(null)
  const [error, setError] = useState(null)

  useEffect(() => {
    materiasApi.listar().then(setMaterias).catch(e => setError(e.message))
  }, [])

  const id = Number(params.get('materia')) || null
  const materia = materias?.find(m => m.id === id) ?? null

  useEffect(() => {
    if (materias?.length && !materia) setParams({ materia: String(materias[0].id) }, { replace: true })
  }, [materias, materia, setParams])

  return { materias, materia, error, seleccionar: (nuevo) => setParams({ materia: String(nuevo) }) }
}

export default function SelectorMateria({ materias, materia, onChange }) {
  if (!materias) return <p className="muted">Cargando materias…</p>
  if (materias.length === 0)
    return <div className="alerta error">No tienes materias sincronizadas. Ve a «Mis materias» y presiona «Sincronizar con Moodle».</div>
  return (
    <label className="selector-materia">
      <span>Materia</span>
      <select value={materia?.id ?? ''} onChange={e => onChange(Number(e.target.value))}>
        {materias.map(m => <option key={m.id} value={m.id}>{m.nombre}</option>)}
      </select>
    </label>
  )
}

import { useCallback, useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { materiasApi } from '../api/client'
import Etiqueta from '../components/Etiqueta'
import Paginacion from '../components/Paginacion'

const TIPOS = [
  { valor: '', texto: 'Todas' },
  { valor: 'Evaluacion', texto: 'Evaluaciones (40 %)' },
  { valor: 'APE', texto: 'APE (30 %)' },
  { valor: 'TrabajoAutonomo', texto: 'Trabajo autónomo (30 %)' }
]
const PAGE_SIZE = 8

/** Mockup M3 - Estudiantes y actividades de la materia (HU-06). */
export default function MateriaDetalle() {
  const { id } = useParams()
  const [materia, setMateria] = useState(null)
  const [error, setError] = useState(null)

  const [buscar, setBuscar] = useState('')
  const [page, setPage] = useState(1)
  const [estudiantes, setEstudiantes] = useState({ items: [], total: 0 })

  const [tipo, setTipo] = useState('')
  const [actividades, setActividades] = useState([])

  useEffect(() => {
    materiasApi.obtener(id).then(setMateria).catch(e =>
      setError(e.status === 403 ? 'No tienes permiso para ver esta materia (pertenece a otro docente).' :
               e.status === 404 ? 'La materia no existe.' : e.message))
  }, [id])

  const cargarEstudiantes = useCallback(() => {
    materiasApi.estudiantes(id, { buscar, page, pageSize: PAGE_SIZE }).then(setEstudiantes).catch(() => {})
  }, [id, buscar, page])

  useEffect(() => {
    const t = setTimeout(cargarEstudiantes, 250) // pequeña espera mientras se escribe
    return () => clearTimeout(t)
  }, [cargarEstudiantes])

  useEffect(() => {
    materiasApi.actividades(id, { tipo }).then(r => setActividades(r.items)).catch(() => {})
  }, [id, tipo])

  if (error) return <div className="alerta error">{error} <Link to="/materias">Volver</Link></div>
  if (!materia) return <p className="muted">Cargando…</p>

  return (
    <>
      <p className="migas"><Link to="/materias">Mis materias</Link> / {materia.nombre}</p>
      <div className="encabezado">
        <div>
          <h1>{materia.nombre} · 1.er parcial</h1>
          <p className="muted">Estudiantes y actividades leídos desde Moodle (solo lectura)</p>
        </div>
      </div>

      <div className="dos-columnas">
        <section className="panel">
          <div className="panel-cab">
            <h3>Estudiantes ({estudiantes.total})</h3>
            <input className="buscador" placeholder="🔍 Buscar por nombre…" value={buscar}
                   onChange={e => { setBuscar(e.target.value); setPage(1) }} />
          </div>
          <table>
            <thead><tr><th>Estudiante</th><th>Correo</th><th>Estado</th></tr></thead>
            <tbody>
              {estudiantes.items.map(e => (
                <tr key={e.id}>
                  <td>{e.apellidos} {e.nombres}</td>
                  <td className="muted">{e.email || '—'}</td>
                  <td><Etiqueta valor={e.estado} /></td>
                </tr>
              ))}
              {estudiantes.items.length === 0 && <tr><td colSpan={3} className="muted">Sin resultados.</td></tr>}
            </tbody>
          </table>
          <Paginacion page={page} pageSize={PAGE_SIZE} total={estudiantes.total} onChange={setPage} />
        </section>

        <section className="panel">
          <div className="panel-cab">
            <h3>Actividades del parcial ({actividades.length})</h3>
            <select value={tipo} onChange={e => setTipo(e.target.value)}>
              {TIPOS.map(t => <option key={t.valor} value={t.valor}>{t.texto}</option>)}
            </select>
          </div>
          <table>
            <thead><tr><th>Actividad</th><th>Tipo</th><th className="num">Peso</th></tr></thead>
            <tbody>
              {actividades.map(a => (
                <tr key={a.id}>
                  <td>{a.nombre}</td>
                  <td><Etiqueta valor={a.tipo} /></td>
                  <td className="num">{a.peso.toLocaleString('es-EC')} %</td>
                </tr>
              ))}
              {actividades.length === 0 && <tr><td colSpan={3} className="muted">Sin actividades.</td></tr>}
            </tbody>
          </table>
        </section>
      </div>
      <p className="muted pie">Ponderación del parcial: 40 % evaluaciones · 30 % APE · 30 % trabajo autónomo (RN-02).</p>
    </>
  )
}

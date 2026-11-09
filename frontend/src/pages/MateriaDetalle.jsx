import { useCallback, useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { materiasApi } from '../api/client'
import { calificacionesApi } from '../api/sprint2'
import { ESTADOS_NOTA, fechaHora, fmt } from '../components/formato'
import Etiqueta from '../components/Etiqueta'
import Paginacion from '../components/Paginacion'
import '../styles-sprint2.css'

const TIPOS = [
  { valor: '', texto: 'Todas' },
  { valor: 'Evaluacion', texto: 'Evaluaciones (40 %)' },
  { valor: 'APE', texto: 'APE (30 %)' },
  { valor: 'TrabajoAutonomo', texto: 'Trabajo autónomo (30 %)' }
]
const PAGE_SIZE = 8

/**
 * Mockup M3 - Estudiantes y actividades de la materia (HU-06).
 * Sprint 2 (HU-07): botón «Sincronizar» con la última sincronización y sus errores, y calificaciones con su estado.
 */
export default function MateriaDetalle() {
  const { id } = useParams()
  const [materia, setMateria] = useState(null)
  const [error, setError] = useState(null)

  const [buscar, setBuscar] = useState('')
  const [page, setPage] = useState(1)
  const [estudiantes, setEstudiantes] = useState({ items: [], total: 0 })

  const [tipo, setTipo] = useState('')
  const [actividades, setActividades] = useState([])

  const [sincronizando, setSincronizando] = useState(false)
  const [resultado, setResultado] = useState(null)
  const [notas, setNotas] = useState(null)
  const [pageNotas, setPageNotas] = useState(1)

  const cargarMateria = useCallback(() => {
    materiasApi.obtener(id).then(setMateria).catch(e =>
      setError(e.status === 403 ? 'No tienes permiso para ver esta materia (pertenece a otro docente).' :
               e.status === 404 ? 'La materia no existe.' : e.message))
  }, [id])

  const cargarEstudiantes = useCallback(() => {
    materiasApi.estudiantes(id, { buscar, page, pageSize: PAGE_SIZE }).then(setEstudiantes).catch(() => {})
  }, [id, buscar, page])

  const cargarActividades = useCallback(() => {
    materiasApi.actividades(id, { tipo }).then(r => setActividades(r.items)).catch(() => {})
  }, [id, tipo])

  const cargarNotas = useCallback(() => {
    calificacionesApi.listar(id, { page: pageNotas, pageSize: PAGE_SIZE }).then(setNotas).catch(() => setNotas(null))
  }, [id, pageNotas])

  useEffect(() => { cargarMateria() }, [cargarMateria])
  useEffect(() => { const t = setTimeout(cargarEstudiantes, 250); return () => clearTimeout(t) }, [cargarEstudiantes])
  useEffect(() => { cargarActividades() }, [cargarActividades])
  useEffect(() => { cargarNotas() }, [cargarNotas])

  const sincronizar = async () => {
    setSincronizando(true); setResultado(null)
    try {
      const r = await materiasApi.sincronizar(id)
      setResultado({ tipo: 'ok', texto: `${r.mensaje} · ${fechaHora(r.fecha)}` })
    } catch (e) {
      // El servidor responde 502 con el detalle; se conservan los últimos datos sincronizados
      setResultado({ tipo: 'error', texto: `${e.data?.mensaje || e.message} Se muestran los últimos datos sincronizados.` })
    } finally {
      setSincronizando(false)
      cargarMateria(); cargarEstudiantes(); cargarActividades(); cargarNotas()
    }
  }

  if (error) return <div className="alerta error">{error} <Link to="/materias">Volver</Link></div>
  if (!materia) return <p className="muted">Cargando…</p>

  const q = `?materia=${materia.id}`

  return (
    <>
      <p className="migas"><Link to="/materias">Mis materias</Link> / {materia.nombre}</p>
      <div className="encabezado">
        <div>
          <h1>{materia.nombre} · 1.er parcial</h1>
          <p className="muted">
            Estudiantes y actividades leídos desde Moodle (solo lectura) · última sincronización: <strong>{fechaHora(materia.ultimaSincronizacion)}</strong>
          </p>
        </div>
        <div className="acciones">
          <Link className="btn-secundario" to={`/estructura${q}`}>Estructura temática</Link>
          <Link className="btn-secundario" to={`/dashboard${q}`}>Dashboard</Link>
          <button className="btn-primario" onClick={sincronizar} disabled={sincronizando}>
            {sincronizando ? 'Sincronizando…' : '⟳ Sincronizar notas'}
          </button>
        </div>
      </div>
      {resultado && <div className={`alerta ${resultado.tipo}`}>{resultado.texto}</div>}

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
                  <td>{`${e.apellidos} ${e.nombres}`.trim() || '—'}</td>
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

      <section className="panel">
        <div className="panel-cab">
          <h3>Calificaciones sincronizadas</h3>
          <span className="muted">Escala 0–10 · se distingue nota cero, sin entrega y pendiente de calificar</span>
        </div>
        {notas && notas.actividades.length > 0 ? (
          <>
            <div className="tabla-scroll">
              <table className="matriz">
                <thead>
                  <tr><th>Estudiante</th>{notas.actividades.map(a => <th key={a.id} title={`${a.nombre} (${fmt(a.peso)} %)`}>{a.nombre}</th>)}</tr>
                </thead>
                <tbody>
                  {notas.estudiantes.items.map(e => (
                    <tr key={e.estudianteId}>
                      <th scope="row">{e.nombre}</th>
                      {notas.actividades.map(a => {
                        const c = e.notas[a.id]
                        const est = ESTADOS_NOTA[c?.estado] || ESTADOS_NOTA.SinEntrega
                        return (
                          <td key={a.id} className={`nota-celda ${est.clase}`} title={est.texto}>
                            {c?.nota !== null && c?.nota !== undefined ? fmt(c.nota) : <small>{est.texto}</small>}
                          </td>
                        )
                      })}
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
            <Paginacion page={pageNotas} pageSize={PAGE_SIZE} total={notas.estudiantes.total} onChange={setPageNotas} />
          </>
        ) : (
          <p className="muted">Aún no hay calificaciones. Presiona «Sincronizar notas».</p>
        )}
      </section>
      <p className="muted pie">Ponderación del parcial: 40 % evaluaciones · 30 % APE · 30 % trabajo autónomo (RN-02).</p>
    </>
  )
}

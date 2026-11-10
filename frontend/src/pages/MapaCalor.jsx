import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { analiticaApi } from '../api/sprint2'
import SelectorMateria, { useMateriaSeleccionada } from '../components/SelectorMateria'
import { fmt } from '../components/formato'
import '../styles-sprint2.css'

const CLASE = { Critico: 'critico', EnUmbral: 'umbral', Aprobado: 'aprobado', SinDatos: 'sindatos' }

function Celda({ celda }) {
  const c = celda ?? { nota: null, nivel: 'SinDatos' }
  return <td className={`celda ${CLASE[c.nivel] ?? 'sindatos'}`}>{c.nota === null ? '—' : fmt(c.nota)}</td>
}

/** Mockup M7 - Mapa de calor: en qué temas y contenidos falla cada estudiante y el curso (HU-12). */
export default function MapaCalor() {
  const { materias, materia, error: errMaterias, seleccionar } = useMateriaSeleccionada()
  const [mapa, setMapa] = useState(null)
  const [error, setError] = useState(null)
  const [nivel, setNivel] = useState('temas')

  useEffect(() => {
    if (!materia) return
    setMapa(null); setError(null)
    analiticaApi.mapaCalor(materia.id).then(setMapa).catch(e => setError(e.status === 404 ? 'sin-datos' : e.message))
  }, [materia])

  const columnas = mapa
    ? (nivel === 'temas'
        ? mapa.temas.map(t => ({ id: t.temaId, titulo: t.tema, ayuda: t.unidad }))
        : mapa.temas.flatMap(t => t.contenidos.map(c => ({ id: c.contenidoId, titulo: c.contenido, ayuda: t.tema }))))
    : []
  const valor = (fila) => (nivel === 'temas' ? fila.temas : fila.contenidos)
  const promedios = mapa ? (nivel === 'temas' ? mapa.promedioCursoTemas : mapa.promedioCursoContenidos) : {}
  const q = materia ? `?materia=${materia.id}` : ''

  return (
    <>
      <div className="encabezado">
        <div>
          <h1>Desempeño por tema y contenido mínimo</h1>
          <p className="muted">Mapa de calor: en qué contenidos falla cada estudiante y el curso</p>
        </div>
        <Link className="btn-secundario" to={`/dashboard${q}`}>← Dashboard</Link>
      </div>

      {errMaterias && <div className="alerta error">{errMaterias}</div>}
      <SelectorMateria materias={materias} materia={materia} onChange={seleccionar} />
      {error === 'sin-datos' && <div className="vacio"><h3>Aún no hay datos para analizar</h3><p>Sincroniza la materia desde «Mis materias».</p></div>}
      {error && error !== 'sin-datos' && <div className="alerta error">{error}</div>}

      {mapa && mapa.temas.length === 0 && (
        <div className="vacio"><h3>Sin estructura temática</h3>
          <p>Registra los temas y mapea las actividades en <Link to={`/estructura${q}`}>Estructura temática</Link> para ver este mapa.</p></div>
      )}

      {mapa && mapa.actividadesSinMapear > 0 && mapa.temas.length > 0 && (
        <div className="alerta aviso">{mapa.actividadesSinMapear} actividad(es) sin mapear: sus notas no se reflejan aquí. <Link to={`/estructura/mapeo${q}`}>Mapear actividades</Link></div>
      )}

      {mapa && mapa.temas.length > 0 && (
        <section className="panel">
          <div className="panel-cab">
            <div className="segmentado" role="group" aria-label="Nivel de detalle">
              <button className={nivel === 'temas' ? 'activa' : ''} onClick={() => setNivel('temas')}>Por tema</button>
              <button className={nivel === 'contenidos' ? 'activa' : ''} onClick={() => setNivel('contenidos')}>Por contenido mínimo</button>
            </div>
            <div className="leyenda">
              <span className="celda aprobado">≥ {fmt(mapa.umbrales.aprobado)} Logrado</span>
              <span className="celda umbral">{fmt(mapa.umbrales.critico)}–{fmt(mapa.umbrales.aprobado)} En umbral</span>
              <span className="celda critico">&lt; {fmt(mapa.umbrales.critico)} Crítico</span>
              <span className="celda sindatos">— Sin datos</span>
            </div>
          </div>
          <div className="tabla-scroll">
            <table className="mapa">
              <thead><tr><th>Estudiante</th>{columnas.map(c => <th key={c.id} title={c.ayuda}>{c.titulo}</th>)}</tr></thead>
              <tbody>
                {mapa.estudiantes.map(f => (
                  <tr key={f.estudianteId}>
                    <th scope="row"><Link to={`/dashboard/ficha/${f.estudianteId}${q}`}>{f.nombre}</Link></th>
                    {columnas.map(c => <Celda key={c.id} celda={valor(f)[c.id]} />)}
                  </tr>
                ))}
                <tr className="curso">
                  <th scope="row">Curso (promedio)</th>
                  {columnas.map(c => <Celda key={c.id} celda={promedios[c.id]} />)}
                </tr>
              </tbody>
            </table>
          </div>
          <p className="muted">Cada celda promedia las notas ponderadas de las actividades evaluadas que miden ese tema. «—» indica que no hay actividades mapeadas o evaluadas.</p>
        </section>
      )}
    </>
  )
}

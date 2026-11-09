import { useCallback, useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { analiticaApi } from '../api/sprint2'
import { NivelBadge } from '../components/Badges'
import Modal from '../components/Modal'
import SelectorMateria, { useMateriaSeleccionada } from '../components/SelectorMateria'
import { fechaHora, fmt, TIPOS } from '../components/formato'
import '../styles-sprint2.css'

/** Mockup M6 - Dashboard del curso: indicadores, distribución y nivel de riesgo (HU-10 y HU-11). */
export default function DashboardCurso() {
  const { materias, materia, error: errMaterias, seleccionar } = useMateriaSeleccionada()
  const [datos, setDatos] = useState(null)
  const [error, setError] = useState(null)
  const [umbrales, setUmbrales] = useState(null)
  const [guardando, setGuardando] = useState(false)
  const [errUmbral, setErrUmbral] = useState(null)

  const cargar = useCallback(async () => {
    if (!materia) return
    setError(null)
    try { setDatos(await analiticaApi.indicadores(materia.id)) }
    catch (e) { setDatos(null); setError(e.status === 404 ? 'sin-datos' : e.message) }
  }, [materia])

  useEffect(() => { setDatos(null); cargar() }, [cargar])

  const guardarUmbrales = async (ev) => {
    ev.preventDefault(); setGuardando(true); setErrUmbral(null)
    try {
      await analiticaApi.guardarUmbrales(materia.id, Number(umbrales.critico), Number(umbrales.aprobado))
      setUmbrales(null); await cargar()
    } catch (e) { setErrUmbral(e.message) }
    finally { setGuardando(false) }
  }

  const maxBarra = datos ? Math.max(1, ...datos.curso.distribucion.map(d => d.cantidad)) : 1
  const q = materia ? `?materia=${materia.id}` : ''

  return (
    <>
      <div className="encabezado">
        <div>
          <h1>Dashboard del curso</h1>
          <p className="muted">
            {datos ? <>Datos de Moodle · actualizado {fechaHora(datos.actualizadoUtc)} · escala {datos.escala}</> : 'Indicadores calculados con las calificaciones sincronizadas'}
          </p>
        </div>
        <div className="acciones">
          {materia && <Link className="btn-secundario" to={`/dashboard/mapa-calor${q}`}>Mapa de calor por tema</Link>}
          {datos && <button className="btn-secundario"
            onClick={() => setUmbrales({ critico: datos.umbrales.critico, aprobado: datos.umbrales.aprobado })}>⚙ Umbrales de riesgo</button>}
        </div>
      </div>

      {errMaterias && <div className="alerta error">{errMaterias}</div>}
      <SelectorMateria materias={materias} materia={materia} onChange={seleccionar} />

      {error === 'sin-datos' && (
        <div className="vacio"><h3>Aún no hay datos para analizar</h3>
          <p>Abre «Mis materias» y presiona <strong>Sincronizar con Moodle</strong>. Las calificaciones se enviarán al análisis automáticamente.</p></div>
      )}
      {error && error !== 'sin-datos' && <div className="alerta error">{error}</div>}

      {datos && (
        <>
          <div className="kpis">
            <div className="kpi"><strong>{fmt(datos.curso.promedio)}</strong><span>Promedio del curso</span></div>
            <div className="kpi"><strong>{fmt(datos.curso.desviacion)}</strong><span>Desviación estándar</span></div>
            <div className="kpi critico"><strong>{datos.curso.criticos}</strong><span>Estudiantes críticos</span></div>
            <div className="kpi umbral"><strong>{datos.curso.enUmbral}</strong><span>En umbral</span></div>
            <div className="kpi aprobado"><strong>{datos.curso.aprobados}</strong><span>Aprobados (≥ {fmt(datos.umbrales.aprobado)})</span></div>
          </div>
          <p className="muted nota-peso">
            Se muestra la nota ponderada 40/30/30 de lo ya evaluado: en promedio el <strong>{fmt(datos.curso.pesoEvaluadoPromedio, 1)} %</strong> del peso del parcial.
            {datos.curso.sinDatos > 0 && <> {datos.curso.sinDatos} estudiante(s) aún sin notas.</>}
          </p>

          <div className="dos-columnas dash">
            <section className="panel">
              <h3>Distribución de notas</h3>
              <div className="histograma" role="img" aria-label="Histograma de notas ponderadas">
                {datos.curso.distribucion.map(d => (
                  <div key={d.rango} className="col">
                    <span className="valor">{d.cantidad}</span>
                    <div className="barra" style={{ height: `${(d.cantidad / maxBarra) * 100}%` }} />
                    <span className="rango">{d.rango}</span>
                  </div>
                ))}
              </div>
              <p className="muted">El promedio puede ocultar grupos distintos: revise cómo se reparten las notas.</p>
            </section>

            <section className="panel">
              <h3>Estudiantes por nivel de riesgo</h3>
              <table>
                <thead><tr><th>Estudiante</th><th className="num">Nota pond.</th><th className="num">Evaluado</th><th>Nivel</th><th>Alerta</th></tr></thead>
                <tbody>
                  {datos.estudiantes.map(e => (
                    <tr key={e.estudianteId}>
                      <td><Link to={`/dashboard/ficha/${e.estudianteId}${q}`}>{e.nombre}</Link></td>
                      <td className="num">{fmt(e.notaPonderada)}</td>
                      <td className="num">{fmt(e.pesoEvaluado, 1)} %</td>
                      <td><NivelBadge nivel={e.nivel} /></td>
                      <td className="alerta-txt">{e.alertaPrincipal ?? '—'}{e.alertas > 1 && <em> (+{e.alertas - 1})</em>}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
              <p className="muted">Las alertas son orientativas: no constituyen un diagnóstico y deben ser revisadas por el docente.</p>
            </section>
          </div>

          <section className="panel">
            <h3>Indicadores por actividad</h3>
            <table>
              <thead><tr><th>Actividad</th><th>Tipo</th><th className="num">Peso</th><th className="num">Promedio</th><th className="num">Desv.</th>
                <th className="num">Calificadas</th><th className="num">Nota cero</th><th className="num">Sin entrega</th><th className="num">Pendientes</th></tr></thead>
              <tbody>
                {datos.actividades.map(a => (
                  <tr key={a.actividadId}>
                    <td>{a.nombre}</td><td>{TIPOS[a.tipo] || a.tipo}</td><td className="num">{fmt(a.peso)} %</td>
                    <td className="num">{fmt(a.promedio)}</td><td className="num">{fmt(a.desviacion)}</td>
                    <td className="num">{a.calificadas}</td><td className="num">{a.ceros}</td>
                    <td className="num">{a.sinEntrega}</td><td className="num">{a.pendientes}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </section>
        </>
      )}

      {umbrales && (
        <Modal titulo="Umbrales de riesgo" onCerrar={() => setUmbrales(null)}>
          <form onSubmit={guardarUmbrales} className="form-modal">
            <p className="muted">Crítico: nota menor a este valor. Aprobado: nota igual o mayor. Por defecto 5 y 7.</p>
            <label>Crítico (menor que)
              <input type="number" step="0.5" min="0.5" max="10" value={umbrales.critico} onChange={e => setUmbrales({ ...umbrales, critico: e.target.value })} />
            </label>
            <label>Aprobado (desde)
              <input type="number" step="0.5" min="0.5" max="10" value={umbrales.aprobado} onChange={e => setUmbrales({ ...umbrales, aprobado: e.target.value })} />
            </label>
            {errUmbral && <div className="alerta error">{errUmbral}</div>}
            <div className="acciones">
              <button className="btn-primario" type="submit" disabled={guardando}>{guardando ? 'Recalculando…' : 'Guardar y recalcular'}</button>
              <button className="btn-secundario" type="button" onClick={() => setUmbrales(null)}>Cancelar</button>
            </div>
          </form>
        </Modal>
      )}
    </>
  )
}

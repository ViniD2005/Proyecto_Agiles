import { useCallback, useEffect, useState } from 'react'
import { descargarBlob, reportesApi } from '../api/sprint3'
import { NivelBadge } from '../components/Badges'
import SelectorMateria, { useMateriaSeleccionada } from '../components/SelectorMateria'
import { fechaHora, fmt } from '../components/formato'
import '../styles-sprint2.css'
import '../styles-sprint3.css'

/** Mockup M10 - Reporte inteligente del curso: vista previa (el mismo contenido del PDF) y descarga (HU-16). */
export default function ReporteCurso() {
  const { materias, materia, error: errMaterias, seleccionar } = useMateriaSeleccionada()
  const [reporte, setReporte] = useState(null)
  const [mensaje, setMensaje] = useState(null)
  const [generando, setGenerando] = useState(false)
  const [descargando, setDescargando] = useState(false)

  const cargar = useCallback(async () => {
    if (!materia) return
    setReporte(null); setMensaje(null)
    try { setReporte(await reportesApi.ultimo(materia.id)) }
    catch (e) { if (e.status !== 404) setMensaje({ tipo: 'error', texto: e.message }) }   // 404 = aún no hay reporte
  }, [materia])

  useEffect(() => { cargar() }, [cargar])

  const generar = async () => {
    setGenerando(true); setMensaje(null)
    try { setReporte(await reportesApi.generar(materia.id)); setMensaje({ tipo: 'ok', texto: 'Reporte generado con las cifras actuales.' }) }
    catch (e) { setMensaje({ tipo: 'error', texto: e.message }) }
    finally { setGenerando(false) }
  }

  const descargar = async () => {
    setDescargando(true); setMensaje(null)
    try { descargarBlob(await reportesApi.pdf(reporte.reporteId)) }
    catch (e) { setMensaje({ tipo: 'error', texto: e.message }) }
    finally { setDescargando(false) }
  }

  const c = reporte?.curso
  return (
    <>
      <div className="encabezado">
        <div>
          <h1>Reportes</h1>
          <p className="muted">Resumen del curso con indicadores, temas críticos y recomendaciones, listo para descargar en PDF.</p>
        </div>
        <div className="acciones">
          <button className="btn-primario" disabled={!materia || generando} onClick={generar}>{generando ? 'Generando…' : 'Generar reporte'}</button>
          <button className="btn-secundario" disabled={!reporte || descargando} onClick={descargar}>{descargando ? 'Descargando…' : '⬇ Descargar PDF'}</button>
        </div>
      </div>

      {errMaterias && <div className="alerta error">{errMaterias}</div>}
      <SelectorMateria materias={materias} materia={materia} onChange={seleccionar} />
      {mensaje && <div className={`alerta ${mensaje.tipo}`}>{mensaje.texto}</div>}

      {materia && !reporte && !mensaje && <div className="panel"><p className="muted">Aún no hay un reporte de esta materia. Presiona «Generar reporte».</p></div>}

      {reporte && (
        <article className="reporte-previa panel">
          <header>
            <h2>Reporte de rendimiento · {reporte.materia}</h2>
            <p className="muted">{reporte.periodo} · generado {fechaHora(reporte.generadoUtc)} por {reporte.generadoPor}
              {reporte.datosActualizadosUtc && <> · datos de Moodle al {fechaHora(reporte.datosActualizadosUtc)}</>}</p>
          </header>

          <h3>Indicadores del curso</h3>
          <div className="kpis">
            <div><span>Promedio</span><strong>{fmt(c.promedio)}</strong></div>
            <div><span>Desviación</span><strong>{fmt(c.desviacion)}</strong></div>
            <div><span>Estudiantes</span><strong>{c.estudiantes}</strong></div>
            <div className="critico"><span>Críticos</span><strong>{c.criticos}</strong></div>
            <div className="umbral"><span>En umbral</span><strong>{c.enUmbral}</strong></div>
            <div className="aprobado"><span>Aprobados</span><strong>{c.aprobados}</strong></div>
          </div>
          <p className="muted">Umbrales: crítico &lt; {fmt(reporte.umbrales.critico)} · mínimo de aprobación {fmt(reporte.umbrales.aprobado)}
            {' · '}peso evaluado promedio {fmt(c.pesoEvaluadoPromedio, 1)} %</p>

          <h3>Hallazgos</h3>
          {reporte.hallazgos.length === 0 ? <p className="muted">Sin hallazgos relevantes.</p> : <ul>{reporte.hallazgos.map((h, i) => <li key={i}>{h}</li>)}</ul>}

          <h3>Desempeño por tema</h3>
          {reporte.temas.length === 0
            ? <p className="muted">La materia aún no tiene estructura temática mapeada.</p>
            : (
              <table>
                <thead><tr><th>Tema</th><th>Unidad</th><th className="num">Promedio</th><th>Nivel</th><th className="num">Estudiantes críticos</th></tr></thead>
                <tbody>
                  {reporte.temas.map((t, i) => (
                    <tr key={i}><td>{t.tema}</td><td>{t.unidad}</td><td className="num">{fmt(t.promedio)}</td>
                      <td><NivelBadge nivel={t.nivel} /></td><td className="num">{t.estudiantesCriticos}</td></tr>
                  ))}
                </tbody>
              </table>
            )}

          <h3>Recomendaciones para el docente</h3>
          <ul>{reporte.recomendaciones.map((r, i) => <li key={i}>{r}</li>)}</ul>

          {reporte.analisisIa && (
            <>
              <h3>Análisis del modelo <small className="muted">(solo cifras agregadas, sin datos de estudiantes)</small></h3>
              <p className="analisis-ia">{reporte.analisisIa}</p>
            </>
          )}
          <p className="muted"><em>{reporte.aviso}</em></p>
        </article>
      )}
    </>
  )
}

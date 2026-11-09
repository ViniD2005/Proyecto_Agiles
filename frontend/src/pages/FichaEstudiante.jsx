import { useEffect, useState } from 'react'
import { Link, useParams, useSearchParams } from 'react-router-dom'
import { analiticaApi } from '../api/sprint2'
import { EstadoNotaBadge, NivelBadge } from '../components/Badges'
import { fmt, TIPOS } from '../components/formato'
import '../styles-sprint2.css'

function Evolucion({ puntos }) {
  if (puntos.length < 2) return <p className="muted">Se necesitan al menos 2 notas evaluadas para mostrar la evolución.</p>
  const w = 360, h = 120, p = 18
  const x = (i) => p + (i * (w - 2 * p)) / (puntos.length - 1)
  const y = (n) => h - p - (n / 10) * (h - 2 * p)
  return (
    <svg viewBox={`0 0 ${w} ${h}`} className="evolucion" role="img" aria-label="Evolución de las notas">
      <line x1={p} y1={y(0)} x2={w - p} y2={y(0)} stroke="#e2e8f0" />
      <line x1={p} y1={y(7)} x2={w - p} y2={y(7)} stroke="#bbf7d0" strokeDasharray="4 3" />
      <polyline fill="none" stroke="#2563eb" strokeWidth="2" points={puntos.map((d, i) => `${x(i)},${y(d.nota)}`).join(' ')} />
      {puntos.map((d, i) => (
        <g key={i}><circle cx={x(i)} cy={y(d.nota)} r="3.5" fill="#2563eb" />
          <title>{`${d.actividad}: ${fmt(d.nota)}`}</title></g>
      ))}
    </svg>
  )
}

/** Mockup M8 - Ficha del estudiante: notas con su estado, temas ligados a cada nota y alertas con evidencia (HU-12). */
export default function FichaEstudiante() {
  const { estudianteId } = useParams()
  const [params] = useSearchParams()
  const materiaId = Number(params.get('materia')) || null
  const [ficha, setFicha] = useState(null)
  const [error, setError] = useState(null)

  useEffect(() => {
    if (!materiaId) { setError('Falta la materia. Abre la ficha desde el dashboard.'); return }
    setFicha(null); setError(null)
    analiticaApi.ficha(materiaId, estudianteId).then(setFicha).catch(e => setError(e.message))
  }, [materiaId, estudianteId])

  const volver = <Link className="btn-secundario" to={`/dashboard?materia=${materiaId ?? ''}`}>← Dashboard</Link>

  if (error) return <><div className="alerta error">{error}</div>{volver}</>
  if (!ficha) return <p className="muted">Cargando…</p>

  return (
    <>
      <div className="encabezado">
        <div className="ficha-cab">
          <span className="avatar grande">{ficha.nombre.split(' ').map(p => p[0]).slice(0, 2).join('').toUpperCase()}</span>
          <div>
            <h1>{ficha.nombre}</h1>
            <p className="muted">{ficha.materia} · nota ponderada {fmt(ficha.notaPonderada)} / 10 · {fmt(ficha.pesoEvaluado, 1)} % del peso evaluado</p>
          </div>
          <NivelBadge nivel={ficha.nivel} />
        </div>
        {volver}
      </div>

      <div className="dos-columnas ficha">
        <section className="panel">
          <h3>Notas por actividad</h3>
          <table>
            <thead><tr><th>Actividad</th><th className="num">Peso</th><th className="num">Nota</th><th>Estado</th></tr></thead>
            <tbody>
              {ficha.actividades.map(a => (
                <tr key={a.actividadId}>
                  <td>{a.nombre} <small className="muted">{TIPOS[a.tipo] || a.tipo}</small>
                    {a.contenidos.length > 0 && (
                      <ul className="relacion">{a.contenidos.map((c, i) => <li key={i}>{c.tema} › {c.contenido}</li>)}</ul>
                    )}
                    {a.contenidos.length === 0 && <div className="relacion muted">Sin contenidos mapeados</div>}
                  </td>
                  <td className="num">{fmt(a.peso)} %</td>
                  <td className="num">{fmt(a.nota)}</td>
                  <td><EstadoNotaBadge estado={a.estado} /></td>
                </tr>
              ))}
            </tbody>
          </table>
        </section>

        <section className="panel">
          <h3>Desempeño por tema</h3>
          {ficha.temas.length === 0 && <p className="muted">La materia aún no tiene estructura temática.</p>}
          {ficha.temas.map(t => (
            <div key={t.temaId} className="barra-tema">
              <div><span>{t.tema} <small className="muted">· {t.unidad}</small></span><strong>{fmt(t.nota)}</strong></div>
              <div className="pista"><div className={`relleno ${t.nivel === 'Critico' ? 'critico' : t.nivel === 'EnUmbral' ? 'umbral' : 'aprobado'}`}
                style={{ width: `${(t.nota ?? 0) * 10}%` }} /></div>
            </div>
          ))}
          <h3 className="sub">Evolución de las notas</h3>
          <Evolucion puntos={ficha.evolucion} />
        </section>
      </div>

      <section className="panel alertas-ficha">
        <h3>Alertas con evidencia</h3>
        {ficha.alertas.length === 0 && <p className="muted">Sin alertas activas con los umbrales actuales ({fmt(ficha.umbrales.critico)} y {fmt(ficha.umbrales.aprobado)}).</p>}
        <ul>
          {ficha.alertas.map((a, i) => (
            <li key={i}><strong>{a.regla}</strong><br /><span>{a.evidencia}</span></li>
          ))}
        </ul>
        <p className="muted"><em>{ficha.aviso}</em></p>
      </section>
    </>
  )
}

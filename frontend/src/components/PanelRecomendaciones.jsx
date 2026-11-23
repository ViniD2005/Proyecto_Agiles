import { useCallback, useEffect, useState } from 'react'
import { recomendacionesApi } from '../api/sprint3'
import { NivelBadge } from './Badges'
import { fechaHora, fmt } from './formato'

const ESTADO = {
  Borrador: { texto: 'Borrador', clase: 'borrador' },
  Aprobada: { texto: 'Aprobada', clase: 'aprobada' }
}
const FUENTE = { Modelo: 'Generada con el modelo', Plantilla: 'Generada por reglas (sin modelo)' }

const aLineas = (t) => t.split('\n').map(x => x.trim()).filter(Boolean)
const deLineas = (a) => (a || []).join('\n')

/** HU-15 (editar y aprobar) y HU-20 (envío manual por correo). */
export default function PanelRecomendaciones({ materia }) {
  const [curso, setCurso] = useState(null)
  const [error, setError] = useState(null)
  const [sel, setSel] = useState(null)         // estudianteId
  const [rec, setRec] = useState(null)
  const [form, setForm] = useState(null)
  const [mensaje, setMensaje] = useState(null)
  const [ocupado, setOcupado] = useState(false)

  const cargarLista = useCallback(async () => {
    try { setCurso(await recomendacionesApi.listar(materia.id)); setError(null) }
    catch (e) { setCurso(null); setError(e.message) }
  }, [materia.id])

  useEffect(() => { setSel(null); setRec(null); setForm(null); setMensaje(null); cargarLista() }, [cargarLista])

  const mostrar = (r) => {
    setRec(r)
    setForm({
      falencias: deLineas(r.contenido.falencias),
      actividades: deLineas(r.contenido.actividadesRefuerzo),
      repasar: deLineas(r.contenido.contenidosARepasar),
      observacion: r.contenido.observacion || ''
    })
  }

  const abrir = async (estudianteId) => {
    setSel(estudianteId); setMensaje(null); setRec(null); setForm(null)
    try { mostrar(await recomendacionesApi.obtener(materia.id, estudianteId)) }
    catch (e) { if (e.status !== 404) setMensaje({ tipo: 'error', texto: e.message }) }   // 404 = aún sin recomendación
  }

  const ejecutar = async (fn, ok) => {
    setOcupado(true); setMensaje(null)
    try {
      const r = await fn()
      if (r?.contenido) mostrar(r)
      setMensaje({ tipo: 'ok', texto: typeof ok === 'function' ? ok(r) : ok })
      await cargarLista()
    } catch (e) { setMensaje({ tipo: 'error', texto: e.message }) }
    finally { setOcupado(false) }
  }

  const generar = () => ejecutar(() => recomendacionesApi.generar(materia.id, sel), 'Recomendación generada. Revísala antes de aprobarla.')
  const guardar = () => ejecutar(() => recomendacionesApi.editar(materia.id, sel, {
    falencias: aLineas(form.falencias), actividadesRefuerzo: aLineas(form.actividades),
    contenidosARepasar: aLineas(form.repasar), observacion: form.observacion.trim() || null
  }), 'Cambios guardados. La recomendación volvió a borrador y requiere aprobación.')
  const aprobar = () => ejecutar(() => recomendacionesApi.aprobar(materia.id, sel), 'Recomendación aprobada.')
  const enviar = () => {
    if (!window.confirm('Se enviará esta recomendación al correo institucional del estudiante. ¿Continuar?')) return
    ejecutar(() => recomendacionesApi.enviar(materia.id, sel), (r) => `Enviada a ${r.enviadaA}.`)
  }

  const estudiante = curso?.estudiantes.find(e => e.estudianteId === sel)
  const aprobada = rec?.estado === 'Aprobada'

  return (
    <div className="dos-columnas recomendaciones">
      <section className="panel">
        <h3>Estudiantes</h3>
        <p className="muted">Cada recomendación se basa en las notas y los temas del estudiante. Tú la editas y apruebas.</p>
        {error && <div className="alerta error">{error}</div>}
        {!curso && !error && <p className="muted">Cargando…</p>}
        {curso && curso.estudiantes.length === 0 && <p className="muted">Aún no hay estudiantes con calificaciones sincronizadas.</p>}
        <ul className="lista-estudiantes">
          {curso?.estudiantes.map(e => (
            <li key={e.estudianteId}>
              <button className={e.estudianteId === sel ? 'activo' : ''} onClick={() => abrir(e.estudianteId)}>
                <span>{e.estudiante}<small className="muted"> · {fmt(e.notaPonderada)}</small></span>
                <span className="etiquetas">
                  <NivelBadge nivel={e.nivel} />
                  {e.estado && <span className={`estado-rec ${(ESTADO[e.estado] || {}).clase}`}>{(ESTADO[e.estado] || {}).texto || e.estado}</span>}
                </span>
              </button>
            </li>
          ))}
        </ul>
      </section>

      <section className="panel detalle-rec">
        {!sel && <p className="muted">Selecciona un estudiante para ver o generar su recomendación.</p>}
        {sel && (
          <>
            <h3>{estudiante?.estudiante}</h3>
            {mensaje && <div className={`alerta ${mensaje.tipo}`}>{mensaje.texto}</div>}

            {!rec && (
              <div>
                <p className="muted">Este estudiante aún no tiene recomendación.</p>
                <button className="btn-primario" disabled={ocupado} onClick={generar}>{ocupado ? 'Generando…' : 'Generar recomendación'}</button>
              </div>
            )}

            {rec && form && (
              <>
                <div className="meta-rec">
                  <NivelBadge nivel={rec.nivel} />
                  <span className={`estado-rec ${(ESTADO[rec.estado] || {}).clase}`}>{(ESTADO[rec.estado] || {}).texto || rec.estado}</span>
                  <small className="muted">{FUENTE[rec.fuente] || rec.fuente} · {fechaHora(rec.generadaUtc)}</small>
                </div>

                <details className="evidencia" open>
                  <summary>Evidencia (calculada por el sistema)</summary>
                  <pre>{rec.evidencia}</pre>
                </details>

                <label>Falencias detectadas
                  <textarea rows={3} value={form.falencias} disabled={ocupado} onChange={e => setForm({ ...form, falencias: e.target.value })} />
                </label>
                <label>Actividades de refuerzo
                  <textarea rows={4} value={form.actividades} disabled={ocupado} onChange={e => setForm({ ...form, actividades: e.target.value })} />
                </label>
                <label>Contenidos a repasar
                  <textarea rows={3} value={form.repasar} disabled={ocupado} onChange={e => setForm({ ...form, repasar: e.target.value })} />
                </label>
                <label>Observación del docente
                  <textarea rows={2} value={form.observacion} disabled={ocupado} onChange={e => setForm({ ...form, observacion: e.target.value })} />
                </label>
                <p className="muted">Una línea por elemento. Al guardar cambios, la recomendación vuelve a borrador.</p>

                {rec.editadaUtc && <p className="muted">Editada por {rec.editadaPor} el {fechaHora(rec.editadaUtc)}.</p>}
                {aprobada && (
                  <p className="muted">Aprobada por {rec.aprobadaPor} el {fechaHora(rec.aprobadaUtc)}.
                    {rec.enviadaUtc && <> Enviada a {rec.enviadaA} el {fechaHora(rec.enviadaUtc)}.</>}</p>
                )}
                {aprobada && (
                  <p className="muted">Una recomendación aprobada no se puede regenerar ni aprobar de nuevo. Si editas el contenido, vuelve a borrador.</p>
                )}

                <div className="acciones">
                  <button className="btn-secundario" disabled={ocupado} onClick={guardar}>Guardar cambios</button>
                  <button className="btn-secundario" disabled={ocupado || aprobada} onClick={generar}
                          title={aprobada ? 'Una recomendación aprobada no se regenera' : ''}>Regenerar</button>
                  <button className="btn-primario" disabled={ocupado || aprobada} onClick={aprobar}>Aprobar</button>
                  <button className="btn-primario enviar" disabled={ocupado || !aprobada} onClick={enviar}
                          title={aprobada ? '' : 'Solo se envía una recomendación aprobada'}>✉ Enviar por correo</button>
                </div>
                <p className="muted"><em>{rec.aviso}</em></p>
              </>
            )}
          </>
        )}
      </section>
    </div>
  )
}

import { useEffect, useRef, useState } from 'react'
import { agenteApi } from '../api/sprint3'
import SelectorMateria, { useMateriaSeleccionada } from '../components/SelectorMateria'
import PanelRecomendaciones from '../components/PanelRecomendaciones'
import { fechaHora } from '../components/formato'
import '../styles-sprint2.css'
import '../styles-sprint3.css'

const SUGERENCIAS = [
  '¿Qué estudiantes están en riesgo y por qué?',
  '¿Qué temas tienen peor desempeño en el curso?',
  'Dame un resumen del curso',
  '¿Hay actividades sin entrega o con nota cero?'
]

function Lista({ titulo, items, clase }) {
  if (!items?.length) return null
  return (
    <div className={`bloque ${clase}`}>
      <h4>{titulo}</h4>
      <ul>{items.map((t, i) => <li key={i}>{t}</li>)}</ul>
    </div>
  )
}

function Respuesta({ r }) {
  const [verHerramientas, setVerHerramientas] = useState(false)
  return (
    <div className="burbuja agente">
      <div className="burbuja-cab">
        <strong>Agente IA</strong>
        <small className="muted">{r.materia} · {fechaHora(r.fechaUtc)}</small>
      </div>
      {r.advertencias.map((a, i) => <div key={i} className="alerta aviso">{a}</div>)}
      <Lista titulo="Hechos (calculados por el sistema)" items={r.hechos} clase="hechos" />
      <Lista titulo="Inferencias del modelo" items={r.inferencias} clase="inferencias" />
      <Lista titulo="Recomendaciones" items={r.recomendaciones} clase="recomendaciones" />
      <Lista titulo="Incertidumbre" items={r.incertidumbre} clase="incertidumbre" />
      <div className="pie-respuesta">
        <span className="muted">
          {r.intencion === 'Fuera de alcance'
            ? 'Consulta fuera del alcance del agente'
            : r.modeloDisponible ? `Modelo: ${r.modelo}` : 'Sin modelo de lenguaje: respuesta por reglas del sistema'}
          {' · '}intención: {r.intencion}
        </span>
        <button className="btn-link" onClick={() => setVerHerramientas(v => !v)}>
          {verHerramientas ? 'Ocultar' : 'Ver'} herramientas usadas ({r.herramientas.length})
        </button>
      </div>
      {verHerramientas && (
        <table className="herramientas">
          <thead><tr><th>Herramienta</th><th>Parámetros</th><th>Resultado</th><th className="num">ms</th></tr></thead>
          <tbody>
            {r.herramientas.map((h, i) => (
              <tr key={i} className={h.exito ? '' : 'falla'}>
                <td><code>{h.herramienta}</code></td>
                <td><small>{Object.entries(h.parametros).map(([k, v]) => `${k}=${v}`).join(', ') || '—'}</small></td>
                <td><small>{h.exito ? '✓ ' : '✗ '}{h.resumen}</small></td>
                <td className="num">{h.duracionMs}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </div>
  )
}

/** Mockup M9 - Agente IA: consulta en lenguaje natural (HU-13/14) y recomendaciones por estudiante (HU-15). */
export default function AgenteIA() {
  const { materias, materia, error: errMaterias, seleccionar } = useMateriaSeleccionada()
  const [pestana, setPestana] = useState('consulta')
  const [estado, setEstado] = useState(null)
  const [pregunta, setPregunta] = useState('')
  const [chat, setChat] = useState([])        // { rol: 'docente'|'agente'|'error', texto?, respuesta? }
  const [enviando, setEnviando] = useState(false)
  const fin = useRef(null)

  useEffect(() => { agenteApi.estado().then(setEstado).catch(() => setEstado(null)) }, [])
  useEffect(() => { setChat([]) }, [materia?.id])
  useEffect(() => { fin.current?.scrollIntoView?.({ behavior: 'smooth' }) }, [chat, enviando])

  const enviar = async (texto) => {
    const q = (texto ?? pregunta).trim()
    if (!q || !materia || enviando) return
    setPregunta('')
    setChat(c => [...c, { rol: 'docente', texto: q }])
    setEnviando(true)
    try {
      const r = await agenteApi.consultar(materia.id, q)
      setChat(c => [...c, { rol: 'agente', respuesta: r }])
    } catch (e) {
      setChat(c => [...c, { rol: 'error', texto: e.status === 403 ? 'No tienes acceso a esa materia (solo ves las tuyas).' : e.message }])
    } finally { setEnviando(false) }
  }

  return (
    <>
      <div className="encabezado">
        <div>
          <h1>Agente IA</h1>
          <p className="muted">Consulta el rendimiento de tu curso. Las cifras las calcula el sistema; el modelo solo las explica.</p>
        </div>
        {estado && (
          <span className={`estado-modelo ${estado.disponible ? 'activo' : 'inactivo'}`} title={estado.detalle || ''}>
            ● {estado.proveedor} · {estado.modelo} {estado.disponible ? 'disponible' : 'no disponible (se usan reglas)'}
          </span>
        )}
      </div>

      {errMaterias && <div className="alerta error">{errMaterias}</div>}
      <SelectorMateria materias={materias} materia={materia} onChange={seleccionar} />

      <div className="pestanas" role="tablist">
        <button role="tab" aria-selected={pestana === 'consulta'} className={pestana === 'consulta' ? 'activa' : ''} onClick={() => setPestana('consulta')}>Consulta</button>
        <button role="tab" aria-selected={pestana === 'recomendaciones'} className={pestana === 'recomendaciones' ? 'activa' : ''} onClick={() => setPestana('recomendaciones')}>Recomendaciones por estudiante</button>
      </div>

      {pestana === 'consulta' && materia && (
        <section className="panel chat">
          <div className="mensajes" aria-live="polite">
            {chat.length === 0 && (
              <div className="chat-vacio">
                <p>Pregunta por el curso <strong>{materia.nombre}</strong>. Por ejemplo:</p>
                <div className="sugerencias">
                  {SUGERENCIAS.map(s => <button key={s} className="btn-secundario" onClick={() => enviar(s)}>{s}</button>)}
                </div>
              </div>
            )}
            {chat.map((m, i) => m.rol === 'docente'
              ? <div key={i} className="burbuja docente">{m.texto}</div>
              : m.rol === 'error'
                ? <div key={i} className="alerta error">{m.texto}</div>
                : <Respuesta key={i} r={m.respuesta} />)}
            {enviando && <div className="burbuja agente pensando">Consultando herramientas y analizando…</div>}
            <div ref={fin} />
          </div>
          <form className="entrada" onSubmit={e => { e.preventDefault(); enviar() }}>
            <input value={pregunta} onChange={e => setPregunta(e.target.value)} maxLength={500} disabled={enviando}
                   placeholder="Escribe tu pregunta sobre el curso…" aria-label="Pregunta al agente" />
            <button className="btn-primario" disabled={enviando || !pregunta.trim()}>Enviar</button>
          </form>
          <p className="muted aviso-chat">Orientativo: no sustituye tu criterio docente. Al modelo solo se envían códigos anónimos (E01, E02…).</p>
        </section>
      )}

      {pestana === 'recomendaciones' && materia && <PanelRecomendaciones materia={materia} />}
    </>
  )
}

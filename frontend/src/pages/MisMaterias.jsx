import { useCallback, useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { materiasApi, moodleApi } from '../api/client'
import Etiqueta from '../components/Etiqueta'

const fecha = (iso) => iso ? new Date(iso.endsWith('Z') ? iso : iso + 'Z').toLocaleString('es-EC') : '—'

/** Mockup M2 - Mis materias y estado de sincronización (HU-05). */
export default function MisMaterias() {
  const [materias, setMaterias] = useState([])
  const [estado, setEstado] = useState(null)
  const [cargando, setCargando] = useState(true)
  const [sincronizando, setSincronizando] = useState(false)
  const [mensaje, setMensaje] = useState(null)

  const cargar = useCallback(async () => {
    setCargando(true)
    try {
      const [lista, est] = await Promise.all([materiasApi.listar(), moodleApi.estado().catch(() => null)])
      setMaterias(lista)
      setEstado(est)
    } catch (e) {
      setMensaje({ tipo: 'error', texto: e.message })
    } finally {
      setCargando(false)
    }
  }, [])

  useEffect(() => { cargar() }, [cargar])

  const sincronizar = async () => {
    setSincronizando(true)
    setMensaje(null)
    try {
      const r = await materiasApi.sincronizarTodas()
      setMensaje({ tipo: 'ok', texto: `${r.mensaje} Origen: ${r.origen} · ${fecha(r.fecha)}` })
    } catch (e) {
      // HU-05 criterio 2: se informa el error y se conservan los últimos datos
      const r = e.data
      setMensaje({ tipo: 'error', texto: `${r?.mensaje || e.message} Se muestran los últimos datos sincronizados.` })
    } finally {
      setSincronizando(false)
      cargar()
    }
  }

  const ultima = materias.map(m => m.ultimaSincronizacion).filter(Boolean).sort().at(-1)

  return (
    <>
      <div className="encabezado">
        <div>
          <h1>Mis materias</h1>
          <p className="muted">Materias asignadas al docente en Moodle · 1.er parcial</p>
        </div>
        <button className="btn-primario" onClick={sincronizar} disabled={sincronizando}>
          {sincronizando ? 'Sincronizando…' : '⟳ Sincronizar con Moodle'}
        </button>
      </div>

      <div className="barra-estado">
        <span>Moodle: {estado ? <Etiqueta valor={estado.conectado ? 'Conectado' : 'Desconectado'} /> : '…'}</span>
        {estado?.conectado && <span>{estado.sitio} · versión {estado.version}</span>}
        {estado && !estado.conectado && <span className="texto-error">{estado.error}</span>}
        <span>Última sincronización: <strong>{fecha(ultima)}</strong></span>
        {estado && <span>Origen: {estado.origen}</span>}
      </div>

      {mensaje && <div className={`alerta ${mensaje.tipo}`}>{mensaje.texto}</div>}

      {cargando ? <p className="muted">Cargando…</p> : materias.length === 0 ? (
        <div className="vacio">
          <h3>Aún no hay materias</h3>
          <p>Presiona «Sincronizar con Moodle» para traer las materias que tienes asignadas.</p>
        </div>
      ) : (
        <div className="grid-materias">
          {materias.map(m => (
            <article key={m.id} className="tarjeta-materia">
              <header>
                <div>
                  <h3>{m.nombre}</h3>
                  <span className="muted">{m.nombreCorto}</span>
                </div>
                <Etiqueta valor="Activo" />
              </header>
              <div className="contadores">
                <div><strong>{m.totalEstudiantes}</strong><span>Estudiantes</span></div>
                <div><strong>{m.totalActividades}</strong><span>Actividades</span></div>
                <div><strong>—</strong><span>Unidades</span></div>
              </div>
              <footer>
                <small className="muted">Sincronizado: {fecha(m.ultimaSincronizacion)}</small>
                <Link className="btn-secundario" to={`/materias/${m.id}`}>Ver materia</Link>
              </footer>
            </article>
          ))}
        </div>
      )}
    </>
  )
}

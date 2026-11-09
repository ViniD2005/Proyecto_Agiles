import { useCallback, useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { estructuraApi, mapeoApi } from '../api/sprint2'
import SelectorMateria, { useMateriaSeleccionada } from '../components/SelectorMateria'
import { TIPOS } from '../components/formato'
import '../styles-sprint2.css'

/** Mockup M5 - Mapeo actividad ↔ contenidos mínimos (HU-09). */
export default function MapeoActividades() {
  const { materias, materia, error: errMaterias, seleccionar } = useMateriaSeleccionada()
  const [arbol, setArbol] = useState(null)
  const [mapeo, setMapeo] = useState(null)
  const [actividadId, setActividadId] = useState(null)
  const [marcados, setMarcados] = useState(new Set())
  const [mensaje, setMensaje] = useState(null)
  const [guardando, setGuardando] = useState(false)

  const cargar = useCallback(async () => {
    if (!materia) return
    try {
      const [a, m] = await Promise.all([estructuraApi.obtener(materia.id), mapeoApi.obtener(materia.id)])
      setArbol(a); setMapeo(m)
      setActividadId(prev => (m.actividades.some(x => x.actividadId === prev) ? prev : m.actividades[0]?.actividadId ?? null))
    } catch (e) { setMensaje({ tipo: 'error', texto: e.message }) }
  }, [materia])

  useEffect(() => { setArbol(null); setMapeo(null); cargar() }, [cargar])

  const actual = useMemo(() => mapeo?.actividades.find(a => a.actividadId === actividadId) ?? null, [mapeo, actividadId])

  // Al cambiar de actividad se cargan sus contenidos ya asignados
  useEffect(() => { setMarcados(new Set(actual?.contenidos.map(c => c.contenidoId) ?? [])) }, [actual])

  const alternar = (id) => setMarcados(prev => { const n = new Set(prev); n.has(id) ? n.delete(id) : n.add(id); return n })

  const guardar = async () => {
    setGuardando(true); setMensaje(null)
    try {
      const r = await mapeoApi.asignar(actividadId, [...marcados])
      setMensaje({
        tipo: r.eventoPublicado ? 'ok' : 'aviso',
        texto: r.eventoPublicado
          ? 'Mapeo guardado. El análisis por tema se actualizará automáticamente.'
          : 'Mapeo guardado, pero el aviso al análisis no se pudo enviar (RabbitMQ no disponible). Se actualizará en la próxima sincronización.'
      })
      await cargar()
    } catch (e) { setMensaje({ tipo: 'error', texto: e.message }) }
    finally { setGuardando(false) }
  }

  const sinEstructura = arbol && arbol.unidades.length === 0

  return (
    <>
      <div className="encabezado">
        <div>
          <h1>Mapeo actividad ↔ contenidos</h1>
          <p className="muted">Indica qué contenidos mínimos evalúa cada actividad</p>
        </div>
        {materia && <Link className="btn-secundario" to={`/estructura?materia=${materia.id}`}>Estructura temática</Link>}
      </div>

      {errMaterias && <div className="alerta error">{errMaterias}</div>}
      <SelectorMateria materias={materias} materia={materia} onChange={seleccionar} />
      {mensaje && <div className={`alerta ${mensaje.tipo}`}>{mensaje.texto}</div>}

      {sinEstructura && (
        <div className="vacio"><h3>La materia aún no tiene estructura temática</h3>
          <p>Primero registra las unidades, temas y contenidos en <Link to={`/estructura?materia=${materia.id}`}>Estructura temática</Link>.</p></div>
      )}

      {mapeo && !sinEstructura && mapeo.actividades.length === 0 && (
        <div className="vacio"><h3>No hay actividades sincronizadas</h3><p>Sincroniza la materia desde «Mis materias».</p></div>
      )}

      {mapeo && arbol && !sinEstructura && mapeo.actividades.length > 0 && (
        <div className="dos-columnas mapeo">
          <section className="panel">
            <div className="panel-cab"><h3>Actividades</h3>
              {mapeo.pendientes > 0 && <span className="etiqueta error">{mapeo.pendientes} pendiente(s)</span>}</div>
            <ul className="lista-seleccion">
              {mapeo.actividades.map(a => (
                <li key={a.actividadId}>
                  <button className={a.actividadId === actividadId ? 'activa' : ''} onClick={() => setActividadId(a.actividadId)}>
                    <strong>{a.nombre}</strong> <small>{TIPOS[a.tipo] || a.tipo}</small>
                    <span className={a.pendiente ? 'pendiente-txt' : 'muted'}>
                      {a.pendiente ? 'Sin contenidos (pendiente)' : `${a.contenidos.length} contenido(s) asignado(s)`}
                    </span>
                  </button>
                </li>
              ))}
            </ul>
          </section>

          <section className="panel">
            <div className="panel-cab"><h3>Contenidos evaluados por «{actual?.nombre}»</h3>
              <span className="etiqueta ape">{marcados.size} seleccionado(s)</span></div>
            {arbol.unidades.map(u => (
              <fieldset key={u.id} className="grupo">
                <legend>{u.nombre}</legend>
                {u.temas.map(t => (
                  <div key={t.id}>
                    <p className="tema-nombre">{t.nombre}</p>
                    {t.contenidos.length === 0 && <p className="muted">Sin contenidos</p>}
                    {t.contenidos.map(c => (
                      <label key={c.id} className="check">
                        <input type="checkbox" checked={marcados.has(c.id)} onChange={() => alternar(c.id)} /> {c.nombre}
                      </label>
                    ))}
                  </div>
                ))}
              </fieldset>
            ))}
            <div className="acciones">
              <button className="btn-secundario" onClick={() => setMarcados(new Set(actual?.contenidos.map(c => c.contenidoId) ?? []))}>Cancelar</button>
              <button className="btn-primario" disabled={guardando} onClick={guardar}>{guardando ? 'Guardando…' : 'Guardar mapeo'}</button>
            </div>
          </section>
        </div>
      )}
    </>
  )
}

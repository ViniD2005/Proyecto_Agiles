import { useCallback, useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { estructuraApi } from '../api/sprint2'
import Modal from '../components/Modal'
import SelectorMateria, { useMateriaSeleccionada } from '../components/SelectorMateria'
import '../styles-sprint2.css'

const ETIQUETA = { unidad: 'unidad', tema: 'tema', contenido: 'contenido mínimo' }
const EJEMPLO = `{
  "unidades": [
    { "nombre": "Unidad 1", "temas": [
      { "nombre": "Fundamentos de HTML y CSS", "contenidos": ["Etiquetas semánticas", "Selectores CSS"] }
    ] }
  ]
}`

/** Mockup M4 - Estructura temática por materia (HU-08). */
export default function EstructuraTematica() {
  const { materias, materia, error: errMaterias, seleccionar } = useMateriaSeleccionada()
  const [arbol, setArbol] = useState(null)
  const [mensaje, setMensaje] = useState(null)
  const [modal, setModal] = useState(null)       // { modo, nivel, padreId, itemId, valor }
  const [importar, setImportar] = useState(null) // texto JSON o null
  const [ocupado, setOcupado] = useState(false)

  const cargar = useCallback(async () => {
    if (!materia) return
    try { setArbol(await estructuraApi.obtener(materia.id)) }
    catch (e) { setMensaje({ tipo: 'error', texto: e.message }) }
  }, [materia])

  useEffect(() => { setArbol(null); cargar() }, [cargar])

  const ejecutar = async (accion, ok) => {
    setOcupado(true); setMensaje(null)
    try { await accion(); if (ok) setMensaje({ tipo: 'ok', texto: ok }); await cargar() }
    catch (e) { setMensaje({ tipo: 'error', texto: e.message }) }
    finally { setOcupado(false) }
  }

  const guardarModal = (ev) => {
    ev.preventDefault()
    const { modo, nivel, padreId, itemId, valor } = modal
    setModal(null)
    if (modo === 'crear')
      ejecutar(() => estructuraApi.crear(materia.id, { nivel, padreId, nombre: valor }), `Se creó el/la ${ETIQUETA[nivel]}.`)
    else
      ejecutar(() => estructuraApi.renombrar(materia.id, nivel, itemId, valor), 'Nombre actualizado.')
  }

  // HU-08, criterio 3: si el elemento está asignado a actividades se pide confirmación
  const eliminar = async (nivel, itemId, nombre) => {
    setOcupado(true); setMensaje(null)
    try {
      try {
        if (!window.confirm(`¿Eliminar ${ETIQUETA[nivel]} «${nombre}»?`)) return
        await estructuraApi.eliminar(materia.id, nivel, itemId, false)
      } catch (e) {
        if (e.status === 409 && e.data?.requiereConfirmacion) {
          if (!window.confirm(`${e.data.mensaje}\n\n¿Eliminar de todos modos?`)) return
          await estructuraApi.eliminar(materia.id, nivel, itemId, true)
        } else throw e
      }
      setMensaje({ tipo: 'ok', texto: 'Elemento eliminado.' })
      await cargar()
    } catch (e) { setMensaje({ tipo: 'error', texto: e.message }) }
    finally { setOcupado(false) }
  }

  const enviarImportacion = () => {
    let cuerpo
    try { cuerpo = JSON.parse(importar) } catch { setMensaje({ tipo: 'error', texto: 'El JSON no es válido.' }); return }
    ejecutar(async () => { await estructuraApi.importar(materia.id, cuerpo); setImportar(null) }, 'Estructura importada.')
  }

  return (
    <>
      <div className="encabezado">
        <div>
          <h1>Estructura temática</h1>
          <p className="muted">Unidades → temas → contenidos mínimos (según el sílabo)</p>
        </div>
        <div className="acciones">
          {materia && <Link className="btn-secundario" to={`/estructura/mapeo?materia=${materia.id}`}>Mapear actividades</Link>}
          <button className="btn-primario" disabled={!materia || ocupado}
                  onClick={() => setModal({ modo: 'crear', nivel: 'unidad', padreId: null, valor: '' })}>+ Nueva unidad</button>
        </div>
      </div>

      {errMaterias && <div className="alerta error">{errMaterias}</div>}
      <SelectorMateria materias={materias} materia={materia} onChange={seleccionar} />
      {mensaje && <div className={`alerta ${mensaje.tipo}`}>{mensaje.texto}</div>}

      {arbol && arbol.advertencias.length > 0 && (
        <div className="alerta aviso">
          <strong>Pendiente por completar:</strong>
          <ul>{arbol.advertencias.map(a => <li key={a}>{a}</li>)}</ul>
        </div>
      )}

      {arbol && arbol.unidades.length === 0 && (
        <div className="panel">
          <h3>Importar el sílabo</h3>
          <p className="muted">Pega la estructura en JSON para cargar las unidades, temas y contenidos de una sola vez.</p>
          {importar === null
            ? <button className="btn-secundario" onClick={() => setImportar(EJEMPLO)}>Importar desde JSON</button>
            : <>
                <textarea className="json" rows={10} value={importar} onChange={e => setImportar(e.target.value)} spellCheck={false} />
                <div className="acciones">
                  <button className="btn-primario" disabled={ocupado} onClick={enviarImportacion}>Importar</button>
                  <button className="btn-secundario" onClick={() => setImportar(null)}>Cancelar</button>
                </div>
              </>}
        </div>
      )}

      {arbol?.unidades.map(u => (
        <section key={u.id} className="panel unidad">
          <header>
            <h3>{u.nombre}</h3>
            <div className="acciones-mini">
              <button onClick={() => setModal({ modo: 'crear', nivel: 'tema', padreId: u.id, valor: '' })}>+ Tema</button>
              <button onClick={() => setModal({ modo: 'renombrar', nivel: 'unidad', itemId: u.id, valor: u.nombre })} aria-label={`Editar ${u.nombre}`}>✎</button>
              <button onClick={() => eliminar('unidad', u.id, u.nombre)} aria-label={`Eliminar ${u.nombre}`}>🗑</button>
            </div>
          </header>
          {u.temas.length === 0 && <p className="muted">Esta unidad aún no tiene temas.</p>}
          {u.temas.map(t => (
            <div key={t.id} className="tema">
              <div className="tema-cab">
                <strong>{t.nombre}</strong>
                <span className="acciones-mini">
                  <button onClick={() => setModal({ modo: 'crear', nivel: 'contenido', padreId: t.id, valor: '' })}>+ Contenido</button>
                  <button onClick={() => setModal({ modo: 'renombrar', nivel: 'tema', itemId: t.id, valor: t.nombre })} aria-label={`Editar ${t.nombre}`}>✎</button>
                  <button onClick={() => eliminar('tema', t.id, t.nombre)} aria-label={`Eliminar ${t.nombre}`}>🗑</button>
                </span>
              </div>
              <div className="chips">
                {t.contenidos.length === 0 && <span className="muted">Sin contenidos mínimos</span>}
                {t.contenidos.map(c => (
                  <span key={c.id} className="chip" title={`${c.actividadesAsignadas} actividad(es) asignada(s)`}>
                    {c.nombre}
                    {c.actividadesAsignadas > 0 && <em>{c.actividadesAsignadas}</em>}
                    <button onClick={() => setModal({ modo: 'renombrar', nivel: 'contenido', itemId: c.id, valor: c.nombre })} aria-label={`Editar ${c.nombre}`}>✎</button>
                    <button onClick={() => eliminar('contenido', c.id, c.nombre)} aria-label={`Eliminar ${c.nombre}`}>✕</button>
                  </span>
                ))}
              </div>
            </div>
          ))}
        </section>
      ))}
      {arbol && <p className="muted pie">Una materia suele tener 4 unidades; cada tema se descompone en contenidos mínimos evaluables (RN-05).</p>}

      {modal && (
        <Modal titulo={`${modal.modo === 'crear' ? 'Nuevo' : 'Editar'} ${ETIQUETA[modal.nivel]}`} onCerrar={() => setModal(null)}>
          <form onSubmit={guardarModal} className="form-modal">
            <label>Nombre
              <input autoFocus maxLength={200} value={modal.valor} onChange={e => setModal({ ...modal, valor: e.target.value })} />
            </label>
            <div className="acciones">
              <button type="submit" className="btn-primario" disabled={!modal.valor.trim()}>Guardar</button>
              <button type="button" className="btn-secundario" onClick={() => setModal(null)}>Cancelar</button>
            </div>
          </form>
        </Modal>
      )}
    </>
  )
}

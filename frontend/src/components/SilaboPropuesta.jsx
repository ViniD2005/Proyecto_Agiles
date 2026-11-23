import { useState } from 'react'
import { estructuraApi } from '../api/sprint2'
import { silaboApi } from '../api/sprint3'

const MAX_MB = 5

/** HU-21 (opcional): sube el sílabo en PDF, muestra la propuesta editable y solo guarda al confirmar. */
export default function SilaboPropuesta({ materiaId, onImportada, yaTieneEstructura = false }) {
  const [propuesta, setPropuesta] = useState(null)   // { fuente, advertencias, aviso }
  const [texto, setTexto] = useState('')             // JSON editable de las unidades
  const [mensaje, setMensaje] = useState(null)
  const [ocupado, setOcupado] = useState(false)

  const subir = async (ev) => {
    const archivo = ev.target.files?.[0]
    ev.target.value = ''
    if (!archivo) return
    if (!/\.pdf$/i.test(archivo.name)) { setMensaje({ tipo: 'error', texto: 'El archivo debe ser un PDF.' }); return }
    if (archivo.size > MAX_MB * 1024 * 1024) { setMensaje({ tipo: 'error', texto: `El PDF supera los ${MAX_MB} MB.` }); return }
    setOcupado(true); setMensaje(null); setPropuesta(null)
    try {
      const r = await silaboApi.proponer(materiaId, archivo)
      setPropuesta(r)
      setTexto(JSON.stringify({ unidades: r.unidades }, null, 2))
    } catch (e) { setMensaje({ tipo: 'error', texto: e.message }) }
    finally { setOcupado(false) }
  }

  const confirmar = async () => {
    let cuerpo
    try { cuerpo = JSON.parse(texto) } catch { setMensaje({ tipo: 'error', texto: 'El JSON no es válido.' }); return }
    setOcupado(true); setMensaje(null)
    try {
      await estructuraApi.importar(materiaId, cuerpo)
      setPropuesta(null); setTexto('')
      onImportada?.()
    } catch (e) { setMensaje({ tipo: 'error', texto: e.message }) }
    finally { setOcupado(false) }
  }

  return (
    <div className="silabo-pdf">
      <h4>O sube el sílabo en PDF</h4>
      <p className="muted">El sistema propone unidades, temas y contenidos. No se guarda nada hasta que revises y confirmes.</p>
      <label className="btn-secundario subir">
        {ocupado && !propuesta ? 'Analizando…' : 'Seleccionar PDF'}
        <input type="file" accept="application/pdf,.pdf" onChange={subir} disabled={ocupado} hidden />
      </label>
      {mensaje && <div className={`alerta ${mensaje.tipo}`}>{mensaje.texto}</div>}

      {propuesta && (
        <>
          <div className="alerta aviso">
            <strong>{propuesta.fuente === 'Modelo' ? 'Propuesta generada con el modelo.' : 'Propuesta por patrones del texto (sin modelo).'}</strong>
            {' '}{propuesta.aviso}
            {propuesta.advertencias.length > 0 && <ul>{propuesta.advertencias.map((a, i) => <li key={i}>{a}</li>)}</ul>}
          </div>
          {yaTieneEstructura && (
            <div className="alerta aviso">Esta materia ya tiene estructura temática, así que esta propuesta es solo de lectura. Para importarla, elimina primero la estructura actual.</div>
          )}
          <textarea className="json" rows={14} value={texto} onChange={e => setTexto(e.target.value)} spellCheck={false} aria-label="Propuesta editable" />
          <div className="acciones">
            <button className="btn-primario" disabled={ocupado || yaTieneEstructura} onClick={confirmar}
                    title={yaTieneEstructura ? 'La materia ya tiene estructura: elimínala antes de importar' : ''}>Confirmar e importar</button>
            <button className="btn-secundario" disabled={ocupado} onClick={() => { setPropuesta(null); setTexto('') }}>Descartar</button>
          </div>
        </>
      )}
    </div>
  )
}

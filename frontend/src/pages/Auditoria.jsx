import { useCallback, useEffect, useState } from 'react'
import { auditoriaApi } from '../api/sprint3'
import Paginacion from '../components/Paginacion'
import { fechaHora } from '../components/formato'
import '../styles-sprint2.css'
import '../styles-sprint3.css'

const VACIO = { accion: '', origen: '', usuario: '', desde: '', hasta: '' }
const ORIGENES = ['MS-A', 'MS-B', 'MS-C']

/** Vista de auditoría centralizada para el administrador técnico (HU-17). Solo lectura; los secretos nunca se muestran. */
export default function Auditoria() {
  const [filtros, setFiltros] = useState(VACIO)
  const [aplicados, setAplicados] = useState(VACIO)
  const [page, setPage] = useState(1)
  const [datos, setDatos] = useState(null)
  const [acciones, setAcciones] = useState([])
  const [error, setError] = useState(null)
  const pageSize = 15

  useEffect(() => { auditoriaApi.acciones().then(setAcciones).catch(() => setAcciones([])) }, [])

  const cargar = useCallback(async () => {
    setError(null)
    try { setDatos(await auditoriaApi.listar({ ...aplicados, page, pageSize })) }
    catch (e) { setDatos(null); setError(e.message) }
  }, [aplicados, page])

  useEffect(() => { cargar() }, [cargar])

  const buscar = (ev) => { ev.preventDefault(); setPage(1); setAplicados({ ...filtros }) }
  const limpiar = () => { setFiltros(VACIO); setAplicados({ ...VACIO }); setPage(1) }
  const campo = (k) => ({ value: filtros[k], onChange: e => setFiltros({ ...filtros, [k]: e.target.value }) })

  return (
    <>
      <div className="encabezado">
        <div>
          <h1>Auditoría</h1>
          <p className="muted">Bitácora centralizada: accesos, sincronizaciones, consultas al agente, recomendaciones, reportes y envíos.</p>
        </div>
      </div>

      <form className="filtros-audit panel" onSubmit={buscar}>
        <label>Acción
          <select {...campo('accion')}>
            <option value="">Todas</option>
            {acciones.map(a => <option key={a} value={a}>{a}</option>)}
          </select>
        </label>
        <label>Origen
          <select {...campo('origen')}>
            <option value="">Todos</option>
            {ORIGENES.map(o => <option key={o} value={o}>{o}</option>)}
          </select>
        </label>
        <label>Usuario<input type="text" placeholder="correo" {...campo('usuario')} /></label>
        <label>Desde<input type="date" {...campo('desde')} /></label>
        <label>Hasta<input type="date" {...campo('hasta')} /></label>
        <div className="acciones">
          <button className="btn-primario">Filtrar</button>
          <button type="button" className="btn-secundario" onClick={limpiar}>Limpiar</button>
        </div>
      </form>

      {error && <div className="alerta error">{error}</div>}
      {datos && (
        <section className="panel">
          <div className="tabla-scroll">
            <table className="tabla-audit">
              <thead><tr><th>Fecha</th><th>Usuario</th><th>Acción</th><th>Origen</th><th>Detalle</th></tr></thead>
              <tbody>
                {datos.items.length === 0 && <tr><td colSpan={5} className="muted">Sin registros con esos filtros.</td></tr>}
                {datos.items.map(r => (
                  <tr key={r.id}>
                    <td>{fechaHora(r.fecha)}</td>
                    <td>{r.usuarioEmail || <span className="muted">sistema</span>}</td>
                    <td><code className={`accion ${/DENEGADO|FALLO|FALLIDO/.test(r.accion) ? 'mala' : ''}`}>{r.accion}</code></td>
                    <td>{r.servicio}</td>
                    <td className="detalle"><small>{r.detalle || '—'}</small></td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <Paginacion page={datos.page} pageSize={datos.pageSize} total={datos.total} onChange={setPage} />
        </section>
      )}
    </>
  )
}

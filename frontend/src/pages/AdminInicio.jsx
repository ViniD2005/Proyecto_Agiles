import { Link } from 'react-router-dom'

export default function AdminInicio() {
  return (
    <div className="vacio">
      <h1>Administración técnica</h1>
      <p>Este rol no tiene acceso a las materias ni a los estudiantes (RN-07).</p>
      <p>Consulta la bitácora centralizada de accesos, sincronizaciones, consultas al agente, reportes y envíos.</p>
      <Link className="btn-primario enlace" to="/auditoria">Abrir auditoría</Link>
    </div>
  )
}

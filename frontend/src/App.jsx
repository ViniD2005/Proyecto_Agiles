import { Navigate, Route, Routes } from 'react-router-dom'
import ProtectedRoute from './auth/ProtectedRoute'
import Layout from './components/Layout'
import Login from './pages/Login'
import MisMaterias from './pages/MisMaterias'
import MateriaDetalle from './pages/MateriaDetalle'
import EstructuraTematica from './pages/EstructuraTematica'
import MapeoActividades from './pages/MapeoActividades'
import DashboardCurso from './pages/DashboardCurso'
import MapaCalor from './pages/MapaCalor'
import FichaEstudiante from './pages/FichaEstudiante'
import Proximamente from './pages/Proximamente'
import AdminInicio from './pages/AdminInicio'
import { ROLES, useAuth } from './auth/AuthContext'

function Inicio() {
  const { user } = useAuth()
  if (!user) return <Navigate to="/login" replace />
  return <Navigate to={user.rol === ROLES.ADMIN ? '/admin' : '/materias'} replace />
}

export default function App() {
  return (
    <Routes>
      <Route path="/login" element={<Login />} />
      <Route path="/" element={<Inicio />} />

      {/* Rutas del docente (HU-04, criterio 4: bloqueo por rol) */}
      <Route element={<ProtectedRoute roles={[ROLES.DOCENTE]}><Layout /></ProtectedRoute>}>
        <Route path="/materias" element={<MisMaterias />} />
        <Route path="/materias/:id" element={<MateriaDetalle />} />

        {/* Sprint 2 */}
        <Route path="/estructura" element={<EstructuraTematica />} />
        <Route path="/estructura/mapeo" element={<MapeoActividades />} />
        <Route path="/dashboard" element={<DashboardCurso />} />
        <Route path="/dashboard/mapa-calor" element={<MapaCalor />} />
        <Route path="/dashboard/ficha/:estudianteId" element={<FichaEstudiante />} />

        {/* Sprint 3 */}
        <Route path="/agente" element={<Proximamente titulo="Agente IA" sprint={3} historia="HU-13 / HU-14" />} />
        <Route path="/reportes" element={<Proximamente titulo="Reportes" sprint={3} historia="HU-16" />} />
      </Route>

      {/* Rutas del administrador técnico */}
      <Route element={<ProtectedRoute roles={[ROLES.ADMIN]}><Layout /></ProtectedRoute>}>
        <Route path="/admin" element={<AdminInicio />} />
        <Route path="/auditoria" element={<Proximamente titulo="Auditoría" sprint={3} historia="HU-17" />} />
      </Route>

      <Route path="/sin-permiso" element={<div className="centrado"><h2>Acceso denegado</h2><p>Tu rol no tiene permiso para abrir esta página.</p><a href="/">Volver al inicio</a></div>} />
      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  )
}

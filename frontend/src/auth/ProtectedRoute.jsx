import { Navigate, useLocation } from 'react-router-dom'
import { useAuth } from './AuthContext'

/** Bloquea rutas sin sesión o con un rol no autorizado (T4.5). */
export default function ProtectedRoute({ roles, children }) {
  const { user } = useAuth()
  const location = useLocation()
  if (!user) return <Navigate to="/login" replace state={{ desde: location.pathname }} />
  if (roles && !roles.includes(user.rol)) return <Navigate to="/sin-permiso" replace />
  return children
}

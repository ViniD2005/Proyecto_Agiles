import { NavLink, Outlet, useNavigate } from 'react-router-dom'
import { ROLES, useAuth } from '../auth/AuthContext'

const MENU_DOCENTE = [
  { to: '/materias', label: 'Mis materias' },
  { to: '/estructura', label: 'Estructura temática' },
  { to: '/dashboard', label: 'Dashboard' },
  { to: '/agente', label: 'Agente IA' },
  { to: '/reportes', label: 'Reportes' }
]
const MENU_ADMIN = [
  { to: '/admin', label: 'Inicio' },
  { to: '/auditoria', label: 'Auditoría' }
]

export default function Layout() {
  const { user, logout } = useAuth()
  const navigate = useNavigate()
  const menu = user?.rol === ROLES.ADMIN ? MENU_ADMIN : MENU_DOCENTE
  const iniciales = (user?.nombre || user?.email || '?').split(' ').map(p => p[0]).slice(0, 2).join('').toUpperCase()

  return (
    <div className="app">
      <aside className="sidebar">
        <div className="marca">
          <strong>EduMonitor AI</strong>
          <span>{user?.rol === ROLES.ADMIN ? 'Administración técnica' : 'Panel del docente'}</span>
        </div>
        <nav>
          {menu.map(m => (
            <NavLink key={m.to} to={m.to} className={({ isActive }) => isActive ? 'activo' : ''}>{m.label}</NavLink>
          ))}
        </nav>
        <p className="aviso-lateral">Apoyo a la decisión docente · no modifica calificaciones en Moodle.</p>
      </aside>
      <div className="contenido">
        <header className="topbar">
          <span />
          <div className="usuario">
            <span className="avatar">{iniciales}</span>
            <div>
              <div className="u-nombre">{user?.nombre}</div>
              <div className="u-email">{user?.email}</div>
            </div>
            <button className="btn-link" onClick={() => { logout(); navigate('/login') }}>Cerrar sesión</button>
          </div>
        </header>
        <main><Outlet /></main>
      </div>
    </div>
  )
}

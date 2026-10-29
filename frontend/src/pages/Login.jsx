import { useState } from 'react'
import { Navigate, useLocation, useNavigate } from 'react-router-dom'
import { ROLES, useAuth } from '../auth/AuthContext'

const EMAIL_RE = /^[^\s@]+@[^\s@]+\.[^\s@]+$/

/** Mockup M1 - Inicio de sesión del docente (HU-04). */
export default function Login() {
  const { user, login } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [errores, setErrores] = useState({})
  const [errorGeneral, setErrorGeneral] = useState('')
  const [cargando, setCargando] = useState(false)

  if (user) return <Navigate to="/" replace />

  const validar = () => {
    const e = {}
    if (!email.trim()) e.email = 'Ingresa tu usuario o correo.'
    else if (!EMAIL_RE.test(email.trim())) e.email = 'El correo no tiene un formato válido.'
    if (!password) e.password = 'Ingresa tu contraseña.'
    setErrores(e)
    return Object.keys(e).length === 0
  }

  const enviar = async (ev) => {
    ev.preventDefault()
    setErrorGeneral('')
    if (!validar()) return
    setCargando(true)
    try {
      const u = await login(email.trim(), password)
      const destino = location.state?.desde || (u.rol === ROLES.ADMIN ? '/admin' : '/materias')
      navigate(destino, { replace: true })
    } catch (err) {
      setErrorGeneral(err.status === 401 ? 'Usuario o contraseña incorrectos.' : err.message)
    } finally {
      setCargando(false)
    }
  }

  return (
    <div className="login">
      <section className="login-intro">
        <span className="login-inst">Universidad Técnica de Ambato · FISEI</span>
        <h1>EduMonitor AI</h1>
        <p>Seguimiento académico personalizado conectado a Moodle, con apoyo de un agente de IA.</p>
        <ul>
          <li>Detecta estudiantes en riesgo a tiempo</li>
          <li>Identifica en qué tema y contenido fallan</li>
          <li>Recomienda acciones de refuerzo</li>
        </ul>
        <small>Apoyo a la decisión docente · no modifica calificaciones.</small>
      </section>

      <form className="login-form" onSubmit={enviar} noValidate>
        <h2>Bienvenido</h2>
        <p className="muted">Ingresa con tus credenciales de docente</p>

        <label htmlFor="email">Usuario o correo</label>
        <input id="email" type="email" autoComplete="username" placeholder="docente@uta.edu.ec"
               value={email} onChange={e => setEmail(e.target.value)} aria-invalid={!!errores.email} />
        {errores.email && <span className="campo-error">{errores.email}</span>}

        <label htmlFor="password">Contraseña</label>
        <input id="password" type="password" autoComplete="current-password"
               value={password} onChange={e => setPassword(e.target.value)} aria-invalid={!!errores.password} />
        {errores.password && <span className="campo-error">{errores.password}</span>}

        {errorGeneral && <div className="alerta error" role="alert">{errorGeneral}</div>}

        <button className="btn-primario" type="submit" disabled={cargando}>
          {cargando ? 'Verificando…' : 'Iniciar sesión'}
        </button>
      </form>
    </div>
  )
}

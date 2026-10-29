import { createContext, useCallback, useContext, useEffect, useMemo, useState } from 'react'
import { authApi, tokenStore } from '../api/client'

export const ROLES = { DOCENTE: 'Docente', ADMIN: 'AdministradorTecnico' }

const AuthContext = createContext(null)

// Decodifica el payload del JWT (solo para mostrar datos; la validación real la hace el backend)
function decodificar(token) {
  try {
    const base64 = token.split('.')[1].replace(/-/g, '+').replace(/_/g, '/')
    const json = decodeURIComponent(atob(base64).split('').map(c => '%' + ('00' + c.charCodeAt(0).toString(16)).slice(-2)).join(''))
    const p = JSON.parse(json)
    if (p.exp * 1000 < Date.now()) return null
    return { id: p.sub, email: p.email, nombre: p.name, rol: p.role, exp: p.exp }
  } catch {
    return null
  }
}

export function AuthProvider({ children }) {
  const [user, setUser] = useState(() => {
    const t = tokenStore.get()
    return t ? decodificar(t) : null
  })

  const logout = useCallback(() => {
    tokenStore.clear()
    setUser(null)
  }, [])

  const login = useCallback(async (email, password) => {
    const r = await authApi.login(email, password)
    tokenStore.set(r.token)
    const u = decodificar(r.token)
    setUser(u)
    return u
  }, [])

  // Cierre de sesión automático cuando el backend responde 401 o el token expira
  useEffect(() => {
    const onExpira = () => setUser(null)
    window.addEventListener('edumonitor:sesion-expirada', onExpira)
    let timer
    if (user?.exp) timer = setTimeout(logout, Math.max(0, user.exp * 1000 - Date.now()))
    return () => { window.removeEventListener('edumonitor:sesion-expirada', onExpira); clearTimeout(timer) }
  }, [user, logout])

  const value = useMemo(() => ({ user, login, logout }), [user, login, logout])
  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

export const useAuth = () => useContext(AuthContext)

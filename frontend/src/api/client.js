// Cliente HTTP hacia el API Gateway. Adjunta el JWT y maneja errores comunes.
const API_URL = (import.meta.env.VITE_API_URL || 'http://localhost:5000').replace(/\/$/, '')
const TOKEN_KEY = 'edumonitor.token'

export const tokenStore = {
  get: () => sessionStorage.getItem(TOKEN_KEY),
  set: (t) => sessionStorage.setItem(TOKEN_KEY, t),
  clear: () => sessionStorage.removeItem(TOKEN_KEY)
}

export class ApiError extends Error {
  constructor(status, message, data) {
    super(message)
    this.status = status
    this.data = data
  }
}

export async function api(path, { method = 'GET', body, auth = true } = {}) {
  const esFormulario = typeof FormData !== 'undefined' && body instanceof FormData   // multipart: el navegador pone el Content-Type
  const headers = esFormulario ? {} : { 'Content-Type': 'application/json' }
  const token = tokenStore.get()
  if (auth && token) headers.Authorization = `Bearer ${token}`

  let res
  try {
    res = await fetch(`${API_URL}${path}`, { method, headers, body: body ? (esFormulario ? body : JSON.stringify(body)) : undefined })
  } catch {
    throw new ApiError(0, 'No se pudo conectar con el servidor. Verifica que el API Gateway esté en ejecución.')
  }

  if (res.status === 401 && auth) {
    tokenStore.clear()
    window.dispatchEvent(new Event('edumonitor:sesion-expirada'))
  }

  const text = await res.text()
  let data = null
  if (text) { try { data = JSON.parse(text) } catch { data = null } }
  if (!res.ok) {
    const msg = data?.detail || data?.mensaje || data?.title ||
      (res.status === 403 ? 'No tienes permiso sobre este recurso.' : `Error ${res.status}`)
    throw new ApiError(res.status, msg, data)
  }
  return data
}

/** Descarga un archivo binario (PDF) con el JWT; devuelve { blob, nombre }. */
export async function apiBlob(path) {
  const token = tokenStore.get()
  let res
  try {
    res = await fetch(`${API_URL}${path}`, { headers: token ? { Authorization: `Bearer ${token}` } : {} })
  } catch {
    throw new ApiError(0, 'No se pudo conectar con el servidor. Verifica que el API Gateway esté en ejecución.')
  }
  if (res.status === 401) {
    tokenStore.clear()
    window.dispatchEvent(new Event('edumonitor:sesion-expirada'))
  }
  if (!res.ok) {
    let detalle = null
    try { detalle = (await res.json())?.detail } catch { /* sin cuerpo JSON */ }
    throw new ApiError(res.status, detalle || (res.status === 403 ? 'No tienes permiso sobre este recurso.' : `Error ${res.status}`))
  }
  const cab = res.headers.get('Content-Disposition') || ''
  const m = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(cab)
  return { blob: await res.blob(), nombre: m ? decodeURIComponent(m[1]) : 'reporte.pdf' }
}

export const authApi = {
  login: (email, password) => api('/api/auth/login', { method: 'POST', body: { email, password }, auth: false })
}

export const materiasApi = {
  listar: () => api('/api/materias'),
  sincronizarTodas: () => api('/api/materias/sincronizar', { method: 'POST' }),
  obtener: (id) => api(`/api/materias/${id}`),
  sincronizar: (id) => api(`/api/materias/${id}/sincronizar`, { method: 'POST' }),
  estudiantes: (id, { buscar = '', page = 1, pageSize = 8 } = {}) =>
    api(`/api/materias/${id}/estudiantes?buscar=${encodeURIComponent(buscar)}&page=${page}&pageSize=${pageSize}`),
  actividades: (id, { tipo = '', page = 1, pageSize = 50 } = {}) =>
    api(`/api/materias/${id}/actividades?page=${page}&pageSize=${pageSize}${tipo ? `&tipo=${tipo}` : ''}`)
}

export const moodleApi = {
  estado: () => api('/api/moodle/estado')
}

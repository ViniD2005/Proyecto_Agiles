// Cliente de los endpoints del Sprint 3 (HU-13 a HU-21). Reutiliza el cliente HTTP de los sprints anteriores.
import { api, apiBlob } from './client'

export const agenteApi = {
  estado: () => api('/api/agente/estado'),
  consultar: (materiaId, pregunta) => api('/api/agente/chat', { method: 'POST', body: { materiaId, pregunta } })
}

export const recomendacionesApi = {
  listar: (materiaId) => api(`/api/materias/${materiaId}/recomendaciones`),
  obtener: (materiaId, estudianteId) => api(`/api/materias/${materiaId}/recomendaciones/${estudianteId}`),
  generar: (materiaId, estudianteId) =>
    api(`/api/materias/${materiaId}/recomendaciones/${estudianteId}/generar`, { method: 'POST' }),
  editar: (materiaId, estudianteId, cuerpo) =>
    api(`/api/materias/${materiaId}/recomendaciones/${estudianteId}`, { method: 'PUT', body: cuerpo }),
  aprobar: (materiaId, estudianteId) =>
    api(`/api/materias/${materiaId}/recomendaciones/${estudianteId}/aprobar`, { method: 'POST' }),
  enviar: (materiaId, estudianteId) =>
    api(`/api/materias/${materiaId}/recomendaciones/${estudianteId}/enviar`, { method: 'POST' })
}

export const reportesApi = {
  generar: (materiaId) => api(`/api/materias/${materiaId}/reporte`, { method: 'POST' }),
  ultimo: (materiaId) => api(`/api/materias/${materiaId}/reporte`),
  pdf: (reporteId) => apiBlob(`/api/reportes/${reporteId}/pdf`)
}

export const silaboApi = {
  proponer: (materiaId, archivo) => {
    const form = new FormData()
    form.append('archivo', archivo)
    return api(`/api/materias/${materiaId}/silabo/propuesta`, { method: 'POST', body: form })
  }
}

export const auditoriaApi = {
  listar: ({ accion = '', origen = '', usuario = '', desde = '', hasta = '', page = 1, pageSize = 15 } = {}) => {
    const q = new URLSearchParams({ page, pageSize })
    if (accion) q.set('accion', accion)
    if (origen) q.set('origen', origen)
    if (usuario) q.set('usuario', usuario)
    if (desde) q.set('desde', desde)
    if (hasta) q.set('hasta', hasta)
    return api(`/api/auditoria?${q}`)
  },
  acciones: () => api('/api/auditoria/acciones')
}

/** Guarda un PDF recibido como archivo descargado por el navegador. */
export function descargarBlob({ blob, nombre }) {
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = nombre
  document.body.appendChild(a)
  a.click()
  a.remove()
  setTimeout(() => URL.revokeObjectURL(url), 1000)
}

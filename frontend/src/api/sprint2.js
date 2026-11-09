// Cliente de los endpoints del Sprint 2 (HU-07 a HU-12). Reutiliza el cliente HTTP del Sprint 1.
import { api } from './client'

export const estructuraApi = {
  obtener: (id) => api(`/api/materias/${id}/estructura`),
  crear: (id, body) => api(`/api/materias/${id}/estructura`, { method: 'POST', body }),
  renombrar: (id, nivel, itemId, nombre) =>
    api(`/api/materias/${id}/estructura/${nivel}/${itemId}`, { method: 'PUT', body: { nombre } }),
  eliminar: (id, nivel, itemId, confirmar = false) =>
    api(`/api/materias/${id}/estructura/${nivel}/${itemId}?confirmar=${confirmar}`, { method: 'DELETE' }),
  importar: (id, body) => api(`/api/materias/${id}/estructura/importar`, { method: 'POST', body })
}

export const mapeoApi = {
  obtener: (id) => api(`/api/materias/${id}/mapeo`),
  asignar: (actividadId, contenidoIds) =>
    api(`/api/actividades/${actividadId}/contenidos`, { method: 'PUT', body: { contenidoIds } })
}

export const calificacionesApi = {
  listar: (id, { page = 1, pageSize = 10 } = {}) =>
    api(`/api/materias/${id}/calificaciones?page=${page}&pageSize=${pageSize}`)
}

export const analiticaApi = {
  indicadores: (id) => api(`/api/materias/${id}/indicadores`),
  riesgo: (id) => api(`/api/materias/${id}/riesgo`),
  mapaCalor: (id) => api(`/api/materias/${id}/desempeno-por-tema`),
  ficha: (id, estudianteId) => api(`/api/materias/${id}/fichas/${estudianteId}`),
  guardarUmbrales: (materiaId, critico, aprobado) =>
    api('/api/umbrales', { method: 'PUT', body: { materiaId, critico, aprobado } })
}

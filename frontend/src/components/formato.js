// Utilidades de formato compartidas por las pantallas del Sprint 2
export const fmt = (n, max = 2) =>
  n === null || n === undefined ? '—' : Number(n).toLocaleString('es-EC', { minimumFractionDigits: 0, maximumFractionDigits: max })

export const fechaHora = (iso) =>
  iso ? new Date(iso.endsWith('Z') ? iso : iso + 'Z').toLocaleString('es-EC') : '—'

export const NIVELES = {
  Critico: { texto: 'Crítico', clase: 'critico' },
  EnUmbral: { texto: 'En umbral', clase: 'umbral' },
  Aprobado: { texto: 'Aprobado', clase: 'aprobado' },
  SinDatos: { texto: 'Sin datos', clase: 'sindatos' }
}

export const ESTADOS_NOTA = {
  Calificada: { texto: 'Calificada', clase: 'calificada' },
  Cero: { texto: 'Nota cero', clase: 'cero' },
  SinEntrega: { texto: 'Sin entrega', clase: 'sinentrega' },
  PendienteDeCalificar: { texto: 'Pendiente de calificar', clase: 'pendiente' }
}

export const TIPOS = {
  Evaluacion: 'Evaluación',
  APE: 'APE',
  TrabajoAutonomo: 'T. autónomo',
  Otra: 'Otra'
}

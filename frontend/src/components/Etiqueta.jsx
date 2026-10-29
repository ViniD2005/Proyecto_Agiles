const CLASES = {
  Evaluacion: ['eval', 'Evaluación'],
  APE: ['ape', 'APE'],
  TrabajoAutonomo: ['ta', 'T. autónomo'],
  Otra: ['otra', 'Otra'],
  Activo: ['ok', 'Activo'],
  Conectado: ['ok', 'Conectado'],
  Desconectado: ['error', 'Sin conexión']
}

/** Etiqueta de estado/tipo reutilizable (TS1.1). */
export default function Etiqueta({ valor }) {
  const [clase, texto] = CLASES[valor] || ['otra', valor]
  return <span className={`etiqueta ${clase}`}>{texto}</span>
}

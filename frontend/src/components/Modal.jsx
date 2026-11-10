import { useEffect } from 'react'

export default function Modal({ titulo, onCerrar, children }) {
  useEffect(() => {
    const onKey = (e) => e.key === 'Escape' && onCerrar()
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [onCerrar])

  return (
    <div className="modal-fondo" onClick={onCerrar}>
      <div className="modal" role="dialog" aria-modal="true" aria-label={titulo} onClick={e => e.stopPropagation()}>
        <h3>{titulo}</h3>
        {children}
      </div>
    </div>
  )
}

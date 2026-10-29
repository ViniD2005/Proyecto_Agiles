export default function Paginacion({ page, pageSize, total, onChange }) {
  const paginas = Math.max(1, Math.ceil(total / pageSize))
  const desde = total === 0 ? 0 : (page - 1) * pageSize + 1
  const hasta = Math.min(total, page * pageSize)
  return (
    <div className="paginacion">
      <span>Mostrando {desde}–{hasta} de {total}</span>
      <div>
        <button disabled={page <= 1} onClick={() => onChange(page - 1)}>‹ Anterior</button>
        <span>Página {page} de {paginas}</span>
        <button disabled={page >= paginas} onClick={() => onChange(page + 1)}>Siguiente ›</button>
      </div>
    </div>
  )
}

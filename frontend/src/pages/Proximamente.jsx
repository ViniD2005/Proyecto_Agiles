export default function Proximamente({ titulo, sprint, historia }) {
  return (
    <div className="vacio">
      <h1>{titulo}</h1>
      <p>Esta pantalla se desarrolla en el <strong>Sprint {sprint}</strong> ({historia}).</p>
    </div>
  )
}

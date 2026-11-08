import { useEffect, useState } from 'react'
import { Navigate } from 'react-router-dom'
import { api } from '../api'
import { useAuth } from '../auth'
import type { StatsData } from '../types'

// a labelled bar, sized against the biggest value in its group
function Bars({ rows }: { rows: { label: string; value: number }[] }) {
  const max = Math.max(1, ...rows.map((r) => r.value))
  return (
    <div>
      {rows.map((r) => (
        <div key={r.label} className="score-row">
          <span className="score-label">{r.label}</span>
          <div className="bar"><div className={r.value === max ? 'bar-fill lead' : 'bar-fill'} style={{ width: `${(r.value / max) * 100}%` }} /></div>
          <span className="score-value">{r.value}</span>
        </div>
      ))}
    </div>
  )
}

export default function Stats() {
  const { user } = useAuth()
  if (!user) return <Navigate to="/login" replace />
  return <Overall />
}

function Overall() {
  const [s, setS] = useState<StatsData | null>(null)
  const [error, setError] = useState('')

  useEffect(() => {
    api<StatsData>('/me/stats').then(setS).catch((e: Error) => setError(e.message))
  }, [])

  if (error) return <p className="error" role="alert">{error}</p>
  if (!s) return <p className="muted">Loading</p>

  return (
    <div>
      <h1>Stats</h1>

      <h2>By status</h2>
      <Bars rows={Object.entries(s.statusCounts).map(([label, value]) => ({ label, value }))} />

      <h2>Ratings</h2>
      {s.ratingCount === 0 ? <p className="muted">No ratings yet.</p> : (
        <>
          <p><span className="num">{s.ratingCount}</span> {s.ratingCount === 1 ? 'rating' : 'ratings'}, average <strong className="num">{s.averageScore}</strong> / 10</p>
          <Bars rows={s.distribution.map((value, i) => ({ label: String(i + 1), value }))} />
        </>
      )}

      <h2>Watched and read</h2>
      <p><span className="num">{s.episodes}</span> episodes · <span className="num">{s.chapters}</span> chapters · <span className="num">{s.volumesRated}</span> volumes rated</p>

      <h2>Top genres</h2>
      {s.genres.length === 0 ? <p className="muted">Nothing on your list yet.</p> : <Bars rows={s.genres.map((g) => ({ label: g.name, value: g.count }))} />}
    </div>
  )
}

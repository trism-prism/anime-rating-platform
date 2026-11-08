import { useEffect, useState } from 'react'
import { Link, Navigate } from 'react-router-dom'
import { api } from '../api'
import { useAuth } from '../auth'
import { plural, score } from '../format'
import type { StatsData } from '../types'
import { useTitle } from '../useTitle'

// a labelled bar, sized against `of` (default: the biggest value). Nothing is drawn when every value is zero.
// lead: accent the biggest bar, but only when exactly one bar is the biggest
function Bars({ rows, of, lead = false }: { rows: { label: string; value: number }[]; of?: number; lead?: boolean }) {
  const max = Math.max(...rows.map((r) => r.value))
  if (!(max > 0)) return null
  const unique = lead && rows.filter((r) => r.value === max).length === 1
  return (
    <div>
      {rows.map((r) => (
        <div key={r.label} className="score-row">
          <span className="score-label">{r.label}</span>
          <div className="bar"><div className={unique && r.value === max ? 'bar-fill lead' : 'bar-fill'} style={{ width: `${(r.value / (of ?? max)) * 100}%` }} /></div>
          <span className="score-value">{r.value}</span>
        </div>
      ))}
    </div>
  )
}

const addFromBrowse = <p className="muted">Add anime from <Link to="/" className="link">Browse</Link>.</p>

export default function Stats() {
  useTitle('Stats')
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

  // only statuses in use; sized against the whole list, so no bar is full just for being the biggest
  const statuses = Object.entries(s.statusCounts).filter(([, value]) => value > 0).map(([label, value]) => ({ label, value }))
  const listed = statuses.reduce((n, r) => n + r.value, 0)
  const watched = [
    s.episodes > 0 && plural(s.episodes, 'episode'),
    s.chapters > 0 && plural(s.chapters, 'chapter'),
    s.volumesRated > 0 && `${plural(s.volumesRated, 'volume')} rated`,
  ].filter(Boolean)

  return (
    <div>
      <h1>Stats</h1>

      <h2>By status</h2>
      {listed === 0 ? addFromBrowse : <Bars rows={statuses} of={listed} />}

      <h2>Ratings</h2>
      {s.ratingCount === 0 ? <p className="muted">Rate a title from its page and your scores show up here.</p> : (
        <>
          <p><span className="num">{s.ratingCount}</span> {s.ratingCount === 1 ? 'rating' : 'ratings'}, average <span className="nowrap"><strong className="num">{s.averageScore != null ? score(s.averageScore) : '-'}</strong> / 10</span></p>
          <Bars lead rows={s.distribution.map((value, i) => ({ label: String(i + 1), value }))} />
        </>
      )}

      {watched.length > 0 && (
        <>
          <h2>Watched and read</h2>
          <p>{watched.join(' · ')}</p>
        </>
      )}

      <h2>Top genres</h2>
      {s.genres.length === 0 ? addFromBrowse : <Bars lead rows={s.genres.map((g) => ({ label: g.name, value: g.count }))} />}
    </div>
  )
}

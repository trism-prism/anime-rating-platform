import { useEffect, useState } from 'react'
import { Link, Navigate, useParams } from 'react-router-dom'
import { api } from '../api'
import { useAuth } from '../auth'
import type { StatsData, YearStats } from '../types'

// a labelled bar, sized against the biggest value in its group
function Bars({ rows }: { rows: { label: string; value: number }[] }) {
  const max = Math.max(1, ...rows.map((r) => r.value))
  return (
    <div>
      {rows.map((r) => (
        <div key={r.label} className="score-row">
          <span className="score-label">{r.label}</span>
          <div className="bar"><div className="bar-fill" style={{ width: `${(r.value / max) * 100}%` }} /></div>
          <span className="score-value">{r.value}</span>
        </div>
      ))}
    </div>
  )
}

const MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec']

export default function Stats() {
  const { user } = useAuth()
  const { year } = useParams()
  if (!user) return <Navigate to="/login" replace />
  return year ? <Year key={year} year={year} /> : <Overall />
}

function Overall() {
  const [s, setS] = useState<StatsData | null>(null)
  const [error, setError] = useState('')

  useEffect(() => {
    api<StatsData>('/me/stats').then(setS).catch((e: Error) => setError(e.message))
  }, [])

  if (error) return <p className="error">{error}</p>
  if (!s) return <p className="muted">Loading...</p>
  const thisYear = new Date().getFullYear()

  return (
    <div>
      <h1>Stats</h1>
      <p className="muted">Year in review: {[thisYear, thisYear - 1, thisYear - 2].map((y) => <span key={y}><Link to={`/stats/${y}`} className="link">{y}</Link>{' '}</span>)}</p>

      <h2>By status</h2>
      <Bars rows={Object.entries(s.statusCounts).map(([label, value]) => ({ label, value }))} />

      <h2>Ratings</h2>
      {s.ratingCount === 0 ? <p className="muted">No ratings yet.</p> : (
        <>
          <p>{s.ratingCount} {s.ratingCount === 1 ? 'rating' : 'ratings'}, average <strong>{s.averageScore}</strong> / 10</p>
          <Bars rows={s.distribution.map((value, i) => ({ label: String(i + 1), value }))} />
        </>
      )}

      <h2>Watched and read</h2>
      <p>{s.episodes} episodes · {s.chapters} chapters · {s.volumesRated} volumes rated</p>

      <h2>Top genres</h2>
      {s.genres.length === 0 ? <p className="muted">Nothing on your list yet.</p> : <Bars rows={s.genres.map((g) => ({ label: g.name, value: g.count }))} />}

      <h2>Formats</h2>
      <Bars rows={s.formats.map((f) => ({ label: f.name.replace(/_/g, ' '), value: f.count }))} />
    </div>
  )
}

function Year({ year }: { year: string }) {
  const [s, setS] = useState<YearStats | null>(null)
  const [error, setError] = useState('')

  useEffect(() => {
    api<YearStats>(`/me/stats/${year}`).then(setS).catch((e: Error) => setError(e.message))
  }, [year])

  if (error) return <p className="error">{error}</p>
  if (!s) return <p className="muted">Loading...</p>

  return (
    <div>
      <h1>{s.year} in review</h1>
      <p><Link to="/stats" className="link">All-time stats</Link></p>
      <p>Finished <strong>{s.finished}</strong> {s.finished === 1 ? 'title' : 'titles'} · {s.episodes} episodes · {s.chapters} chapters</p>
      <p>Rated {s.ratingCount} {s.ratingCount === 1 ? 'title' : 'titles'}{s.averageScore != null && <>, average <strong>{s.averageScore}</strong> / 10</>}</p>

      <h2>Finished by month</h2>
      <Bars rows={s.byMonth.map((value, i) => ({ label: MONTHS[i], value }))} />

      <h2>Top genres</h2>
      {s.genres.length === 0 ? <p className="muted">Nothing finished this year.</p> : <Bars rows={s.genres.map((g) => ({ label: g.name, value: g.count }))} />}

      <h2>Your top rated</h2>
      {s.topRated.length === 0 ? <p className="muted">No ratings this year.</p> : (
        <ol>{s.topRated.map((t) => <li key={t.titleId}><Link to={`/titles/${t.titleId}`} className="link">{t.name}</Link> <span className="muted">{t.overall}</span></li>)}</ol>
      )}
    </div>
  )
}

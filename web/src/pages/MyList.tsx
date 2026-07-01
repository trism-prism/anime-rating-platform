import { useEffect, useState } from 'react'
import { Link, Navigate } from 'react-router-dom'
import { api } from '../api'
import { useAuth } from '../auth'
import { STATUSES, type MyRating, type WatchEntry, type WatchStatus } from '../types'

export default function MyList() {
  const { user } = useAuth()
  const [list, setList] = useState<WatchEntry[] | null>(null)
  const [ratings, setRatings] = useState<MyRating[] | null>(null)
  const [filter, setFilter] = useState<WatchStatus | 'All'>('All')
  const [error, setError] = useState('')

  useEffect(() => {
    if (!user) return
    api<WatchEntry[]>('/me/watchlist').then(setList).catch((e: Error) => setError(e.message))
    api<MyRating[]>('/me/ratings').then(setRatings).catch((e: Error) => setError(e.message))
  }, [user])

  if (!user) return <Navigate to="/login" replace />

  async function update(e: WatchEntry, status: WatchStatus, progress: number) {
    setError('')
    try {
      await api(`/me/watchlist/${e.titleId}`, { method: 'PUT', body: JSON.stringify({ status, progress }) })
      setList((l) => l && l.map((x) => (x.titleId === e.titleId ? { ...x, status, progress } : x)))
    } catch (err) {
      setError((err as Error).message)
    }
  }

  async function remove(e: WatchEntry) {
    setError('')
    try {
      await api(`/me/watchlist/${e.titleId}`, { method: 'DELETE' })
      setList((l) => l && l.filter((x) => x.titleId !== e.titleId))
    } catch (err) {
      setError((err as Error).message)
    }
  }

  const shown = list?.filter((x) => filter === 'All' || x.status === filter) ?? []

  return (
    <div>
      <h1>My list</h1>
      {error && <p className="error">{error}</p>}
      <div className="filters">
        {(['All', ...STATUSES] as const).map((s) => (
          <button key={s} className={filter === s ? 'on' : ''} onClick={() => setFilter(s)}>{s}</button>
        ))}
      </div>
      {!list && <p className="muted">Loading...</p>}
      {list && shown.length === 0 && <p className="muted">Nothing here yet. Add titles from their pages.</p>}
      {shown.map((e) => (
        <div key={e.titleId} className="list-row">
          <Link to={`/titles/${e.titleId}`} className="list-name">{e.name}</Link>
          <span className="muted">{e.type}</span>
          <select value={e.status} onChange={(ev) => update(e, ev.target.value as WatchStatus, e.progress)}>
            {STATUSES.map((s) => <option key={s}>{s}</option>)}
          </select>
          <input
            type="number"
            min={0}
            className="num"
            aria-label="Progress"
            defaultValue={e.progress}
            onBlur={(ev) => {
              const p = Math.max(0, Number(ev.target.value) || 0)
              if (p !== e.progress) update(e, e.status, p)
            }}
          />
          <button onClick={() => remove(e)}>Remove</button>
        </div>
      ))}

      <h2>My ratings</h2>
      {!ratings && <p className="muted">Loading...</p>}
      {ratings && ratings.length === 0 && <p className="muted">You haven't rated anything yet.</p>}
      {ratings && ratings.length > 0 && (
        <table className="table">
          <thead>
            <tr><th>Title</th><th>Story</th><th>Art</th><th>Chars</th><th>Fun</th><th>Overall</th></tr>
          </thead>
          <tbody>
            {ratings.map((r) => (
              <tr key={r.titleId}>
                <td><Link to={`/titles/${r.titleId}`} className="link">{r.titleName}</Link></td>
                <td>{r.story}</td><td>{r.art}</td><td>{r.characters}</td><td>{r.enjoyment}</td>
                <td><strong>{r.overall}</strong></td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </div>
  )
}

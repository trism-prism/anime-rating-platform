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
  const [listError, setListError] = useState('')
  const [ratingsError, setRatingsError] = useState('')
  const [tick, setTick] = useState(0) // bump to retry loading

  useEffect(() => {
    if (!user) return
    api<WatchEntry[]>('/me/watchlist').then((l) => { setList(l); setListError('') }).catch((e: Error) => setListError(e.message))
    api<MyRating[]>('/me/ratings').then((r) => { setRatings(r); setRatingsError('') }).catch((e: Error) => setRatingsError(e.message))
  }, [user, tick])

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
      {!list && listError && <p className="error">{listError} <button onClick={() => { setListError(''); setTick((t) => t + 1) }}>Retry</button></p>}
      {!list && !listError && <p className="muted">Loading...</p>}
      {list && shown.length === 0 && <p className="muted">Nothing here yet. Add titles from their pages.</p>}
      {shown.map((e) => (
        <div key={e.titleId} className="list-row">
          <Link to={`/titles/${e.titleId}`} className="list-name">{e.name}</Link>
          <Link to={`/series/${e.seriesId}`} className="muted link-quiet">{e.seriesName}</Link>
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
      {!ratings && ratingsError && <p className="error">{ratingsError} <button onClick={() => { setRatingsError(''); setTick((t) => t + 1) }}>Retry</button></p>}
      {!ratings && !ratingsError && <p className="muted">Loading...</p>}
      {ratings && ratings.length === 0 && <p className="muted">You haven't rated anything yet.</p>}
      {ratings && ratings.length > 0 && (
        <table className="table">
          <thead>
            <tr><th>Title</th><th>Story</th><th>Art</th><th>Chars</th><th>Fun</th><th>Overall</th></tr>
          </thead>
          <tbody>
            {ratings.map((r) => (
              <tr key={`${r.titleId}-${r.volume ?? 0}`}>
                <td><Link to={`/titles/${r.titleId}`} className="link">{r.titleName}</Link>{r.volume != null && <span className="muted"> · Vol {r.volume}</span>}<span className="muted"> · <Link to={`/series/${r.seriesId}`} className="link-quiet">series</Link></span></td>
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

import { useEffect, useRef, useState } from 'react'
import { Link, Navigate } from 'react-router-dom'
import { api } from '../api'
import { useAuth } from '../auth'
import AudienceSelect from '../components/AudienceSelect'
import WatchDates from '../components/WatchDates'
import { STATUSES, type Audience, type MyRating, type WatchEntry, type WatchStatus } from '../types'

export default function MyList() {
  const { user } = useAuth()
  const [list, setList] = useState<WatchEntry[] | null>(null)
  const [ratings, setRatings] = useState<MyRating[] | null>(null)
  const [filter, setFilter] = useState<WatchStatus | 'All'>('All')
  const [error, setError] = useState('')
  const [listError, setListError] = useState('')
  const [ratingsError, setRatingsError] = useState('')
  const [tick, setTick] = useState(0) // bump to retry loading
  const inFlight = useRef(new Set<string>()) // audience changes being saved, readable at once (state lags a fast double change)
  const [changing, setChanging] = useState<string[]>([])

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

  // one change at a time per row; on failure the lists are loaded again, so the select shows what the server has
  async function changeAudience(key: string, path: string, audience: Audience, apply: () => void) {
    if (inFlight.current.has(key)) return
    inFlight.current.add(key)
    setChanging([...inFlight.current])
    setError('')
    try {
      await api(path, { method: 'PUT', body: JSON.stringify({ audience }) })
      apply()
    } catch (err) {
      setError((err as Error).message)
      setTick((t) => t + 1)
    } finally {
      inFlight.current.delete(key)
      setChanging([...inFlight.current])
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
          <AudienceSelect
            label={`Who can see ${e.name} on your list`}
            value={e.audience}
            disabled={changing.includes(`w-${e.titleId}`)}
            onChange={(a) => changeAudience(`w-${e.titleId}`, `/me/watchlist/${e.titleId}/audience`, a,
              () => setList((l) => l && l.map((x) => (x.titleId === e.titleId ? { ...x, audience: a } : x))))}
          />
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
          <WatchDates key={`${e.titleId}-${e.startedOn}-${e.finishedOn}-${e.rewatchCount}`} entry={e} onSaved={(n) => setList((l) => l && l.map((x) => (x.titleId === n.titleId ? n : x)))} />
        </div>
      ))}

      <h2>My ratings</h2>
      {!ratings && ratingsError && <p className="error">{ratingsError} <button onClick={() => { setRatingsError(''); setTick((t) => t + 1) }}>Retry</button></p>}
      {!ratings && !ratingsError && <p className="muted">Loading...</p>}
      {ratings && ratings.length === 0 && <p className="muted">You haven't rated anything yet.</p>}
      {ratings && ratings.length > 0 && (
        <div className="table-scroll">
        <table className="table">
          <thead>
            <tr><th>Title</th><th>Story</th><th>Art</th><th>Chars</th><th>Fun</th><th>Overall</th><th>Who can see</th></tr>
          </thead>
          <tbody>
            {ratings.map((r) => (
              <tr key={`${r.titleId}-${r.volume ?? 0}`}>
                <td><Link to={`/titles/${r.titleId}`} className="link">{r.titleName}</Link>{r.volume != null && <span className="muted"> · Vol {r.volume}</span>}<span className="muted"> · <Link to={`/series/${r.seriesId}`} className="link-quiet">series</Link></span></td>
                <td>{r.story}</td><td>{r.art}</td><td>{r.characters}</td><td>{r.enjoyment}</td>
                <td><strong>{r.overall}</strong></td>
                <td>
                  <AudienceSelect
                    label={`Who can see your rating of ${r.titleName}${r.volume != null ? ` volume ${r.volume}` : ''}`}
                    value={r.audience}
                    disabled={changing.includes(`r-${r.titleId}-${r.volume ?? 0}`)}
                    onChange={(a) => changeAudience(`r-${r.titleId}-${r.volume ?? 0}`,
                      `/titles/${r.titleId}/rating/audience${r.volume == null ? '' : `?volume=${r.volume}`}`, a,
                      () => setRatings((l) => l && l.map((x) => (x.titleId === r.titleId && x.volume === r.volume ? { ...x, audience: a } : x))))}
                  />
                </td>
              </tr>
            ))}
          </tbody>
        </table>
        </div>
      )}
    </div>
  )
}

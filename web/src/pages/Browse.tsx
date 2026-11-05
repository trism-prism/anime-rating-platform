import { useEffect, useState } from 'react'
import { api } from '../api'
import SearchBox from '../components/SearchBox'
import SeriesCard from '../components/SeriesCard'
import type { SeriesPage, SeriesSummary } from '../types'

const TAKE = 24
const GENRES = ['Action', 'Adventure', 'Comedy', 'Drama', 'Fantasy', 'Horror', 'Romance', 'Sci-Fi', 'Slice of Life', 'Sports', 'Supernatural', 'Thriller']

function seriesUrl(query: string, type: string, genre: string, cursor: string | null) {
  const params = new URLSearchParams({ take: String(TAKE) })
  if (query) params.set('search', query)
  if (type) params.set('type', type)
  if (genre) params.set('genre', genre)
  if (cursor) params.set('cursor', cursor)
  return `/series?${params}`
}

export default function Browse() {
  const [query, setQuery] = useState('') // search text actually sent, updated on submit
  const [type, setType] = useState('')
  const [genre, setGenre] = useState('')
  const [data, setData] = useState<{ key: string; items: SeriesSummary[]; next: string | null } | null>(null)
  const [error, setError] = useState('')
  const [tick, setTick] = useState(0) // bump to retry
  const [more, setMore] = useState<{ busy: boolean; error: string }>({ busy: false, error: '' })

  const key = `${query}|${type}|${genre}`

  useEffect(() => {
    let stale = false
    api<SeriesPage>(seriesUrl(query, type, genre, null))
      .then((d) => {
        if (stale) return
        setData({ key, items: d.items, next: d.nextCursor })
        setError('')
        setMore({ busy: false, error: '' })
      })
      .catch((e: Error) => !stale && setError(e.message))
    return () => {
      stale = true
    }
  }, [query, type, genre, key, tick])

  async function loadMore() {
    if (!data?.next) return
    const k = key
    setMore({ busy: true, error: '' })
    try {
      const d = await api<SeriesPage>(seriesUrl(query, type, genre, data.next))
      // the filters may have changed while this was in flight
      setData((cur) => (cur && cur.key === k ? { ...cur, items: [...cur.items, ...d.items], next: d.nextCursor } : cur))
      setMore({ busy: false, error: '' })
    } catch (e) {
      setMore({ busy: false, error: (e as Error).message })
    }
  }

  const loading = !data || data.key !== key

  return (
    <div>
      <h1>Browse</h1>
      <div className="filters">
        <SearchBox onSubmit={setQuery} />
        <select aria-label="Type" value={type} onChange={(e) => setType(e.target.value)}>
          <option value="">All types</option>
          <option value="Anime">Anime</option>
          <option value="Manga">Manga</option>
        </select>
        <select aria-label="Genre" value={genre} onChange={(e) => setGenre(e.target.value)}>
          <option value="">All genres</option>
          {GENRES.map((g) => <option key={g}>{g}</option>)}
        </select>
      </div>

      <div className="results">
        {error && (
          <p className="error">
            {error} <button onClick={() => { setError(''); setTick((t) => t + 1) }}>Retry</button>
          </p>
        )}
        {loading && !error && <p className="muted">Loading...</p>}
        {!loading && data.items.length === 0 && <p className="muted">No series found.</p>}
        {!loading && (
          <>
            <div className="grid">
              {data.items.map((s) => (
                <SeriesCard key={s.id} id={s.id} name={s.name} coverUrl={s.coverUrl} type={s.type} average={s.average} ratingCount={s.ratingCount} />
              ))}
            </div>
            <div className="pager">
              {data.next ? (
                <button disabled={more.busy} onClick={loadMore}>{more.busy ? 'Loading...' : 'Load more'}</button>
              ) : (
                data.items.length > 0 && <span className="muted">That's everything.</span>
              )}
              {more.error && <span className="error">{more.error}</span>}
            </div>
          </>
        )}
      </div>
    </div>
  )
}

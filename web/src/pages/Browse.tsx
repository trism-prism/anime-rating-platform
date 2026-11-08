import { useEffect, useRef, useState } from 'react'
import { api } from '../api'
import SearchBox from '../components/SearchBox'
import Select from '../components/Select'
import SeriesCard from '../components/SeriesCard'
import type { SeriesPage, SeriesSummary } from '../types'

const TAKE = 24
const TYPES = [{ value: '', label: 'All types' }, { value: 'Anime', label: 'Anime' }, { value: 'Manga', label: 'Manga' }]
const GENRES = ['Action', 'Adventure', 'Comedy', 'Drama', 'Fantasy', 'Horror', 'Romance', 'Sci-Fi', 'Slice of Life', 'Sports', 'Supernatural', 'Thriller']
const GENRE_OPTIONS = [{ value: '', label: 'All genres' }, ...GENRES.map((g) => ({ value: g, label: g }))]

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
  // moreBusy and moreError live in data so they belong to this filter key and reset with every first-page load
  const [data, setData] = useState<{ key: string; items: SeriesSummary[]; next: string | null; moreBusy: boolean; moreError: string } | null>(null)
  const [error, setError] = useState('')
  const [tick, setTick] = useState(0) // bump to retry
  const generation = useRef(0) // bumped by every first-page load; a load-more from an older one is dropped

  const key = `${query}|${type}|${genre}`

  useEffect(() => {
    const run = { stale: false }
    generation.current++
    api<SeriesPage>(seriesUrl(query, type, genre, null))
      .then((d) => {
        if (run.stale) return
        setData({ key, items: d.items, next: d.nextCursor, moreBusy: false, moreError: '' })
        setError('')
      })
      .catch((e: Error) => !run.stale && setError(e.message))
    return () => {
      run.stale = true
    }
  }, [query, type, genre, key, tick])

  async function loadMore() {
    if (!data?.next || data.key !== key) return
    const gen = generation.current
    const cursor = data.next
    // still the same page of the same list? (a filter switch and back reloads page one with the same cursor)
    const current = (cur: typeof data | null) => (cur && gen === generation.current && cur.next === cursor ? cur : null)
    setData((cur) => { const c = current(cur); return c ? { ...c, moreBusy: true, moreError: '' } : cur })
    try {
      const d = await api<SeriesPage>(seriesUrl(query, type, genre, cursor))
      setData((cur) => {
        const c = current(cur)
        if (!c) return cur
        const have = new Set(c.items.map((i) => i.id)) // popularity can shift between pages and repeat a series
        return { ...c, items: [...c.items, ...d.items.filter((i) => !have.has(i.id))], next: d.nextCursor, moreBusy: false, moreError: '' }
      })
    } catch (e) {
      setData((cur) => { const c = current(cur); return c ? { ...c, moreBusy: false, moreError: (e as Error).message } : cur })
    }
  }

  const loading = !data || data.key !== key

  return (
    <div>
      <h1>Browse</h1>
      <div className="filters">
        <SearchBox onSubmit={setQuery} />
        <Select label="Type" value={type} options={TYPES} onChange={setType} />
        <Select label="Genre" value={genre} options={GENRE_OPTIONS} onChange={setGenre} />
      </div>

      <div className="results">
        {error && (
          <p className="error">
            {error} <button onClick={() => { setError(''); setTick((t) => t + 1) }}>Retry</button>
          </p>
        )}
        {loading && !error && <p className="muted">Loading</p>}
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
                <button disabled={data.moreBusy} onClick={loadMore}>{data.moreBusy ? 'Loading' : 'Load more'}</button>
              ) : (
                data.items.length > 0 && <span className="muted">That's everything</span>
              )}
              {data.moreError && <span className="error">{data.moreError}</span>}
            </div>
          </>
        )}
      </div>
    </div>
  )
}

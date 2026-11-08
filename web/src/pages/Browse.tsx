import { useEffect, useLayoutEffect, useRef, useState } from 'react'
import { Link, useLocation, useSearchParams } from 'react-router-dom'
import { api } from '../api'
import SearchBox from '../components/SearchBox'
import Select from '../components/Select'
import SeriesCard from '../components/SeriesCard'
import type { SeriesPage, SeriesSummary, TrendingItem } from '../types'
import { useTitle } from '../useTitle'

const TAKE = 24
const RAIL_MAX = 12
const TYPES = [{ value: '', label: 'All types' }, { value: 'Anime', label: 'Anime' }, { value: 'Manga', label: 'Manga' }]
const GENRES = ['Action', 'Adventure', 'Comedy', 'Drama', 'Fantasy', 'Horror', 'Romance', 'Sci-Fi', 'Slice of Life', 'Sports', 'Supernatural', 'Thriller']
const GENRE_OPTIONS = [{ value: '', label: 'All genres' }, ...GENRES.map((g) => ({ value: g, label: g }))]
const SORTS = [{ value: 'popular', label: 'Popular' }, { value: 'top', label: 'Top rated' }, { value: 'newest', label: 'Newest' }, { value: 'az', label: 'A to Z' }]
const SNAPSHOT = 'browse-snapshot' // sessionStorage: the loaded pages and scroll position, so Back from a series lands where you were

type Data = { key: string; items: SeriesSummary[]; next: string | null; total: number; moreBusy: boolean; moreError: string }

const count = (n: number) => `${n.toLocaleString('en-US')} ${n === 1 ? 'title' : 'titles'}`

function seriesUrl(query: string, type: string, genre: string, sort: string, cursor: string | null) {
  const params = new URLSearchParams({ take: String(TAKE), sort })
  if (query) params.set('search', query)
  if (type) params.set('type', type)
  if (genre) params.set('genre', genre)
  if (cursor) params.set('cursor', cursor)
  return `/series?${params}`
}

// storage can be blocked or full; Browse then simply starts at the top
function readSnapshot(loc: string, key: string): { y: number; data: Data } | null {
  try {
    const s = JSON.parse(sessionStorage.getItem(SNAPSHOT) ?? 'null')
    if (s && s.loc === loc && s.data?.key === key && Array.isArray(s.data.items)) return s
  } catch { /* ignore */ }
  return null
}

// Trending now: a row of covers that scrolls sideways (Tab moves through the cards and scrolls them into view)
let railCache: TrendingItem[] = [] // kept for the session so the rail is there at once on Back and the page below does not move
function TrendingRail() {
  const [items, setItems] = useState<TrendingItem[]>(railCache)
  useEffect(() => {
    let stale = false
    api<TrendingItem[]>('/trending?type=Anime')
      .then((l) => { railCache = l.slice(0, RAIL_MAX); if (!stale) setItems(railCache) })
      .catch(() => {}) // no rail beats an error banner
    return () => { stale = true }
  }, [])
  if (items.length === 0) return null
  return (
    <section className="rail-section" aria-labelledby="rail-title">
      <div className="rail-head">
        <h2 id="rail-title">Trending now</h2>
        <Link to="/trending" className="link-quiet">See all</Link>
      </div>
      <ul className="rail">
        {items.map((t, i) => (
          <li key={t.id}>
            <SeriesCard id={t.id} name={t.name} coverUrl={t.coverUrl} type={t.type} average={t.averageOverall} eager={i < 6} />
          </li>
        ))}
      </ul>
    </section>
  )
}

export default function Browse() {
  useTitle('Browse')
  const [params, setParams] = useSearchParams()
  const location = useLocation()
  // the address holds the search, filters and sort, so Back and refresh bring them back
  const query = params.get('q') ?? ''
  const type = TYPES.some((t) => t.value === params.get('type')) ? params.get('type') ?? '' : ''
  const genre = GENRES.includes(params.get('genre') ?? '') ? params.get('genre') ?? '' : ''
  const sort = SORTS.some((s) => s.value === params.get('sort')) ? params.get('sort') ?? 'popular' : 'popular'
  const key = `${query}|${type}|${genre}|${sort}`
  const filtered = !!(query || type || genre)

  const [clearCount, setClearCount] = useState(0) // remounts the search box so its text empties on Clear filters
  const [snap] = useState(() => readSnapshot(location.key, key))
  // moreBusy and moreError live in data so they belong to this filter key and reset with every first-page load
  const [data, setData] = useState<Data | null>(snap ? { ...snap.data, moreBusy: false, moreError: '' } : null)
  const [error, setError] = useState('')
  const [tick, setTick] = useState(0) // bump to retry
  const generation = useRef(0) // bumped by every first-page load; a load-more from an older one is dropped
  const skipKey = useRef(snap?.data.key ?? null) // the restored list stands for this key, so it is not fetched again

  function change(patch: Record<string, string>) {
    const next = new URLSearchParams(params)
    for (const [k, v] of Object.entries(patch)) {
      if (v && !(k === 'sort' && v === 'popular')) next.set(k, v)
      else next.delete(k)
    }
    setParams(next, { replace: true }) // a filter change is not a page to go Back to
  }
  const clear = () => { setClearCount((n) => n + 1); change({ q: '', type: '', genre: '' }) }

  useEffect(() => {
    if (skipKey.current === key) return
    skipKey.current = null
    const run = { stale: false }
    generation.current++
    api<SeriesPage>(seriesUrl(query, type, genre, sort, null))
      .then((d) => {
        if (run.stale) return
        setData({ key, items: d.items, next: d.nextCursor, total: d.total, moreBusy: false, moreError: '' })
        setError('')
      })
      .catch((e: Error) => !run.stale && setError(e.message))
    return () => {
      run.stale = true
    }
  }, [query, type, genre, sort, key, tick])

  // back from a series: same scroll position (the cards keep their size before their pictures load, so the page is tall enough at once)
  useLayoutEffect(() => {
    if (snap) window.scrollTo(0, snap.y)
  }, [snap])

  // remember the list and scroll position for the Back button
  const scrollY = useRef(snap?.y ?? 0)
  const raf = useRef(0)
  const latest = useRef({ data, key, loc: location.key })
  useEffect(() => { latest.current = { data, key, loc: location.key } })
  useEffect(() => {
    const path = window.location.pathname
    const save = () => {
      const { data: d, key: k, loc } = latest.current
      if (!d || d.key !== k) return
      try { sessionStorage.setItem(SNAPSHOT, JSON.stringify({ loc, y: scrollY.current, data: { ...d, moreBusy: false, moreError: '' } })) } catch { /* ignore */ }
    }
    const onScroll = () => {
      if (window.location.pathname !== path) return // the next page is already in place and may have clamped the scroll
      scrollY.current = window.scrollY
      cancelAnimationFrame(raf.current)
      raf.current = requestAnimationFrame(save)
    }
    window.addEventListener('scroll', onScroll, { passive: true })
    return () => {
      window.removeEventListener('scroll', onScroll)
      cancelAnimationFrame(raf.current)
      save()
    }
  }, [snap])

  async function loadMore() {
    if (!data?.next || data.key !== key) return
    const gen = generation.current
    const cursor = data.next
    // still the same page of the same list? (a filter switch and back reloads page one with the same cursor)
    const current = (cur: typeof data | null) => (cur && gen === generation.current && cur.next === cursor ? cur : null)
    setData((cur) => { const c = current(cur); return c ? { ...c, moreBusy: true, moreError: '' } : cur })
    try {
      const d = await api<SeriesPage>(seriesUrl(query, type, genre, sort, cursor))
      setData((cur) => {
        const c = current(cur)
        if (!c) return cur
        const have = new Set(c.items.map((i) => i.id)) // a rating between two pages can shift the order and repeat a series
        return { ...c, items: [...c.items, ...d.items.filter((i) => !have.has(i.id))], next: d.nextCursor, total: d.total, moreBusy: false, moreError: '' }
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
        <SearchBox key={clearCount} onSubmit={(q) => change({ q })} initial={query} />
        <Select label="Type" value={type} options={TYPES} onChange={(v) => change({ type: v })} />
        <Select label="Genre" value={genre} options={GENRE_OPTIONS} onChange={(v) => change({ genre: v })} />
      </div>

      {!filtered && <TrendingRail />}

      <div className="results-bar">
        <p className="results-count" role="status">{!loading && count(data.total)}</p>
        <label className="row">
          <span className="muted">Sort</span>
          <Select label="Sort" value={sort} options={SORTS} onChange={(v) => change({ sort: v })} />
        </label>
      </div>

      <div className="results">
        {error && (
          <p className="error" role="alert">
            {error} <button onClick={() => { setError(''); setTick((t) => t + 1) }}>Retry</button>
          </p>
        )}
        {loading && !error && <p className="muted">Loading</p>}
        {!loading && data.items.length === 0 && (
          <div className="empty">
            <p><strong>{query ? `Nothing matches '${query}'.` : filtered ? 'Nothing matches these filters.' : 'No series yet.'}</strong></p>
            {filtered && <p className="muted">Check the spelling, try fewer words, or remove a filter.</p>}
            <div className="row">
              {filtered && <button type="button" onClick={clear}>Clear filters</button>}
              <Link to="/trending" className="button">See what's trending</Link>
            </div>
          </div>
        )}
        {!loading && data.items.length > 0 && (
          <>
            <h2 className="sr-only">Results</h2>
            <div className="grid">
              {data.items.map((s, i) => (
                <SeriesCard key={s.id} id={s.id} name={s.name} coverUrl={s.coverUrl} type={s.type} average={s.average} ratingCount={s.ratingCount} eager={i < 6} />
              ))}
            </div>
            <div className="pager">
              {data.next ? (
                <>
                  <span className="muted" role="status">Showing {data.items.length.toLocaleString('en-US')} of {data.total.toLocaleString('en-US')}</span>
                  <button disabled={data.moreBusy} onClick={loadMore}>{data.moreBusy ? 'Loading' : 'Load more'}</button>
                </>
              ) : (
                <span className="muted">That's everything</span>
              )}
              {data.moreError && <span className="error">{data.moreError}</span>}
            </div>
          </>
        )}
      </div>
    </div>
  )
}

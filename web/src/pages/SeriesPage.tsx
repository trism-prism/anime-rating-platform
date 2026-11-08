import { useEffect, useLayoutEffect, useRef, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { api } from '../api'
import { useAuth } from '../auth'
import AddToList from '../components/AddToList'
import AlsoRated from '../components/AlsoRated'
import Cover from '../components/Cover'
import Reviews from '../components/Reviews'
import { cleanSynopsis, formatLabel, plural, score, sizeLabel } from '../format'
import { useTitle } from '../useTitle'
import type { MyRating, SeriesDetail, SeriesEntry } from '../types'

const RELATION: Record<string, string> = { SEQUEL: 'Sequel', PREQUEL: 'Prequel', SIDE_STORY: 'Side story' }
const COLLAPSE_AFTER = 6

// the synopsis, cut to about four lines with a Read more button when it is longer than that
function Synopsis({ text, source }: { text: string; source: string | null }) {
  const [open, setOpen] = useState(false)
  const [clipped, setClipped] = useState(false)
  const ref = useRef<HTMLParagraphElement>(null)

  useLayoutEffect(() => {
    const el = ref.current
    if (!el) return
    const measure = () => { if (!open) setClipped(el.scrollHeight > el.clientHeight + 1) }
    measure()
    const ro = new ResizeObserver(measure)
    ro.observe(el)
    return () => ro.disconnect()
  }, [text, open])

  return (
    <div className="series-synopsis">
      <p ref={ref} className={`synopsis${open ? '' : ' clamp'}`}>{text}</p>
      {(clipped || open) && (
        <button type="button" className="link-button" aria-expanded={open} onClick={() => setOpen(!open)}>{open ? 'Show less' : 'Read more'}</button>
      )}
      {source && <p className="muted credit">Source: {source}</p>}
    </div>
  )
}

function EntryRow({ e }: { e: SeriesEntry }) {
  return (
    <li>
      <Cover src={e.coverUrl} name={e.name} className="entry-cover" />
      <div className="entry-main">
        <Link to={`/titles/${e.id}`} className="list-name">{e.name}</Link>
        <div className="muted">{[e.format && formatLabel(e.format), e.year, sizeLabel(e)].filter(Boolean).join(' · ')}</div>
      </div>
      {e.average != null && (
        <span className="score-pill" role="img" title={`${plural(e.ratingCount, 'rating')}`} aria-label={`${score(e.average)} out of 10, from ${plural(e.ratingCount, 'rating')}`}>{score(e.average)}</span>
      )}
    </li>
  )
}

export default function SeriesPage() {
  const { id } = useParams()
  const { user } = useAuth()
  const [state, setState] = useState<{ id: string; series: SeriesDetail } | null>(null)
  const [error, setError] = useState<{ id: string; message: string } | null>(null)
  const [tick, setTick] = useState(0)
  const [mine, setMine] = useState<MyRating[] | null>(null) // the signed-in user's ratings, for the summary
  const [showUnrated, setShowUnrated] = useState<{ id: string; open: boolean } | null>(null)
  useTitle(state && state.id === id ? state.series.name : null)

  useEffect(() => {
    if (!id) return
    let stale = false
    api<SeriesDetail>(`/series/${id}`)
      .then((series) => { if (!stale) { setState({ id, series }); setError(null) } })
      .catch((e: Error & { status?: number }) => !stale && setError({ id, message: e.status === 404 ? 'Series not found.' : e.message }))
    return () => {
      stale = true
    }
  }, [id, tick])

  useEffect(() => {
    if (!user) return
    let stale = false
    api<MyRating[]>('/me/ratings').then((r) => !stale && setMine(r)).catch(() => {})
    return () => { stale = true }
  }, [user])

  if (error && error.id === id) {
    return (
      <p className="error">
        {error.message} <button onClick={() => { setError(null); setTick((t) => t + 1) }}>Retry</button>
      </p>
    )
  }
  if (!state || state.id !== id) {
    return (
      <div aria-busy="true" className="series-head">
        <span className="cover-box series-cover" />
        <div className="series-info"><p className="muted">Loading</p></div>
      </div>
    )
  }

  const s = state.series
  const crossKind = s.type === 'Anime' ? 'manga' : 'anime'
  const synopsis = cleanSynopsis(s.synopsis)
  const format = s.format && formatLabel(s.format).toLowerCase() !== s.type.toLowerCase() ? formatLabel(s.format) : ''

  const rated = s.entries.filter((e) => e.average != null)
  const unrated = s.entries.filter((e) => e.average == null)
  // with nothing rated there is nothing above to make the list shorter, so it stays open
  const collapsible = unrated.length > COLLAPSE_AFTER && rated.length > 0
  const unratedOpen = !collapsible || (showUnrated?.id === id && showUnrated.open)

  // related series as chips: one per series, the relation in plain words
  const related = new Map<number, { seriesId: number; name: string; label: string }>()
  for (const g of s.related) {
    const label = RELATION[g.type] ?? 'Related'
    for (const r of g.items) {
      if (r.seriesId === s.id) continue // never a chip to the page you are on
      const have = related.get(r.seriesId)
      if (!have || (have.label === 'Related' && label !== 'Related')) related.set(r.seriesId, { seriesId: r.seriesId, name: r.seriesName, label })
    }
  }

  const entryIds = new Set(s.entries.map((e) => e.id))
  const myScores = (mine ?? []).filter((r) => r.volume == null && entryIds.has(r.titleId)).map((r) => r.overall)
  const myAverage = myScores.length ? myScores.reduce((a, b) => a + b, 0) / myScores.length : null

  return (
    <div>
      <div className="series-head">
        <Cover src={s.coverUrl} name={s.name} eager className="series-cover" />
        <div className="series-info">
          <h1>{s.name}</h1>
          <div className="badges">
            <span className="badge-kind">{s.type}</span>
            {format && <span className="badge-kind">{format}</span>}
            {s.entries.length > 1 && <span className="muted">{plural(s.entries.length, 'entry', 'entries')}</span>}
          </div>
          {s.genres.length > 0 && (
            <div className="tags">
              {s.genres.map((g) => <span key={g} className="tag">{g}</span>)}
            </div>
          )}
          {s.crossLinks.length > 0 && (
            <p>
              Also as {crossKind}:{' '}
              {s.crossLinks.map((c, i) => (
                <span key={c.seriesId}>
                  {i > 0 && ', '}
                  <Link to={`/series/${c.seriesId}`} className="link">{c.name}</Link>
                </span>
              ))}
            </p>
          )}
        </div>

        <div className="series-score">
          {s.average != null ? (
            <div>
              <div className="series-score-main">
                <span className="num series-score-num">{score(s.average)}</span>
                <span className="series-score-of">/ 10</span>
              </div>
              <div className="muted">from <span className="num">{s.ratingCount}</span> {s.ratingCount === 1 ? 'rating' : 'ratings'}</div>
            </div>
          ) : (
            <div className="series-score-none">Not rated yet</div>
          )}
          {user && mine && (
            <div className="series-mine">
              <div className="muted">Your ratings</div>
              {myAverage != null ? (
                <div>
                  <span className="num series-mine-num">{score(myAverage)}</span>
                  <span className="muted"> average, {myScores.length} of {s.entries.length} {s.entries.length === 1 ? 'entry' : 'entries'}</span>
                </div>
              ) : (
                <div className="muted">None yet</div>
              )}
            </div>
          )}
        </div>

        <div className="series-body">
          {synopsis.text ? <Synopsis key={s.id} text={synopsis.text} source={synopsis.source} /> : <p className="muted">No synopsis.</p>}
        </div>
      </div>

      <AddToList seriesId={s.id} />

      <h2>In this series</h2>
      {rated.length > 0 && (
        <ul className="entries">
          {rated.map((e) => <EntryRow key={e.id} e={e} />)}
        </ul>
      )}
      {unrated.length > 0 && (
        <>
          {rated.length > 0 && (
            collapsible ? (
              <div className="entries-sub">
                <button type="button" className="expander" aria-expanded={unratedOpen} onClick={() => setShowUnrated({ id: id ?? '', open: !unratedOpen })}>
                  {unratedOpen ? 'Hide unrated entries' : `Show ${unrated.length} unrated entries`} <svg width="16" height="16" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d="M4 6.5l4 4 4-4" /></svg>
                </button>
              </div>
            ) : (
              <h3 className="entries-sub">Not rated yet</h3>
            )
          )}
          {unratedOpen && (
            <ul className="entries">
              {unrated.map((e) => <EntryRow key={e.id} e={e} />)}
            </ul>
          )}
        </>
      )}

      <AlsoRated key={`also-${s.id}`} path={`/series/${s.id}`} />

      {related.size > 0 && (
        <>
          <h2>Related</h2>
          <ul className="chips">
            {[...related.values()].map((r) => (
              <li key={r.seriesId}>
                <Link to={`/series/${r.seriesId}`} className="chip">
                  <span className="chip-rel">{r.label}</span>
                  <span className="chip-name">{r.name}</span>
                </Link>
              </li>
            ))}
          </ul>
        </>
      )}

      <Reviews key={`reviews-${s.id}`} path={`/series/${s.id}`} showTitle />
    </div>
  )
}

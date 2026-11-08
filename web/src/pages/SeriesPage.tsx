import { useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { api } from '../api'
import AddToList from '../components/AddToList'
import AlsoRated from '../components/AlsoRated'
import Reviews from '../components/Reviews'
import { formatLabel, plural, score, sentence, sizeLabel } from '../format'
import { useTitle } from '../useTitle'
import type { SeriesDetail } from '../types'

export default function SeriesPage() {
  const { id } = useParams()
  const [state, setState] = useState<{ id: string; series: SeriesDetail } | null>(null)
  const [error, setError] = useState<{ id: string; message: string } | null>(null)
  const [tick, setTick] = useState(0)
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

  if (error && error.id === id) {
    return (
      <p className="error">
        {error.message} <button onClick={() => { setError(null); setTick((t) => t + 1) }}>Retry</button>
      </p>
    )
  }
  if (!state || state.id !== id) {
    return (
      <div aria-busy="true">
        <div className="detail">
          <div className="cover skeleton noimg" />
          <div className="skeleton-text"><p className="muted">Loading</p></div>
        </div>
      </div>
    )
  }

  const s = state.series
  const crossKind = s.type === 'Anime' ? 'manga' : 'anime'

  return (
    <div>
      <div className="detail">
        {s.coverUrl ? <img className="cover" src={s.coverUrl} alt="" /> : <div className="cover noimg" />}
        <div>
          <h1>{s.name}</h1>
          <p className="muted">
            {s.type} · {plural(s.entries.length, 'entry', 'entries')} ·{' '}
            {s.average != null
              ? <><span className="nowrap"><span className="num">{score(s.average)}</span> / 10</span> from <span className="num">{s.ratingCount}</span> {s.ratingCount === 1 ? 'rating' : 'ratings'}</>
              : 'Not rated yet'}
          </p>
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
      </div>

      <AddToList seriesId={s.id} />

      <h2>In this series</h2>
      <ol className="entries">
        {s.entries.map((e) => (
          <li key={e.id}>
            <span className="entry-pos muted">#{e.position}</span>
            {e.coverUrl ? <img src={e.coverUrl} alt="" loading="lazy" /> : <div className="noimg" />}
            <div className="entry-main">
              <Link to={`/titles/${e.id}`} className="list-name">{e.name}</Link>
              <div className="muted">{[e.format && formatLabel(e.format), e.year, sizeLabel(e)].filter(Boolean).join(' · ')}</div>
            </div>
            <div className="entry-score">
              {e.average != null ? (
                <>
                  <strong>{score(e.average)}</strong>
                  <div className="muted"><span className="num">{e.ratingCount}</span> {e.ratingCount === 1 ? 'rating' : 'ratings'}</div>
                </>
              ) : <div className="muted">Not rated yet</div>}
            </div>
          </li>
        ))}
      </ol>

      <AlsoRated key={`also-${s.id}`} path={`/series/${s.id}`} />

      {s.related.length > 0 && (
        <>
          <h2>Related</h2>
          <p className="muted">Linked to this series but not part of it.</p>
          {s.related.map((g) => (
            <div key={g.type} className="related-group">
              <h3>{sentence(g.type)}</h3>
              <ul>
                {g.items.map((r) => (
                  <li key={r.titleId}>
                    <Link to={`/titles/${r.titleId}`} className="link">{r.name}</Link>
                    {r.seriesName !== r.name && (
                      <span className="muted"> in <Link to={`/series/${r.seriesId}`} className="link">{r.seriesName}</Link></span>
                    )}
                  </li>
                ))}
              </ul>
            </div>
          ))}
        </>
      )}

      <Reviews key={`reviews-${s.id}`} path={`/series/${s.id}`} showTitle />
    </div>
  )
}

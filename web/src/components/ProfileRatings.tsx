import { useCallback, useEffect, useRef, useState } from 'react'
import { Link } from 'react-router-dom'
import { api } from '../api'
import { timeAgo } from '../time'
import type { ProfileRating, ProfileRatingPage } from '../types'

// what this viewer may see of a user's ratings (the API applies each rating's audience); keyset, 20 per page
export default function ProfileRatings({ username }: { username: string }) {
  const [items, setItems] = useState<ProfileRating[] | null>(null)
  const [next, setNext] = useState<number | null>(null)
  const [error, setError] = useState('')
  const [loadingMore, setLoadingMore] = useState(false)
  const gen = useRef(0)
  const base = `/users/${encodeURIComponent(username)}/ratings`

  const loadFirst = useCallback(() => {
    const g = ++gen.current
    api<ProfileRatingPage>(base)
      .then((p) => { if (g === gen.current) { setItems(p.items); setNext(p.nextCursor); setError('') } })
      .catch((e: Error) => { if (g === gen.current) setError(e.message) })
  }, [base])

  useEffect(() => { loadFirst() }, [loadFirst])

  async function loadMore() {
    if (next === null || loadingMore) return
    const g = gen.current
    setLoadingMore(true)
    try {
      const p = await api<ProfileRatingPage>(`${base}?cursor=${next}`)
      if (g !== gen.current) return
      setItems((l) => {
        const have = new Set((l ?? []).map((x) => x.ratingId))
        return [...(l ?? []), ...p.items.filter((x) => !have.has(x.ratingId))]
      })
      setNext(p.nextCursor)
    } catch (e) {
      setError((e as Error).message)
    } finally {
      setLoadingMore(false)
    }
  }

  return (
    <section aria-labelledby="ratings-title">
      <h2 id="ratings-title">Ratings</h2>
      {error && <p className="error" role="alert">{error} <button onClick={() => { setError(''); loadFirst() }}>Retry</button></p>}
      {!items && !error && <p className="muted">Loading...</p>}
      {items && items.length === 0 && <p className="muted">Nothing to show.</p>}
      {items && items.length > 0 && (
        <ul className="people profile-ratings">
          {items.map((r) => (
            <li key={r.ratingId}>
              <div className="profile-rating-main">
                <Link to={`/titles/${r.titleId}`} className="link"><strong>{r.titleName}</strong></Link>
                <span className="muted">
                  {r.seriesName !== r.titleName && <> · <Link to={`/series/${r.seriesId}`} className="link-quiet">{r.seriesName}</Link></>}
                  {r.volume != null && ` · Volume ${r.volume}`} · {timeAgo(r.createdAt)}
                </span>
                {r.snippet && <p className="review-text">{r.snippet}</p>}
                {r.hasReview && r.spoiler && <p className="muted">Review contains spoilers.</p>}
                {r.hasReview && <Link to={`/reviews/${r.ratingId}`} className="link">Read review</Link>}
              </div>
              <strong>{r.overall} / 10</strong>
            </li>
          ))}
        </ul>
      )}
      {next !== null && <button onClick={loadMore} disabled={loadingMore}>{loadingMore ? 'Loading...' : 'Load more'}</button>}
    </section>
  )
}

import { useCallback, useEffect, useRef, useState } from 'react'
import { Link, Navigate } from 'react-router-dom'
import { api } from '../api'
import { useAuth } from '../auth'
import { timeAgo } from '../time'
import type { FeedItem, FeedPage } from '../types'
import Avatar from '../components/Avatar'
import FriendsSection from '../components/FriendsSection'

// recent ratings and reviews by friends; the API applies each rating's audience, keyset paged
export default function Feed() {
  const { user, loading } = useAuth()
  const [items, setItems] = useState<FeedItem[] | null>(null)
  const [next, setNext] = useState<number | null>(null)
  const [error, setError] = useState('')
  const [loadingMore, setLoadingMore] = useState(false)
  const gen = useRef(0)

  const loadFirst = useCallback(() => {
    const g = ++gen.current
    api<FeedPage>('/me/feed')
      .then((p) => { if (g === gen.current) { setItems(p.items); setNext(p.nextCursor); setError('') } })
      .catch((e: Error) => { if (g === gen.current) setError(e.message) })
  }, [])

  useEffect(() => { if (user) loadFirst() }, [user, loadFirst])

  async function loadMore() {
    if (next === null || loadingMore) return
    const g = gen.current
    setLoadingMore(true)
    try {
      const p = await api<FeedPage>(`/me/feed?cursor=${next}`)
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

  if (loading) return <p className="muted">Loading</p>
  if (!user) return <Navigate to="/login" replace />

  return (
    <div>
      <h1>Friends</h1>
      <FriendsSection />
      <section aria-labelledby="feed-title">
      <h2 id="feed-title">Activity</h2>
      {error && <p className="error" role="alert">{error} <button onClick={() => { setError(''); loadFirst() }}>Retry</button></p>}
      {!items && !error && <p className="muted">Loading</p>}
      {items && items.length === 0 && (
        <p className="muted">Nothing yet. When your friends rate or review something it shows up here.</p>
      )}
      {items && items.length > 0 && (
        <ul className="people profile-ratings">
          {items.map((r) => (
            <li key={r.ratingId}>
              <div className="profile-rating-main">
                <span>
                  <span className="who"><Avatar userName={r.userName} small /><Link to={`/u/${encodeURIComponent(r.userName)}`} className="link"><strong>{r.userName}</strong></Link></span>
                  {' rated '}
                  <Link to={`/titles/${r.titleId}`} className="link">{r.titleName}</Link>
                  <span className="muted">{r.volume != null && ` · Volume ${r.volume}`} · {timeAgo(r.createdAt)}</span>
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
      {next !== null && <button onClick={loadMore} disabled={loadingMore}>{loadingMore ? 'Loading' : 'Load more'}</button>}
      </section>
    </div>
  )
}

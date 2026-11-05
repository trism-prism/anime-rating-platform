import { useCallback, useEffect, useRef, useState } from 'react'
import { api } from '../api'
import type { ReviewItem, ReviewPage } from '../types'
import ReviewCard from './ReviewCard'

type Sort = 'helpful' | 'newest'

// the Reviews section of a title or series page: path is "/titles/5" or "/series/3"; `refresh` changes when the page's own ratings did
export default function Reviews({ path, showTitle, refresh = 0 }: { path: string; showTitle: boolean; refresh?: number }) {
  const [sort, setSort] = useState<Sort>('helpful')
  const [items, setItems] = useState<ReviewItem[] | null>(null)
  const [next, setNext] = useState<string | null>(null)
  const [error, setError] = useState('')
  const [moreError, setMoreError] = useState('')
  const [loadingMore, setLoadingMore] = useState(false)
  const gen = useRef(0) // bumped on every reload, so a late answer from an older sort or page is dropped

  // state is set only when the answer arrives; an answer from an older call (another sort) is dropped
  const loadFirst = useCallback(() => {
    const g = ++gen.current
    api<ReviewPage>(`${path}/reviews?sort=${sort}`)
      .then((p) => { if (g === gen.current) { setItems(p.items); setNext(p.nextCursor); setError(''); setMoreError('') } })
      .catch((e: Error) => { if (g === gen.current) setError(e.message) })
  }, [path, sort])

  // a changed `refresh` (the user saved a rating) reloads in place, without blanking the list
  useEffect(() => { loadFirst() }, [loadFirst, refresh])

  const retry = () => { setError(''); setItems(null); loadFirst() }
  const pick = (s: Sort) => { setSort(s); setItems(null); setNext(null); setError(''); setMoreError('') }

  async function loadMore() {
    if (!next || loadingMore) return
    const g = gen.current
    setLoadingMore(true)
    setMoreError('')
    try {
      const p = await api<ReviewPage>(`${path}/reviews?sort=${sort}&cursor=${encodeURIComponent(next)}`)
      if (g !== gen.current) return
      setItems((l) => {
        const have = new Set((l ?? []).map((x) => x.id))
        return [...(l ?? []), ...p.items.filter((x) => !have.has(x.id))]
      })
      setNext(p.nextCursor)
    } catch (e) {
      setMoreError((e as Error).message)
    } finally {
      setLoadingMore(false)
    }
  }

  // a card reports its new state, a patch to apply to the current one, or null when it is gone; applying by id makes a late answer harmless
  const update = useCallback((id: number, r: ReviewItem | ((x: ReviewItem) => ReviewItem) | null) => {
    setItems((l) => l && (r ? l.map((x) => (x.id === id ? (typeof r === 'function' ? r(x) : r) : x)) : l.filter((x) => x.id !== id)))
  }, [])

  return (
    <section aria-labelledby="reviews-title">
      <div className="row reviews-head">
        <h2 id="reviews-title">Reviews</h2>
        <label className="row">
          <span className="muted">Sort</span>
          <select value={sort} onChange={(e) => pick(e.target.value as Sort)}>
            <option value="helpful">Most helpful</option>
            <option value="newest">Newest</option>
          </select>
        </label>
      </div>
      <div className="reviews" aria-busy={!items && !error}>
        {error && <p className="error" role="alert">{error} <button onClick={retry}>Retry</button></p>}
        {!items && !error && <div className="skeleton review-skeleton" aria-label="Loading reviews" />}
        {items && items.length === 0 && <p className="muted">No reviews yet.</p>}
        {items?.map((r) => <ReviewCard key={r.id} review={r} showTitle={showTitle} onChange={update} />)}
      </div>
      {moreError && <p className="error" role="alert">{moreError} <button onClick={loadMore}>Retry</button></p>}
      {items && next && !moreError && <button onClick={loadMore} disabled={loadingMore}>{loadingMore ? 'Loading...' : 'Load more'}</button>}
    </section>
  )
}

import { useCallback, useEffect, useRef, useState } from 'react'
import { Link } from 'react-router-dom'
import { api } from '../api'
import { timeAgo } from '../time'
import type { ReportItem, ReportPage } from '../types'

// the open reports, oldest first, keyset paged; every action reloads the queue because removing content settles other reports too
export default function AdminReports() {
  const [items, setItems] = useState<ReportItem[] | null>(null)
  const [next, setNext] = useState<number | null>(null)
  const [error, setError] = useState('')
  const [actionError, setActionError] = useState('')
  const [busy, setBusy] = useState<number | null>(null)
  const [loadingMore, setLoadingMore] = useState(false)
  const gen = useRef(0)

  const loadFirst = useCallback(() => {
    const g = ++gen.current
    api<ReportPage>('/admin/reports')
      .then((p) => { if (g === gen.current) { setItems(p.items); setNext(p.nextCursor); setError('') } })
      .catch((e: Error) => { if (g === gen.current) setError(e.message) })
  }, [])

  useEffect(() => { loadFirst() }, [loadFirst])

  async function loadMore() {
    const g = gen.current
    setLoadingMore(true)
    try {
      const p = await api<ReportPage>(`/admin/reports?cursor=${next}`)
      if (g !== gen.current) return
      setItems((l) => {
        const have = new Set((l ?? []).map((x) => x.id))
        return [...(l ?? []), ...p.items.filter((x) => !have.has(x.id))]
      })
      setNext(p.nextCursor)
    } catch (e) {
      setError((e as Error).message)
    } finally {
      setLoadingMore(false)
    }
  }

  async function resolve(r: ReportItem, action: 'dismiss' | 'remove') {
    if (busy !== null) return
    setBusy(r.id)
    setActionError('')
    try {
      await api(`/admin/reports/${r.id}/${action}`, { method: 'POST' })
    } catch (e) {
      setActionError((e as Error).message)
    } finally {
      setBusy(null)
      loadFirst() // also after a failure: someone else may have settled it
    }
  }

  return (
    <section aria-labelledby="reports-title">
      <h2 id="reports-title">Reports</h2>
      {error && <p className="error" role="alert">{error} <button onClick={() => { setError(''); loadFirst() }}>Retry</button></p>}
      {actionError && <p className="error" role="alert">{actionError}</p>}
      {!items && !error && <p className="muted">Loading</p>}
      {items && items.length === 0 && <p className="muted">No open reports.</p>}
      {items && items.length > 0 && (
        <ul className="people">
          {items.map((r) => (
            <li key={r.id}>
              <div className="profile-rating-main">
                <span>
                  <strong>{r.reason}</strong> · {r.target.toLowerCase()} by{' '}
                  <Link to={`/u/${encodeURIComponent(r.targetUser)}`} className="link">{r.targetUser}</Link>
                  <span className="muted"> · reported by {r.reporter} · {timeAgo(r.createdAt)}</span>
                </span>
                <p className="review-text">{r.snapshot}</p>
                {r.note && <p className="muted">Note: {r.note}</p>}
                {r.target === 'Review' && <Link to={`/reviews/${r.targetId}`} className="link">Open review</Link>}
              </div>
              <span className="row">
                <button disabled={busy !== null} onClick={() => resolve(r, 'dismiss')}>Dismiss</button>
                {r.target !== 'Profile' && <button disabled={busy !== null} onClick={() => resolve(r, 'remove')}>Remove content</button>}
              </span>
            </li>
          ))}
        </ul>
      )}
      {next !== null && <button onClick={loadMore} disabled={loadingMore}>{loadingMore ? 'Loading' : 'Load more'}</button>}
    </section>
  )
}

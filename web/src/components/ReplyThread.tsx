import { useCallback, useEffect, useRef, useState, type FormEvent } from 'react'
import { Link, useLocation } from 'react-router-dom'
import { api, ApiError } from '../api'
import { useAuth } from '../auth'
import { timeAgo } from '../time'
import type { ReplyItem, ReplyPage } from '../types'
import Spoiler from './Spoiler'

const MAX = 1000

// the flat thread under one review, oldest first; mounted only while it is open
export default function ReplyThread({ reviewId, reviewMine, onCount, onResync }: {
  reviewId: number
  reviewMine: boolean
  onCount: (delta: number) => void // a reply was added or removed here
  onResync: () => void // something failed or the review is gone: the card should look again
}) {
  const { user, emailVerified } = useAuth()
  const { pathname } = useLocation()
  const [items, setItems] = useState<ReplyItem[] | null>(null)
  const [next, setNext] = useState<number | null>(null)
  const [error, setError] = useState('')
  const [loadingMore, setLoadingMore] = useState(false)
  const [text, setText] = useState('')
  const [spoiler, setSpoiler] = useState(false)
  const [posting, setPosting] = useState(false)
  const [formError, setFormError] = useState('')
  const [deleting, setDeleting] = useState<number[]>([])
  const gen = useRef(0) // bumped on every reload, so a late answer from before it is dropped

  const loadFirst = useCallback(() => {
    const g = ++gen.current
    api<ReplyPage>(`/reviews/${reviewId}/replies`)
      .then((p) => { if (g === gen.current) { setItems(p.items); setNext(p.nextCursor); setError('') } })
      .catch((e: Error) => { if (g === gen.current) setError(e instanceof ApiError && e.status === 404 ? 'This review is no longer available.' : e.message) })
  }, [reviewId])

  useEffect(() => { loadFirst() }, [loadFirst])

  async function loadMore() {
    const g = gen.current
    setLoadingMore(true)
    try {
      const p = await api<ReplyPage>(`/reviews/${reviewId}/replies?cursor=${next}`)
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

  async function post(e: FormEvent) {
    e.preventDefault()
    if (posting) return // a double click must not send twice
    const body = text.trim()
    if (!body) return
    setPosting(true)
    setFormError('')
    try {
      const r = await api<ReplyItem>(`/reviews/${reviewId}/replies`, { method: 'POST', body: JSON.stringify({ body, spoiler }) })
      setText('')
      setSpoiler(false)
      onCount(1)
      // the newest reply belongs at the end: show it now only if every older one is already on screen
      if (next === null) setItems((l) => [...(l ?? []), r])
    } catch (err) {
      setFormError((err as Error).message)
      if (err instanceof ApiError && err.status === 404) onResync()
    } finally {
      setPosting(false)
    }
  }

  async function remove(r: ReplyItem) {
    if (deleting.includes(r.id)) return
    setDeleting((l) => [...l, r.id])
    setFormError('')
    try {
      // the reply's author and the review's author use the normal route; anyone else who may delete is an admin
      await api(r.mine || reviewMine ? `/replies/${r.id}` : `/admin/replies/${r.id}`, { method: 'DELETE' })
      setItems((l) => l && l.filter((x) => x.id !== r.id))
      onCount(-1)
    } catch (err) {
      setFormError((err as Error).message)
      loadFirst() // it may be gone already or no longer ours: show what is really there
      onResync()
    } finally {
      setDeleting((l) => l.filter((id) => id !== r.id))
    }
  }

  return (
    <div className="thread" role="region" aria-label="Replies">
      {error && <p className="error" role="alert">{error} <button onClick={() => { setError(''); loadFirst() }}>Retry</button></p>}
      {!items && !error && <p className="muted">Loading replies...</p>}
      {items && items.length === 0 && <p className="muted">No replies yet.</p>}
      {items && items.length > 0 && (
        <ul className="replies">
          {items.map((r) => (
            <li key={r.id}>
              <div className="reply-head">
                <Link to={`/u/${encodeURIComponent(r.userName)}`} className="link"><strong>{r.userName}</strong></Link>
                <span className="muted"> · {timeAgo(r.createdAt)}</span>
                {r.canDelete && (
                  <button className="link-button reply-delete" disabled={deleting.includes(r.id)} onClick={() => remove(r)} aria-label={`Delete reply by ${r.userName}`}>
                    Delete
                  </button>
                )}
              </div>
              {r.spoiler ? <Spoiler what="reply"><p className="review-text">{r.body}</p></Spoiler> : <p className="review-text">{r.body}</p>}
            </li>
          ))}
        </ul>
      )}
      {next !== null && <button onClick={loadMore} disabled={loadingMore}>{loadingMore ? 'Loading...' : 'Load more replies'}</button>}

      {!user && <p className="muted"><Link to="/login" className="link">Log in</Link> to reply.</p>}
      {user && !emailVerified && (
        <p className="muted"><Link to="/verify" state={{ from: pathname }} className="link">Verify your email</Link> to reply.</p>
      )}
      {user && emailVerified && (
        <form className="reply-form" onSubmit={post}>
          <textarea
            aria-label="Write a reply"
            placeholder="Write a reply"
            rows={2}
            maxLength={MAX}
            value={text}
            onChange={(e) => setText(e.target.value)}
          />
          <div className="row">
            <label className="check"><input type="checkbox" checked={spoiler} onChange={(e) => setSpoiler(e.target.checked)} /> Contains spoilers</label>
            <span className="muted count">{text.length}/{MAX}</span>
            <button type="submit" disabled={posting || !text.trim()}>{posting ? 'Posting...' : 'Reply'}</button>
          </div>
        </form>
      )}
      {formError && <p className="error" role="alert">{formError}</p>}
    </div>
  )
}

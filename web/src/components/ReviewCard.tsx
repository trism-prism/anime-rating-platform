import { useState } from 'react'
import { Link } from 'react-router-dom'
import { api, ApiError } from '../api'
import { useAuth } from '../auth'
import { score } from '../format'
import { timeAgo } from '../time'
import { REACTIONS, type ReactionKind, type ReactionState, type ReviewItem } from '../types'
import UserLink from './UserLink'
import ConfirmDialog from './ConfirmDialog'
import ReplyThread from './ReplyThread'
import ReportButton from './ReportButton'
import Spoiler from './Spoiler'

const countOf = (r: ReviewItem, k: ReactionKind) => (k === 'Helpful' ? r.helpful : k === 'Unhelpful' ? r.unhelpful : r.funny)

// one review with reactions and a collapsible reply thread. onChange gets the updated review, a patch for the parent's current copy, or null once it is gone.
export default function ReviewCard({ review, showTitle, defaultOpen = false, onChange }: {
  review: ReviewItem
  showTitle: boolean
  defaultOpen?: boolean
  onChange: (id: number, next: ReviewItem | ((r: ReviewItem) => ReviewItem) | null) => void
}) {
  const { user, isAdmin } = useAuth()
  const [open, setOpen] = useState(defaultOpen)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [confirmRemove, setConfirmRemove] = useState(false)
  const id = review.id

  // what the server has now; 404 means the review is gone or no longer visible to us
  async function resync() {
    try {
      onChange(id, await api<ReviewItem>(`/reviews/${id}`))
    } catch (e) {
      if (e instanceof ApiError && e.status === 404) onChange(id, null)
    }
  }

  async function react(kind: ReactionKind) {
    if (busy) return // a double click must not send the second request
    setBusy(true)
    setError('')
    try {
      // clicking your active reaction takes it back
      const clear = review.myReaction === kind
      const r = await api<ReactionState>(`/reviews/${id}/reaction`, clear ? { method: 'DELETE' } : { method: 'PUT', body: JSON.stringify({ kind }) })
      onChange(id, (x) => ({ ...x, ...r }))
    } catch (e) {
      setError((e as Error).message)
      await resync()
    } finally {
      setBusy(false)
    }
  }

  async function removeText() {
    setBusy(true)
    setError('')
    try {
      await api(`/admin/reviews/${id}/text`, { method: 'DELETE' })
      onChange(id, null)
    } catch (e) {
      setError((e as Error).message)
      await resync()
    } finally {
      setBusy(false)
      setConfirmRemove(false)
    }
  }

  const text = <p className="review-text">{review.reviewText}</p>
  const why = !user ? 'Log in to react' : review.mine ? "You can't react to your own review" : undefined

  return (
    <article className="review" id={`review-${id}`}>
      <div className="review-head">
        <UserLink name={review.userName} />
        <span className="muted">
          {showTitle && <> · <Link to={`/titles/${review.titleId}`} className="link-quiet">{review.titleName}</Link></>}
          {review.volume != null && ` · Volume ${review.volume}`} · <span className="nowrap">{score(review.overall)} / 10</span> · {timeAgo(review.createdAt)}
        </span>
      </div>
      {review.spoiler ? <Spoiler what="review">{text}</Spoiler> : text}

      <div className="review-actions">
        <div className="reactions" role="group" aria-label="React to this review">
          {REACTIONS.map((k) => (
            <button
              key={k}
              aria-pressed={review.myReaction === k}
              aria-label={`${k}, ${countOf(review, k)}`}
              className={review.myReaction === k ? 'on' : ''}
              disabled={busy || !user || review.mine}
              title={why}
              onClick={() => react(k)}
            >
              {k} <span className="count">{countOf(review, k)}</span>
            </button>
          ))}
        </div>
        <button className="link-button" aria-expanded={open} onClick={() => setOpen(!open)}>
          {review.replyCount} {review.replyCount === 1 ? 'reply' : 'replies'}{open ? ' (hide)' : ''}
        </button>
        {user && !review.mine && <ReportButton path={`/reviews/${id}/report`} what="review" />}
        {isAdmin && <button className="link-button" disabled={busy} onClick={() => setConfirmRemove(true)}>Remove text</button>}
      </div>
      {user && why && <p className="muted hint">{why}</p>}
      {error && <p className="error" role="alert">{error}</p>}

      {open && (
        <ReplyThread
          reviewId={id}
          reviewMine={review.mine}
          onCount={(d) => onChange(id, (x) => ({ ...x, replyCount: Math.max(0, x.replyCount + d) }))}
          onResync={resync}
        />
      )}

      <ConfirmDialog open={confirmRemove} title="Remove this review's text?" confirmLabel="Remove" busy={busy}
        onConfirm={removeText} onCancel={() => setConfirmRemove(false)}>
        <p>The text goes for everyone. Its replies and reactions stay in the database but nothing shows them. The score stays.</p>
      </ConfirmDialog>
    </article>
  )
}

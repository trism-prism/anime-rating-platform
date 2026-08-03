import { useCallback, useEffect, useRef, useState, type KeyboardEvent } from 'react'
import { useNavigate } from 'react-router-dom'
import { api } from '../api'
import { refreshPending } from '../pendingRequests'
import { timeAgo } from '../time'
import type { Notification, NotificationPage } from '../types'
import Avatar from './Avatar'

const icon = { width: 16, height: 16, viewBox: '0 0 16 16', fill: 'none', stroke: 'currentColor', strokeWidth: 1.5, strokeLinecap: 'round', strokeLinejoin: 'round', 'aria-hidden': true } as const

const BellIcon = () => (
  <svg {...icon}>
    <path d="M3.5 11.5h9l-1.2-1.6V7a3.3 3.3 0 0 0-6.6 0v2.9zM6.6 13.6a1.5 1.5 0 0 0 2.8 0" />
  </svg>
)
const CheckIcon = () => (
  <svg {...icon}>
    <path d="M3 8.5l3.2 3L13 4.5" />
  </svg>
)
const CrossIcon = () => (
  <svg {...icon}>
    <path d="M4 4l8 8M12 4l-8 8" />
  </svg>
)

const TEXT: Record<Notification['type'], string> = {
  FriendRequest: 'sent you a friend request',
  FriendAccepted: 'accepted your friend request',
  Reply: 'replied to your review',
  Reaction: 'reacted to your review',
  ReportOutcome: '',
}
// a report outcome has no other person: its actor is the reporter
const line = (n: Notification) => n.type === 'ReportOutcome'
  ? <>{n.outcome === 'Removed' ? 'The content you reported was removed.' : 'Your report was reviewed and dismissed.'}</>
  : <><strong>{n.actor}</strong> {TEXT[n.type]}</>

// bell with an unread badge and a dropdown of notifications; rendered only while someone is signed in
export default function NotificationBell() {
  const navigate = useNavigate()
  const [unread, setUnread] = useState(0)
  const [open, setOpen] = useState(false)
  const [items, setItems] = useState<Notification[] | null>(null)
  const [next, setNext] = useState<number | null>(null)
  const [error, setError] = useState('')
  const [actionError, setActionError] = useState('')
  const [busy, setBusy] = useState(false) // loading more
  const [answering, setAnswering] = useState<number[]>([]) // requests being accepted or declined right now
  const inFlight = useRef(new Set<number>()) // the same, readable at once (state lags a fast double click)
  const gen = useRef(0) // bumped on every reload, so a late "load older" answer from before it is dropped
  const box = useRef<HTMLDivElement>(null)
  const button = useRef<HTMLButtonElement>(null)

  const loadCount = useCallback(() => {
    api<{ count: number }>('/me/notifications/unread-count').then((r) => setUnread(r.count)).catch(() => {}) // a failed poll is retried next minute
  }, [])

  // poll once a minute, only while this tab is visible; coming back to the tab refreshes at once
  useEffect(() => {
    loadCount()
    const tick = () => { if (document.visibilityState === 'visible') loadCount() }
    const id = setInterval(tick, 60_000)
    document.addEventListener('visibilitychange', tick)
    return () => {
      clearInterval(id)
      document.removeEventListener('visibilitychange', tick)
    }
  }, [loadCount])

  // fetches the first page; `quiet` keeps the current list on screen meanwhile (used to resync after a failed action)
  const loadFirst = useCallback((quiet = false) => {
    const g = ++gen.current
    if (!quiet) {
      setError('')
      setItems(null)
    }
    api<NotificationPage>('/me/notifications')
      .then((p) => { if (g === gen.current) { setItems(p.items); setNext(p.nextCursor); setError('') } })
      .catch((e: Error) => { if (g === gen.current && !quiet) setError(e.message) })
    loadCount()
  }, [loadCount])

  useEffect(() => {
    if (!open) return
    const onDown = (e: MouseEvent) => {
      if (!box.current?.contains(e.target as Node)) setOpen(false)
    }
    document.addEventListener('mousedown', onDown)
    return () => document.removeEventListener('mousedown', onDown)
  }, [open])

  async function loadMore() {
    const g = gen.current
    setBusy(true)
    try {
      const p = await api<NotificationPage>(`/me/notifications?cursor=${next}`)
      if (g !== gen.current) return // the list was reloaded meanwhile; this page belongs to the old one
      setItems((l) => {
        const have = new Set((l ?? []).map((x) => x.id))
        return [...(l ?? []), ...p.items.filter((x) => !have.has(x.id))]
      })
      setNext(p.nextCursor)
    } catch (e) {
      setActionError((e as Error).message)
    } finally {
      setBusy(false)
    }
  }

  async function act(fn: () => Promise<unknown>) {
    setActionError('')
    try {
      await fn()
      return true
    } catch (e) {
      setActionError((e as Error).message)
      loadFirst(true) // it may have been answered elsewhere: show what is really there
      return false
    }
  }

  async function openNotification(n: Notification) {
    if (!n.read) {
      setItems((l) => l && l.map((x) => (x.id === n.id ? { ...x, read: true } : x)))
      setUnread((u) => Math.max(0, u - 1))
      api(`/me/notifications/${n.id}/read`, { method: 'POST' }).catch(loadCount) // failed: take the real count back
    }
    setOpen(false)
    if (n.type === 'ReportOutcome') return
    // replies and reactions lead to the review itself; friend notifications to the other person
    navigate(n.reviewId != null ? `/reviews/${n.reviewId}` : `/u/${encodeURIComponent(n.actor)}`)
  }

  async function answer(n: Notification, accept: boolean) {
    if (inFlight.current.has(n.id)) return // a double click must not send the second answer
    inFlight.current.add(n.id)
    setAnswering([...inFlight.current])
    const ok = await act(() => api(`/friends/requests/${n.requestId}/${accept ? 'accept' : 'decline'}`, { method: 'POST' }))
    inFlight.current.delete(n.id)
    setAnswering([...inFlight.current])
    if (!ok) return
    // accepted: the row stays, read and no longer actionable; declined: the server deleted it
    setItems((l) => l && (accept ? l.map((x) => (x.id === n.id ? { ...x, read: true, actionable: false } : x)) : l.filter((x) => x.id !== n.id)))
    loadCount()
    refreshPending()
  }

  async function markAll() {
    if (!(await act(() => api('/me/notifications/read-all', { method: 'POST' })))) return
    setItems((l) => l && l.map((x) => ({ ...x, read: true })))
    setUnread(0)
  }

  function onKeyDown(e: KeyboardEvent) {
    if (e.key === 'Escape' && open) {
      setOpen(false)
      button.current?.focus()
    }
  }

  return (
    <div className="bell" ref={box} onKeyDown={onKeyDown}>
      <button
        ref={button}
        className="bell-button"
        aria-haspopup="true"
        aria-expanded={open}
        aria-label={unread > 0 ? `Notifications, ${unread} unread` : 'Notifications'}
        onClick={() => { if (!open) loadFirst(); setOpen(!open) }}
      >
        <BellIcon />
        {unread > 0 && <span className="bell-badge" aria-hidden="true">{unread > 9 ? '9+' : unread}</span>}
      </button>
      {open && (
        <div className="bell-panel" role="region" aria-label="Notifications">
          <div className="bell-head">
            <strong>Notifications</strong>
            <button className="link-button" onClick={markAll} disabled={!items?.some((x) => !x.read)}>Mark all read</button>
          </div>
          {actionError && <p className="error bell-msg" role="alert">{actionError}</p>}
          {error && (
            <p className="error bell-msg" role="alert">{error} <button onClick={() => loadFirst()}>Retry</button></p>
          )}
          {!items && !error && <p className="muted bell-msg">Loading</p>}
          {items && items.length === 0 && <p className="muted bell-msg">No notifications yet.</p>}
          {items && items.length > 0 && (
            <ul className="bell-list">
              {items.map((n) => (
                <li key={n.id} className={n.read ? 'bell-item' : 'bell-item unread'}>
                  <button className="bell-main" onClick={() => openNotification(n)}>
                    <span className="who">{n.type !== 'ReportOutcome' && <Avatar userName={n.actor} small />}<span>{line(n)}</span></span>
                    <span className="muted bell-time">{timeAgo(n.createdAt)}</span>
                  </button>
                  {n.actionable && (
                    <span className="bell-actions">
                      <button className="icon-button" aria-label={`Accept friend request from ${n.actor}`} disabled={answering.includes(n.id)} onClick={() => answer(n, true)}><CheckIcon /></button>
                      <button className="icon-button" aria-label={`Decline friend request from ${n.actor}`} disabled={answering.includes(n.id)} onClick={() => answer(n, false)}><CrossIcon /></button>
                    </span>
                  )}
                </li>
              ))}
            </ul>
          )}
          {next != null && <button className="bell-more" onClick={loadMore} disabled={busy}>{busy ? 'Loading' : 'Load older'}</button>}
        </div>
      )}
    </div>
  )
}

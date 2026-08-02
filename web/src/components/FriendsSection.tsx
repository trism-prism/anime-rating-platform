import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { Link } from 'react-router-dom'
import { api } from '../api'
import type { FriendsData } from '../types'
import Avatar from './Avatar'
import ConfirmDialog from './ConfirmDialog'

const profile = (name: string) => `/u/${encodeURIComponent(name)}`
const Who = ({ name }: { name: string }) => <span className="who"><Avatar userName={name} small /><Link to={profile(name)} className="link">{name}</Link></span>

// shown on your own profile: friends, pending requests both ways, and add-by-username
export default function FriendsSection({ onChange }: { onChange: () => void }) {
  const [data, setData] = useState<FriendsData | null>(null)
  const [error, setError] = useState('')
  const [actionError, setActionError] = useState('')
  const [name, setName] = useState('')
  const [msg, setMsg] = useState('')
  const [busy, setBusy] = useState(false)
  const [confirming, setConfirming] = useState(false) // the "decline all" dialog

  const load = useCallback(() => {
    api<FriendsData>('/me/friends').then((d) => { setData(d); setError('') }).catch((e: Error) => setError(e.message))
  }, [])

  useEffect(load, [load])

  async function run(fn: () => Promise<unknown>) {
    setActionError('')
    setMsg('')
    setBusy(true)
    try {
      await fn()
      load()
      onChange()
      return true
    } catch (e) {
      setActionError((e as Error).message)
      load() // resync, so buttons for requests that are already gone disappear
      onChange()
      return false
    } finally {
      setBusy(false)
    }
  }

  async function add(e: FormEvent) {
    e.preventDefault()
    const target = name.trim()
    if (!target) return
    const r = await run(async () => {
      const res = await api<{ state: string }>('/friends/requests', { method: 'POST', body: JSON.stringify({ username: target }) })
      setMsg(res.state === 'Friends' ? `You and ${target} are now friends.` : `Request sent to ${target}.`)
    })
    if (r) setName('')
  }

  const post = (path: string) => run(() => api(path, { method: 'POST' }))

  return (
    <section aria-labelledby="friends-title">
      <h2 id="friends-title">Friends</h2>
      <form className="row" onSubmit={add}>
        <label className="sr-only" htmlFor="friend-name">Add friend by username</label>
        <input id="friend-name" placeholder="Add friend by username" autoComplete="off" maxLength={256} value={name} onChange={(e) => setName(e.target.value)} />
        <button type="submit" disabled={busy || !name.trim()}>Send request</button>
      </form>
      {actionError && <p className="error" role="alert">{actionError}</p>}
      {msg && <p role="status">{msg}</p>}

      {error && <p className="error" role="alert">{error} <button onClick={() => { setError(''); load() }}>Retry</button></p>}
      {!data && !error && <p className="muted">Loading</p>}
      {data && (
        <>
          {data.incoming.length > 0 && (
            <>
              <div className="row subhead-row">
                <h3 className="subhead">Requests to you</h3>
                <button disabled={busy} onClick={() => setConfirming(true)}>Decline all</button>
              </div>
              <ul className="people">
                {data.incoming.map((r) => (
                  <li key={r.requestId}>
                    <Who name={r.userName} />
                    <span className="row">
                      <button disabled={busy} onClick={() => post(`/friends/requests/${r.requestId}/accept`)}>Accept</button>
                      <button disabled={busy} onClick={() => post(`/friends/requests/${r.requestId}/decline`)}>Decline</button>
                    </span>
                  </li>
                ))}
              </ul>
            </>
          )}
          {data.outgoing.length > 0 && (
            <>
              <h3 className="subhead">Requests you sent</h3>
              <ul className="people">
                {data.outgoing.map((r) => (
                  <li key={r.requestId}>
                    <Who name={r.userName} />
                    <button disabled={busy} onClick={() => run(() => api(`/friends/requests/${r.requestId}`, { method: 'DELETE' }))}>Cancel</button>
                  </li>
                ))}
              </ul>
            </>
          )}
          <h3 className="subhead">Your friends</h3>
          {data.friends.length === 0 ? (
            <p className="muted">No friends yet. Add someone by their username above.</p>
          ) : (
            <ul className="people">
              {data.friends.map((f) => (
                <li key={f.userName}><Who name={f.userName} /></li>
              ))}
            </ul>
          )}
        </>
      )}
      <ConfirmDialog open={confirming} title="Decline all requests?" confirmLabel="Decline all" busy={busy}
        onConfirm={async () => { await run(() => api('/friends/requests/decline-all', { method: 'POST' })); setConfirming(false) }}
        onCancel={() => setConfirming(false)}>
        <p>Every pending request sent to you is removed. The senders aren't told, and anyone can ask again later.</p>
      </ConfirmDialog>
    </section>
  )
}

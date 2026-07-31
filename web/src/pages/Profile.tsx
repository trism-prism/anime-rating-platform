import { useCallback, useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { api, ApiError } from '../api'
import { useAuth } from '../auth'
import Avatar from '../components/Avatar'
import ConfirmDialog from '../components/ConfirmDialog'
import FriendsSection from '../components/FriendsSection'
import ProfileLists from '../components/ProfileLists'
import ProfileRatings from '../components/ProfileRatings'
import ReportButton from '../components/ReportButton'
import type { Profile as ProfileData } from '../types'

// keyed so another profile or another account starts from the loading state
export default function ProfilePage() {
  const { username = '' } = useParams()
  const { user } = useAuth()
  return <Profile key={username + (user?.userId ?? '')} username={username} />
}

function Profile({ username }: { username: string }) {
  const [p, setP] = useState<ProfileData | null>(null)
  const [missing, setMissing] = useState(false)
  const [error, setError] = useState('')
  const [actionError, setActionError] = useState('')
  const [busy, setBusy] = useState(false)
  const [confirmBlock, setConfirmBlock] = useState(false)
  const [tick, setTick] = useState(0)

  const load = useCallback(() => {
    return api<ProfileData>(`/users/${encodeURIComponent(username)}`)
      .then((r) => { setP(r); setMissing(false); setError('') })
      .catch((e: Error) => {
        if (e instanceof ApiError && e.status === 404) { setP(null); setMissing(true) } else setError(e.message)
      })
  }, [username])

  useEffect(() => { load() }, [load, tick])

  async function run(fn: () => Promise<unknown>) {
    setActionError('')
    setBusy(true)
    try {
      await fn()
      await load()
    } catch (e) {
      setActionError((e as Error).message)
      await load() // it may have changed on the other side (cancelled, accepted): show what is really there
    } finally {
      setBusy(false)
      setConfirmBlock(false)
    }
  }

  const call = (path: string, method: string, body?: unknown) => () =>
    api(path, { method, body: body === undefined ? undefined : JSON.stringify(body) })

  if (missing) {
    return (
      <div>
        <h1>User not found</h1>
        <p className="muted">There is no user called "{username}", or you can't see this profile.</p>
        <Link to="/" className="link">Back to browse</Link>
      </div>
    )
  }
  if (error) return <p className="error" role="alert">{error} <button onClick={() => { setError(''); setTick((t) => t + 1) }}>Retry</button></p>
  if (!p) return <p className="muted">Loading...</p>

  const state = p.viewerState
  const name = encodeURIComponent(p.userName)
  const reqPath = `/friends/requests/${p.requestId}`

  return (
    <div className="profile">
      <div className="profile-head">
        <span className="profile-avatar"><Avatar userName={p.userName} /></span>
        <div>
          <h1>{p.userName}</h1>
          <p className="muted">
            Joined {new Date(p.joinedAt).toLocaleDateString(undefined, { year: 'numeric', month: 'long' })} · {p.friendCount} {p.friendCount === 1 ? 'friend' : 'friends'}
          </p>
        </div>
      </div>

      {state === null && <p><Link to="/login" className="link">Log in</Link> to add friends.</p>}
      {state && state !== 'Self' && (
        <div className="row profile-actions">
          {state === 'None' && <button disabled={busy} onClick={() => run(call('/friends/requests', 'POST', { username: p.userName }))}>Add friend</button>}
          {state === 'OutgoingPending' && (
            <>
              <span className="muted">Request sent</span>
              <button disabled={busy} onClick={() => run(call(reqPath, 'DELETE'))}>Cancel</button>
            </>
          )}
          {state === 'IncomingPending' && (
            <>
              <span className="muted">Wants to be friends</span>
              <button disabled={busy} onClick={() => run(call(`${reqPath}/accept`, 'POST'))}>Accept</button>
              <button disabled={busy} onClick={() => run(call(`${reqPath}/decline`, 'POST'))}>Decline</button>
            </>
          )}
          {state === 'Friends' && <button disabled={busy} onClick={() => run(call(`/friends/${name}`, 'DELETE'))}>Unfriend</button>}
          {state === 'BlockedByViewer' && <span className="muted">You blocked this user</span>}
          <details className="more" key={state}>
            <summary>More</summary>
            <div className="more-menu">
              {state === 'BlockedByViewer'
                ? <button disabled={busy} onClick={() => run(call(`/blocks/${name}`, 'DELETE'))}>Unblock</button>
                : <button disabled={busy} onClick={() => setConfirmBlock(true)}>Block</button>}
              <ReportButton path={`/users/${name}/report`} what="profile" />
            </div>
          </details>
        </div>
      )}
      {actionError && <p className="error" role="alert">{actionError}</p>}

      {state === 'Self' && <FriendsSection onChange={load} />}

      <ProfileLists key={`l:${p.userName}:${p.viewerState}`} username={p.userName} />

      <ProfileRatings key={`${p.userName}:${p.viewerState}`}username={p.userName} />

      <ConfirmDialog open={confirmBlock} title={`Block ${p.userName}?`} confirmLabel="Block" busy={busy}
        onConfirm={() => run(call(`/blocks/${name}`, 'PUT'))} onCancel={() => setConfirmBlock(false)}>
        <p>They won't be told. Any friendship or request between you ends, and they can no longer find your profile or send requests.</p>
      </ConfirmDialog>
    </div>
  )
}

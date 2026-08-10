import { useCallback, useEffect, useRef, useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { api, ApiError } from '../api'
import { useAuth } from '../auth'
import Avatar from '../components/Avatar'
import ConfirmDialog from '../components/ConfirmDialog'
import AvatarEditor from '../components/AvatarEditor'
import InlineEdit from '../components/InlineEdit'
import ProfileOverview from '../components/ProfileOverview'
import ProfileLists from '../components/ProfileLists'
import MoreMenu from '../components/MoreMenu'
import { ReportDialog } from '../components/ReportButton'
import { plural } from '../format'
import { refreshPending } from '../pendingRequests'
import { useTitle } from '../useTitle'
import type { Profile as ProfileData } from '../types'

// keyed so another profile or another account starts from the loading state
export default function ProfilePage() {
  const { username = '' } = useParams()
  const { user } = useAuth()
  return <Profile key={username + (user?.userId ?? '')} username={username} />
}

function Profile({ username }: { username: string }) {
  const { rename } = useAuth()
  const navigate = useNavigate()
  const [p, setP] = useState<ProfileData | null>(null)
  const [missing, setMissing] = useState(false)
  const [error, setError] = useState('')
  const [actionError, setActionError] = useState('')
  const [busy, setBusy] = useState(false)
  const [confirm, setConfirm] = useState<'block' | 'unfriend' | null>(null)
  const [reporting, setReporting] = useState(false)
  const [reported, setReported] = useState(false)
  const moreButton = useRef<HTMLButtonElement>(null)
  const [tick, setTick] = useState(0)
  useTitle(p?.userName ?? username)
  useEffect(() => { if (reported) moreButton.current?.focus() }, [reported]) // the dialog is gone: keep focus on More

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
      refreshPending() // an accepted or declined request changes the nav badge
    } catch (e) {
      setActionError((e as Error).message)
      await load() // it may have changed on the other side (cancelled, accepted): show what is really there
    } finally {
      setBusy(false)
      setConfirm(null)
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
  if (!p) return <p className="muted">Loading</p>

  const state = p.viewerState
  const name = encodeURIComponent(p.userName)
  const reqPath = `/friends/requests/${p.requestId}`

  return (
    <div className="profile">
      <div className="profile-head">
        {state === 'Self'
          ? <AvatarEditor userName={p.userName} hasAvatar={p.hasAvatar} onChange={load} />
          : <span className="profile-avatar"><Avatar userName={p.userName} /></span>}
        <div>
          {state === 'Self' ? (
            <InlineEdit label="Display name" value={p.userName} display={<h1>{p.userName}</h1>}
              hint={() => '3 to 30 letters, digits, . _ -'}
              field={(v, set) => <input required minLength={3} maxLength={30} pattern="[A-Za-z0-9._\-]+" autoComplete="off" value={v} onChange={(e) => set(e.target.value)} />}
              onSave={async (v) => {
                const r = await api<{ userName: string }>('/me/username', { method: 'PUT', body: JSON.stringify({ userName: v }) })
                rename(r.userName)
                navigate(`/u/${encodeURIComponent(r.userName)}`, { replace: true })
              }} />
          ) : <h1>{p.userName}</h1>}
          <p className="muted">
            Joined {new Date(p.joinedAt).toLocaleDateString('en-GB', { year: 'numeric', month: 'long' })}{p.friendCount > 0 && <> · {plural(p.friendCount, 'friend')}</>}
          </p>
        </div>
      </div>

      {state === 'Self' ? (
        <InlineEdit label="Bio" value={p.bio ?? ''} empty="Add a bio" display={<p className="bio">{p.bio}</p>}
          hint={(v) => `${v.length}/300`}
          field={(v, set) => <textarea rows={4} maxLength={300} value={v} onChange={(e) => set(e.target.value)} />}
          onSave={async (v) => {
            const r = await api<{ bio: string | null }>('/me/bio', { method: 'PUT', body: JSON.stringify({ bio: v }) })
            setP((x) => x && { ...x, bio: r.bio }) // the server's trimmed and censored text
          }} />
      ) : p.bio && <p className="bio">{p.bio}</p>}

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
          {state === 'Friends' && <span className="muted">Friends</span>}
          {state === 'BlockedByViewer' && <span className="muted">You blocked this user</span>}
          <MoreMenu buttonRef={moreButton} disabled={busy} items={[
            ...(state === 'Friends' ? [{ label: 'Unfriend', onSelect: () => setConfirm('unfriend') }] : []),
            state === 'BlockedByViewer'
              ? { label: 'Unblock', onSelect: () => run(call(`/blocks/${name}`, 'DELETE')) }
              : { label: 'Block', onSelect: () => setConfirm('block') },
            { label: reported ? 'Reported' : 'Report', disabled: reported, onSelect: () => setReporting(true) },
          ]} />
        </div>
      )}
      {actionError && <p className="error" role="alert">{actionError}</p>}

      <ProfileOverview key={`o:${p.userName}:${p.viewerState}`} username={p.userName} />
      <ProfileLists key={`l:${p.userName}:${p.viewerState}`} username={p.userName} />

      <ConfirmDialog open={confirm === 'block'} title={`Block ${p.userName}?`} confirmLabel="Block" busy={busy}
        onConfirm={() => run(call(`/blocks/${name}`, 'PUT'))} onCancel={() => setConfirm(null)}>
        <p>They won't be told. Any friendship or request between you ends, and they can no longer find your profile or send requests.</p>
      </ConfirmDialog>
      <ConfirmDialog open={confirm === 'unfriend'} title={`Unfriend ${p.userName}?`} confirmLabel="Unfriend" busy={busy}
        onConfirm={() => run(call(`/friends/${name}`, 'DELETE'))} onCancel={() => setConfirm(null)}>
        <p>You can send a new request later.</p>
      </ConfirmDialog>
      <ReportDialog path={`/users/${name}/report`} what="profile" open={reporting} onClose={() => setReporting(false)}
        onSent={() => { setReporting(false); setReported(true) }} />
    </div>
  )
}

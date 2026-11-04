import { useCallback, useEffect, useState } from 'react'
import { Navigate } from 'react-router-dom'
import { api, ApiError } from '../api'
import { useAuth } from '../auth'
import { device } from '../device'
import Activity from '../components/Activity'
import EmailForm from '../components/EmailForm'
import PasswordForm from '../components/PasswordForm'
import TwoFactor from '../components/TwoFactor'

type SessionRow = { id: string; createdAt: string; lastUsedAt: string; userAgent: string; ip: string; current: boolean }

export default function Settings() {
  const { user, emailVerified, loading, refresh, logout } = useAuth()
  const [msg, setMsg] = useState('')
  const [error, setError] = useState('')
  const [cooldown, setCooldown] = useState(false)
  const [sessions, setSessions] = useState<SessionRow[] | null>(null)
  const [devicesError, setDevicesError] = useState('')

  // pick up a verification done in another tab
  useEffect(() => { refresh() }, [refresh])

  const loadSessions = useCallback(() => {
    api<SessionRow[]>('/me/sessions').then(setSessions).catch((e: Error) => setDevicesError(e.message))
  }, [])
  useEffect(() => { if (user) loadSessions() }, [user, loadSessions])

  // after something that signs devices out or writes an event: reload both lists (a new key remounts Activity)
  const [changes, setChanges] = useState(0)
  function changed() {
    loadSessions()
    setChanges((n) => n + 1)
  }

  if (!user) return <Navigate to="/login" replace />

  async function send() {
    setMsg('')
    setError('')
    setCooldown(true) // locked while the request runs, then for 60 s on success/429
    let keepLocked = true
    try {
      const r = await api<{ message: string }>('/me/verify-email', { method: 'POST' })
      setMsg(r.message)
    } catch (err) {
      setError((err as Error).message)
      keepLocked = err instanceof ApiError && err.status === 429
    }
    if (keepLocked) setTimeout(() => setCooldown(false), 60_000)
    else setCooldown(false)
  }

  async function signOut(s: SessionRow) {
    setDevicesError('')
    if (s.current) return logout() // this device: also drops the cookie and the local account
    try {
      await api(`/me/sessions/${s.id}/revoke`, { method: 'POST' })
    } catch (err) {
      setDevicesError((err as Error).message)
    }
    changed()
  }

  async function signOutOthers() {
    setDevicesError('')
    try {
      await api('/me/sessions/revoke-others', { method: 'POST' })
    } catch (err) {
      setDevicesError((err as Error).message)
    }
    changed()
  }

  return (
    <div className="settings">
      <h1>Settings</h1>
      <p><span className="muted">Email: </span>{user.email}</p>
      <p><span className="muted">Username: </span>{user.userName}</p>
      {!loading && (
        <p>
          <span className={emailVerified ? 'badge ok' : 'badge'}>{emailVerified ? 'Verified' : 'Not verified'}</span>
        </p>
      )}
      {!loading && !emailVerified && (
        <>
          <p className="muted hint">Verified accounts can write review text.</p>
          <button onClick={send} disabled={cooldown}>Send verification email</button>
        </>
      )}
      {msg && <p className="muted">{msg}</p>}
      {error && <p className="error">{error}</p>}

      <h2>Password</h2>
      <PasswordForm onChanged={changed} />

      <h2>Email</h2>
      <EmailForm onChanged={changed} />

      <h2>Two-factor authentication</h2>
      <TwoFactor onChanged={changed} />

      <h2>Signed-in devices</h2>
      {devicesError && <p className="error">{devicesError}</p>}
      {sessions === null && !devicesError && <p className="muted">Loading...</p>}
      {sessions?.map((s) => (
        <div key={s.id} className="list-row">
          <span className="list-name">
            {device(s.userAgent)} {s.current && <span className="badge ok">This device</span>}
            <br />
            <span className="muted hint">{s.ip} · last used {new Date(s.lastUsedAt).toLocaleString()}</span>
          </span>
          <button onClick={() => signOut(s)}>Sign out</button>
        </div>
      ))}
      {sessions && sessions.length > 1 && <button onClick={signOutOthers}>Sign out all other devices</button>}

      <h2>Recent activity</h2>
      <Activity key={changes} />
    </div>
  )
}

import { useEffect, useState } from 'react'
import { Navigate } from 'react-router-dom'
import { api, ApiError } from '../api'
import { useAuth } from '../auth'

export default function Account() {
  const { user, emailVerified, loading, refresh } = useAuth()
  const [msg, setMsg] = useState('')
  const [error, setError] = useState('')
  const [cooldown, setCooldown] = useState(false)

  // pick up a verification done in another tab
  useEffect(() => { refresh() }, [refresh])

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

  return (
    <div className="auth-form">
      <h1>Account</h1>
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
    </div>
  )
}

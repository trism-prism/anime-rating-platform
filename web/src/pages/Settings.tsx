import { useEffect, useState } from 'react'
import { Navigate } from 'react-router-dom'
import { api, ApiError } from '../api'
import { useAuth } from '../auth'
import EmailForm from '../components/EmailForm'
import PasswordForm from '../components/PasswordForm'
import TwoFactor from '../components/TwoFactor'
import UserNameForm from '../components/UserNameForm'

export default function Settings() {
  const { user, emailVerified, hasPassword, loading, refresh } = useAuth()
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
    <div className="settings">
      <h1>Settings</h1>

      <h2>Display name</h2>
      <UserNameForm />

      {loading ? null : !hasPassword ? (
        <>
          <h2>Sign-in</h2>
          <p className="muted">This account signs in with Google. To add a password, use Forgot password on the login page.</p>
        </>
      ) : (
        <>
          <h2>Password</h2>
          <PasswordForm onChanged={refresh} />

          <h2>Email</h2>
          <p>
            <span className="muted">Current: </span>{user.email}{' '}
            <span className={emailVerified ? 'badge ok' : 'badge'}>{emailVerified ? 'Verified' : 'Not verified'}</span>
          </p>
          {!emailVerified && (
            <>
              <p className="muted hint">Verified accounts can write review text.</p>
              <button onClick={send} disabled={cooldown}>Send verification email</button>
              {msg && <p className="muted">{msg}</p>}
              {error && <p className="error">{error}</p>}
            </>
          )}
          <EmailForm onChanged={refresh} />

          <h2>Two-step verification</h2>
          <TwoFactor onChanged={refresh} />
        </>
      )}
    </div>
  )
}

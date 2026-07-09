import { useEffect, useState, type FormEvent } from 'react'
import { Link, Navigate, useLocation, useNavigate } from 'react-router-dom'
import { api, ApiError } from '../api'
import { useAuth } from '../auth'
import { useCountdown } from '../useCountdown'

const RESEND_SECONDS = 30 // the server sends one code per 30 s

// verify the account email with a 6-digit code; links here pass the page to return to as state.from
export default function Verify() {
  const { user, emailVerified, loading, refresh } = useAuth()
  const navigate = useNavigate()
  const from: string = useLocation().state?.from ?? '/'
  const [code, setCode] = useState('')
  const [sent, setSent] = useState(false)
  const [info, setInfo] = useState('')
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)
  const [done, setDone] = useState(false)
  const [wait, setWait] = useCountdown()

  // pick up a verification done in another tab
  useEffect(() => { refresh() }, [refresh])

  // after success: show the message for a moment, then go back
  useEffect(() => {
    if (!done) return
    const t = setTimeout(() => navigate(from, { replace: true }), 1500)
    return () => clearTimeout(t)
  }, [done, from, navigate])

  if (!user) return <Navigate to="/login" replace />
  if (loading) return null

  async function send() {
    setError('')
    setInfo('')
    setBusy(true)
    try {
      const r = await api<{ emailVerified: boolean; message: string }>('/me/verify/send', { method: 'POST' })
      setInfo(r.message)
      setSent(true)
      setWait(RESEND_SECONDS)
      if (r.emailVerified) refresh()
    } catch (err) {
      setError((err as Error).message)
      if (err instanceof ApiError && err.status === 429) setWait(RESEND_SECONDS)
    } finally {
      setBusy(false)
    }
  }

  async function submit(e: FormEvent) {
    e.preventDefault()
    setError('')
    setInfo('')
    setBusy(true)
    try {
      await api('/me/verify', { method: 'POST', body: JSON.stringify({ code }) })
      await refresh()
      setDone(true)
    } catch (err) {
      setError((err as Error).message)
    } finally {
      setBusy(false)
    }
  }

  if (done)
    return (
      <div className="auth-form">
        <h1>Email verified</h1>
        <p role="status">Thanks, you can write reviews now. Taking you back...</p>
      </div>
    )

  if (emailVerified)
    return (
      <div className="auth-form">
        <h1>Verify your email</h1>
        <p>Your email is already verified.</p>
        <Link to={from} className="link">Back</Link>
      </div>
    )

  return (
    <form className="auth-form" onSubmit={submit}>
      <h1>Verify your email</h1>
      <p className="muted">We will email a 6-digit code to {user.email}.</p>
      <button type="button" onClick={send} disabled={busy || wait > 0}>
        {wait > 0 ? `${sent ? 'Send the code again' : 'Send code'} (${wait}s)` : sent ? 'Send the code again' : 'Send code'}
      </button>
      <label>Code
        <input required inputMode="numeric" autoComplete="one-time-code" pattern="[0-9 ]{6,7}"
          title="6 digits" value={code} onChange={(e) => setCode(e.target.value)} />
      </label>
      {info && <p className="muted" role="status">{info}</p>}
      {error && <p className="error" role="alert">{error}</p>}
      <button type="submit" disabled={busy}>Verify</button>
    </form>
  )
}

import { useEffect, useState, type FormEvent } from 'react'
import { Link, useLocation, useNavigate } from 'react-router-dom'
import { api, ApiError, type SignedIn, type TwoFactorStep } from '../api'
import { useAuth } from '../auth'
import GoogleLink from '../components/GoogleLink'
import PasswordInput from '../components/PasswordInput'

const RESEND_SECONDS = 30 // the server allows one code per 30 s

// kai@gmail.com -> k***@gmail.com
const mask = (email: string) => email.charAt(0) + '***' + email.slice(email.indexOf('@'))

// ?error=... as set by the Google callback
const GOOGLE_ERRORS: Record<string, string> = {
  google: 'Google sign-in failed. Try again or use your password.',
}

export default function Login() {
  const { login } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()
  const note: string | undefined = location.state?.note // set by Register
  const googleError = GOOGLE_ERRORS[new URLSearchParams(location.search).get('error') ?? ''] ?? ''
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [challenge, setChallenge] = useState<string | null>(null) // set once the password was right and a code was emailed
  const [code, setCode] = useState('')
  const [wait, setWait] = useState(0) // seconds until a new code can be asked for
  const [info, setInfo] = useState('')
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    if (wait <= 0) return
    const t = setTimeout(() => setWait(wait - 1), 1000)
    return () => clearTimeout(t)
  }, [wait])

  async function submit(e: FormEvent) {
    e.preventDefault()
    setError('')
    setInfo('')
    setBusy(true)
    try {
      if (challenge) {
        const s = await api<SignedIn>('/auth/2fa', {
          method: 'POST',
          body: JSON.stringify({ challengeToken: challenge, code }),
        })
        await login(s)
        navigate('/')
        return
      }
      const r = await api<SignedIn | TwoFactorStep>('/auth/login', {
        method: 'POST',
        body: JSON.stringify({ email, password }),
      })
      if ('twoFactorRequired' in r) {
        setChallenge(r.challengeToken)
        setWait(RESEND_SECONDS)
        return
      }
      await login(r)
      navigate('/')
    } catch (err) {
      setError((err as Error).message)
    } finally {
      setBusy(false)
    }
  }

  async function resend() {
    setError('')
    setInfo('')
    setBusy(true)
    try {
      const r = await api<{ message: string }>('/auth/2fa/resend', { method: 'POST', body: JSON.stringify({ challengeToken: challenge }) })
      setInfo(r.message)
      setWait(RESEND_SECONDS)
    } catch (err) {
      // 401 here means the challenge ran out: back to the password step with the server's message
      if (err instanceof ApiError && err.status === 401) startOver()
      setError((err as Error).message)
    } finally {
      setBusy(false)
    }
  }

  function startOver() {
    setChallenge(null)
    setCode('')
    setPassword('')
    setInfo('')
    setError('')
  }

  if (challenge)
    return (
      <form className="auth-form" onSubmit={submit}>
        <h1>Check your email</h1>
        <p className="muted">We emailed a 6-digit code to {mask(email)}.</p>
        <label>Code
          <input required autoFocus inputMode="numeric" autoComplete="one-time-code" pattern="[0-9 ]{6,7}"
            title="6 digits" value={code} onChange={(e) => setCode(e.target.value)} />
        </label>
        {info && <p className="muted" role="status">{info}</p>}
        {error && <p className="error" role="alert">{error}</p>}
        <button type="submit" disabled={busy}>Continue</button>
        <button type="button" onClick={resend} disabled={busy || wait > 0}>
          {wait > 0 ? `Send a new code (${wait}s)` : 'Send a new code'}
        </button>
        <button type="button" onClick={startOver} disabled={busy}>Back to login</button>
      </form>
    )

  return (
    <form className="auth-form" onSubmit={submit}>
      <h1>Login</h1>
      {note && <p className="muted">{note}</p>}
      <label>Email
        <input type="email" required autoComplete="off" value={email} onChange={(e) => setEmail(e.target.value)} />
      </label>
      <PasswordInput label="Password" value={password} onChange={setPassword} autoComplete="current-password" />
      {(error || googleError) && <p className="error">{error || googleError}</p>}
      <button type="submit" disabled={busy}>Login</button>
      <GoogleLink />
      <p className="muted"><Link to="/forgot-password" className="link">Forgot password?</Link></p>
      <p className="muted">No account? <Link to="/register" className="link">Register</Link></p>
    </form>
  )
}

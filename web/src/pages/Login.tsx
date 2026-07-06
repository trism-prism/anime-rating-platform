import { useState, type FormEvent } from 'react'
import { Link, useLocation, useNavigate } from 'react-router-dom'
import { api, type SignedIn, type TwoFactorStep } from '../api'
import { useAuth } from '../auth'
import GoogleLink from '../components/GoogleLink'
import PasswordInput from '../components/PasswordInput'

export default function Login() {
  const { login } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()
  const note: string | undefined = location.state?.note // set by Register
  const googleError = new URLSearchParams(location.search).get('error') === 'google'
    ? 'Google sign-in failed. Try again or use your password.' : ''
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [challenge, setChallenge] = useState<string | null>(null) // set once the password was right and a code is needed
  const [code, setCode] = useState('')
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)

  async function submit(e: FormEvent) {
    e.preventDefault()
    setError('')
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

  function startOver() {
    setChallenge(null)
    setCode('')
    setPassword('')
    setError('')
  }

  if (challenge)
    return (
      <form className="auth-form" onSubmit={submit}>
        <h1>Two-factor authentication</h1>
        <label>Code
          <input required autoFocus autoComplete="one-time-code" value={code} onChange={(e) => setCode(e.target.value)} />
        </label>
        <p className="muted hint">Enter the 6-digit code from your authenticator app. No phone? Use a recovery code instead.</p>
        {error && <p className="error" role="alert">{error}</p>}
        <button type="submit" disabled={busy}>Continue</button>
        <button type="button" onClick={startOver} disabled={busy}>Back to login</button>
      </form>
    )

  return (
    <form className="auth-form" onSubmit={submit}>
      <h1>Login</h1>
      {note && <p className="muted">{note}</p>}
      <label>Email
        <input type="email" required value={email} onChange={(e) => setEmail(e.target.value)} />
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

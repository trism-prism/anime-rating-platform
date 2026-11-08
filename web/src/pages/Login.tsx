import { useState, type FormEvent } from 'react'
import { Link, useLocation, useNavigate } from 'react-router-dom'
import { api, type SignedIn } from '../api'
import { useAuth } from '../auth'
import GoogleLink from '../components/GoogleLink'
import PasswordInput from '../components/PasswordInput'
import { useTitle } from '../useTitle'

// ?error=... as set by the Google callback
const GOOGLE_ERRORS: Record<string, string> = {
  google: 'Google sign-in failed. Try again or use your password.',
  terms: 'Google sign-in needs the Continue with Google button on this page or on Register. Try again from there.',
}

export default function Login() {
  useTitle('Login')
  const { login } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()
  const note: string | undefined = location.state?.note // set by Register and by the password reset
  const googleError = GOOGLE_ERRORS[new URLSearchParams(location.search).get('error') ?? ''] ?? ''
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)

  async function submit(e: FormEvent) {
    e.preventDefault()
    setError('')
    setBusy(true)
    try {
      const s = await api<SignedIn>('/auth/login', {
        method: 'POST',
        body: JSON.stringify({ email, password }),
      })
      await login(s)
      navigate('/')
    } catch (err) {
      setError((err as Error).message)
    } finally {
      setBusy(false)
    }
  }

  return (
    <form className="auth-form" onSubmit={submit}>
      <h1>Login</h1>
      {note && <p className="muted">{note}</p>}
      <label>Email
        <input type="email" required autoComplete="off" value={email} onChange={(e) => setEmail(e.target.value)} />
      </label>
      <PasswordInput label="Password" value={password} onChange={setPassword} autoComplete="current-password" />
      {(error || googleError) && <p className="error">{error || googleError}</p>}
      <button type="submit" className="btn-primary" disabled={busy}>Login</button>
      <GoogleLink notice />
      <p className="muted"><Link to="/forgot-password" className="link">Forgot password?</Link></p>
      <p className="muted">No account? <Link to="/register" className="link">Register</Link></p>
    </form>
  )
}

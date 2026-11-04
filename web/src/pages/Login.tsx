import { useState, type FormEvent } from 'react'
import { Link, useLocation, useNavigate } from 'react-router-dom'
import { api, type SignedIn } from '../api'
import { useAuth } from '../auth'
import PasswordInput from '../components/PasswordInput'

export default function Login() {
  const { login } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()
  const note: string | undefined = location.state?.note // set by Register
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
        <input type="email" required value={email} onChange={(e) => setEmail(e.target.value)} />
      </label>
      <PasswordInput label="Password" value={password} onChange={setPassword} autoComplete="current-password" />
      {error && <p className="error">{error}</p>}
      <button type="submit" disabled={busy}>Login</button>
      <p className="muted"><Link to="/forgot-password" className="link">Forgot password?</Link></p>
      <p className="muted">No account? <Link to="/register" className="link">Register</Link></p>
    </form>
  )
}

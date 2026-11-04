import { useState, type FormEvent } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { api } from '../api'

export default function Register() {
  const navigate = useNavigate()
  const [email, setEmail] = useState('')
  const [userName, setUserName] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)

  async function submit(e: FormEvent) {
    e.preventDefault()
    setError('')
    setBusy(true)
    try {
      await api('/auth/register', {
        method: 'POST',
        body: JSON.stringify({ email, password, userName: userName || null }),
      })
      navigate('/check-email', { state: { email } })
    } catch (err) {
      setError((err as Error).message)
    } finally {
      setBusy(false)
    }
  }

  return (
    <form className="auth-form" onSubmit={submit}>
      <h1>Register</h1>
      <label>Email
        <input type="email" required value={email} onChange={(e) => setEmail(e.target.value)} />
      </label>
      <label>Username (optional)
        <input value={userName} onChange={(e) => setUserName(e.target.value)} />
      </label>
      <label>Password
        <input type="password" required minLength={8} value={password} onChange={(e) => setPassword(e.target.value)} />
      </label>
      <p className="muted hint">At least 8 characters, with a digit and a lowercase letter.</p>
      {error && <p className="error">{error}</p>}
      <button type="submit" disabled={busy}>Create account</button>
      <p className="muted">Have an account? <Link to="/login" className="link">Login</Link></p>
    </form>
  )
}

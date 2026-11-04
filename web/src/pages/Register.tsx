import { useState, type FormEvent } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { api } from '../api'
import PasswordInput from '../components/PasswordInput'

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
      navigate('/login', { state: { note: 'Account created. Verify your email in Settings to post reviews.' } })
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
      <PasswordInput label="Password" value={password} onChange={setPassword} minLength={12} autoComplete="new-password" />
      <p className="muted hint">At least 12 characters. A passphrase works well. Passwords found in data breaches are refused.</p>
      {error && <p className="error">{error}</p>}
      <button type="submit" disabled={busy}>Create account</button>
      <p className="muted">Have an account? <Link to="/login" className="link">Login</Link></p>
    </form>
  )
}

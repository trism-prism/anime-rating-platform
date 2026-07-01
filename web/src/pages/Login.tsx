import { useState, type FormEvent } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { api, ApiError } from '../api'
import { useAuth } from '../auth'

export default function Login() {
  const { login } = useAuth()
  const navigate = useNavigate()
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState('')
  const [unconfirmed, setUnconfirmed] = useState(false)
  const [info, setInfo] = useState('')
  const [busy, setBusy] = useState(false)

  async function submit(e: FormEvent) {
    e.preventDefault()
    setError('')
    setInfo('')
    setUnconfirmed(false)
    setBusy(true)
    try {
      const u = await api<{ token: string; email: string; userName: string }>('/auth/login', {
        method: 'POST',
        body: JSON.stringify({ email, password }),
      })
      login(u)
      navigate('/')
    } catch (err) {
      setError((err as Error).message)
      setUnconfirmed(err instanceof ApiError && err.status === 403)
    } finally {
      setBusy(false)
    }
  }

  async function resend() {
    try {
      const r = await api<{ message: string }>('/auth/resend-confirmation', {
        method: 'POST',
        body: JSON.stringify({ email }),
      })
      setInfo(r.message)
    } catch (err) {
      setError((err as Error).message)
    }
  }

  return (
    <form className="auth-form" onSubmit={submit}>
      <h1>Login</h1>
      <label>Email
        <input type="email" required value={email} onChange={(e) => setEmail(e.target.value)} />
      </label>
      <label>Password
        <input type="password" required value={password} onChange={(e) => setPassword(e.target.value)} />
      </label>
      {error && <p className="error">{error}</p>}
      {unconfirmed && <button type="button" onClick={resend}>Resend confirmation email</button>}
      {info && <p className="muted">{info}</p>}
      <button type="submit" disabled={busy}>Login</button>
      <p className="muted">No account? <Link to="/register" className="link">Register</Link></p>
    </form>
  )
}

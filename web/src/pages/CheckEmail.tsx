import { useState } from 'react'
import { useLocation } from 'react-router-dom'
import { api } from '../api'

export default function CheckEmail() {
  const location = useLocation()
  const [email, setEmail] = useState<string>(location.state?.email ?? '')
  const [msg, setMsg] = useState('')
  const [error, setError] = useState('')

  async function resend() {
    setMsg('')
    setError('')
    try {
      const r = await api<{ message: string }>('/auth/resend-confirmation', {
        method: 'POST',
        body: JSON.stringify({ email }),
      })
      setMsg(r.message)
    } catch (err) {
      setError((err as Error).message)
    }
  }

  return (
    <div className="auth-form">
      <h1>Check your email</h1>
      <p>We sent a confirmation link{email && <> to <strong>{email}</strong></>}. Click it to activate your account.</p>
      {!location.state?.email && (
        <input type="email" placeholder="Your email" value={email} onChange={(e) => setEmail(e.target.value)} />
      )}
      <button onClick={resend} disabled={!email}>Resend email</button>
      {msg && <p className="muted">{msg}</p>}
      {error && <p className="error">{error}</p>}
    </div>
  )
}

import { useState, type FormEvent } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { api } from '../api'
import PasswordInput from '../components/PasswordInput'

export default function ResetPassword() {
  const [params] = useSearchParams()
  const userId = params.get('userId')
  const token = params.get('token')
  const [password, setPassword] = useState('')
  const [message, setMessage] = useState('')
  const [error, setError] = useState(userId && token ? '' : 'Invalid reset link.')
  const [busy, setBusy] = useState(false)

  async function submit(e: FormEvent) {
    e.preventDefault()
    setError('')
    setBusy(true)
    try {
      const r = await api<{ message: string }>('/auth/reset-password', {
        method: 'POST',
        body: JSON.stringify({ userId, token, newPassword: password }),
      })
      setMessage(r.message)
    } catch (err) {
      setError((err as Error).message)
    } finally {
      setBusy(false)
    }
  }

  if (message) {
    return (
      <div className="auth-form">
        <h1>Reset password</h1>
        <p>{message}</p>
        <Link to="/login" className="link">Go to login</Link>
      </div>
    )
  }

  return (
    <form className="auth-form" onSubmit={submit}>
      <h1>Reset password</h1>
      <PasswordInput label="New password" value={password} onChange={setPassword} minLength={12} autoComplete="new-password" />
      <p className="muted hint">At least 12 characters. Passwords found in data breaches are refused.</p>
      {error && <p className="error">{error}</p>}
      <button type="submit" disabled={busy || !userId || !token}>Change password</button>
    </form>
  )
}

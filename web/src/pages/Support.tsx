import { useState, type FormEvent } from 'react'
import { Link } from 'react-router-dom'
import { api } from '../api'
import { useTitle } from '../useTitle'

// works signed out; `website` is a honeypot that real visitors never see
export default function Support() {
  useTitle('Support')
  const [name, setName] = useState('')
  const [email, setEmail] = useState('')
  const [message, setMessage] = useState('')
  const [website, setWebsite] = useState('')
  const [error, setError] = useState('')
  const [done, setDone] = useState('')
  const [busy, setBusy] = useState(false)

  async function submit(e: FormEvent) {
    e.preventDefault()
    setError('')
    setDone('')
    setBusy(true)
    try {
      const r = await api<{ message: string }>('/support', { method: 'POST', body: JSON.stringify({ name, email, message, website }) })
      setDone(r.message)
      setMessage('')
    } catch (err) {
      setError((err as Error).message)
    } finally {
      setBusy(false)
    }
  }

  return (
    <form className="auth-form" onSubmit={submit}>
      <h1>Support</h1>
      <p className="muted">
        Questions, bugs, a decision you disagree with, or a data request? Check the{' '}
        <Link to="/faq" className="link">FAQ</Link> first. You do not need an account. We use your
        email to reply to you. The message is emailed to the admins, as described in the{' '}
        <Link to="/privacy" className="link">privacy policy</Link>.
      </p>
      <label>Name (optional)
        <input maxLength={100} autoComplete="name" value={name} onChange={(e) => setName(e.target.value)} />
      </label>
      <label>Email
        <input type="email" required autoComplete="email" value={email} onChange={(e) => setEmail(e.target.value)} />
      </label>
      <label>Message
        <textarea required rows={6} maxLength={2000} value={message} onChange={(e) => setMessage(e.target.value)} />
      </label>
      <p className="muted hint">{message.length} / 2000</p>
      <label className="hp" aria-hidden="true">Website
        <input tabIndex={-1} autoComplete="off" value={website} onChange={(e) => setWebsite(e.target.value)} />
      </label>
      {error && <p className="error" role="alert">{error}</p>}
      {done && <p role="status">{done}</p>}
      <button type="submit" className="btn-primary" disabled={busy}>Send</button>
    </form>
  )
}

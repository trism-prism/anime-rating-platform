import { useState, type FormEvent } from 'react'
import { api } from '../api'
import PasswordInput from './PasswordInput'

// change the password; the server signs out every other device
export default function PasswordForm({ onChanged }: { onChanged: () => void }) {
  const [current, setCurrent] = useState('')
  const [next, setNext] = useState('')
  const [msg, setMsg] = useState('')
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)

  async function submit(e: FormEvent) {
    e.preventDefault()
    setMsg('')
    setError('')
    setBusy(true)
    try {
      const r = await api<{ message: string }>('/me/password', {
        method: 'POST',
        body: JSON.stringify({ currentPassword: current, newPassword: next }),
      })
      setMsg(r.message)
      setCurrent('')
      setNext('')
      onChanged()
    } catch (err) {
      setError((err as Error).message)
    } finally {
      setBusy(false)
    }
  }

  return (
    <form className="settings-form" onSubmit={submit}>
      <PasswordInput label="Current password" value={current} onChange={setCurrent} autoComplete="current-password" />
      <PasswordInput label="New password" value={next} onChange={setNext} minLength={12} autoComplete="new-password" />
      <p className="muted hint">At least 12 characters. Your other devices will be signed out.</p>
      {error && <p className="error" role="alert">{error}</p>}
      {msg && <p role="status">{msg}</p>}
      <button type="submit" className="btn-primary" disabled={busy}>{busy ? 'Saving' : 'Change password'}</button>
    </form>
  )
}

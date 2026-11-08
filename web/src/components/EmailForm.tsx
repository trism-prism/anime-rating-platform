import { useState, type FormEvent } from 'react'
import { api } from '../api'
import PasswordInput from './PasswordInput'

// ask for a new email; it only changes once the link sent to the new address is opened
export default function EmailForm({ onChanged }: { onChanged: () => void }) {
  const [newEmail, setNewEmail] = useState('')
  const [password, setPassword] = useState('')
  const [msg, setMsg] = useState('')
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)

  async function submit(e: FormEvent) {
    e.preventDefault()
    setMsg('')
    setError('')
    setBusy(true)
    try {
      const r = await api<{ message: string }>('/me/email', {
        method: 'POST',
        body: JSON.stringify({ newEmail, currentPassword: password }),
      })
      setMsg(r.message)
      setNewEmail('')
      setPassword('')
      onChanged()
    } catch (err) {
      setError((err as Error).message)
    } finally {
      setBusy(false)
    }
  }

  return (
    <form className="settings-form" onSubmit={submit}>
      <label>New email
        <input type="email" required autoComplete="email" value={newEmail} onChange={(e) => setNewEmail(e.target.value)} />
      </label>
      <PasswordInput label="Current password" value={password} onChange={setPassword} autoComplete="current-password" />
      {error && <p className="error" role="alert">{error}</p>}
      {msg && <p role="status">{msg}</p>}
      <button type="submit" className="btn-primary" disabled={busy}>{busy ? 'Sending' : 'Change email'}</button>
    </form>
  )
}

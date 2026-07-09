import { useState, type FormEvent } from 'react'
import { api } from '../api'
import { useAuth } from '../auth'

// change the display name shown on reviews; no password needed
export default function UserNameForm() {
  const { user, rename } = useAuth()
  const [name, setName] = useState(user?.userName ?? '')
  const [msg, setMsg] = useState('')
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)

  async function submit(e: FormEvent) {
    e.preventDefault()
    setMsg('')
    setError('')
    setBusy(true)
    try {
      const r = await api<{ userName: string }>('/me/username', { method: 'PUT', body: JSON.stringify({ userName: name }) })
      rename(r.userName)
      setName(r.userName)
      setMsg('Display name saved.')
    } catch (err) {
      setError((err as Error).message)
    } finally {
      setBusy(false)
    }
  }

  return (
    <form className="settings-form" onSubmit={submit}>
      <label>Display name
        <input required minLength={3} maxLength={30} pattern="[A-Za-z0-9._\-]+" autoComplete="off"
          title="3 to 30 letters, digits, dots, underscores or dashes" value={name} onChange={(e) => setName(e.target.value)} />
      </label>
      <p className="muted hint">Shown on your reviews. 3 to 30 letters, digits, dots, underscores or dashes.</p>
      {error && <p className="error" role="alert">{error}</p>}
      {msg && <p role="status">{msg}</p>}
      <button type="submit" disabled={busy}>Save display name</button>
    </form>
  )
}

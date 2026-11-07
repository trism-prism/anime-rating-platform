import { useState, type FormEvent } from 'react'
import { api } from '../api'
import { useAuth } from '../auth'
import ConfirmDialog from './ConfirmDialog'
import PasswordInput from './PasswordInput'

// delete the account (its ratings and reviews stay, anonymized); asks for the password, or for an emailed code when the account has no password
export default function DeleteAccountForm() {
  const { hasPassword, logout } = useAuth()
  const [password, setPassword] = useState('')
  const [code, setCode] = useState('')
  const [sent, setSent] = useState(false)
  const [asking, setAsking] = useState(false)
  const [error, setError] = useState('')
  const [info, setInfo] = useState('')
  const [busy, setBusy] = useState(false)

  async function sendCode() {
    setError('')
    setInfo('')
    setBusy(true)
    try {
      const r = await api<{ message: string }>('/me/delete/code', { method: 'POST' })
      setInfo(r.message)
      setSent(true)
    } catch (err) {
      setError((err as Error).message)
    } finally {
      setBusy(false)
    }
  }

  async function remove() {
    setError('')
    setBusy(true)
    try {
      await api('/me/delete', { method: 'POST', body: JSON.stringify({ currentPassword: password, code }) })
      await logout() // forgets the account in this browser
      window.location.assign('/')
    } catch (err) {
      setAsking(false)
      setError((err as Error).message)
      setBusy(false)
    }
  }

  function submit(e: FormEvent) {
    e.preventDefault()
    setAsking(true)
  }

  const ready = hasPassword ? !!password : !!code
  return (
    <form className="settings-form" onSubmit={submit}>
      <p className="muted hint">
        This cannot be undone. Download your data above first if you want a copy.
      </p>
      {hasPassword ? (
        <PasswordInput label="Current password" value={password} onChange={setPassword} autoComplete="current-password" />
      ) : (
        <>
          <p className="muted hint">This account has no password, so we email you a code to confirm.</p>
          <button type="button" onClick={sendCode} disabled={busy}>{sent ? 'Send a new code' : 'Email me a code'}</button>
          <label>Code
            <input inputMode="numeric" autoComplete="one-time-code" value={code} onChange={(e) => setCode(e.target.value)} />
          </label>
        </>
      )}
      {error && <p className="error" role="alert">{error}</p>}
      {info && <p role="status">{info}</p>}
      <button type="submit" disabled={busy || !ready}>Delete my account</button>
      <ConfirmDialog open={asking} title="Delete your account?" confirmLabel="Delete forever" busy={busy} onConfirm={remove} onCancel={() => setAsking(false)}>
        <p>This cannot be undone.</p>
      </ConfirmDialog>
    </form>
  )
}

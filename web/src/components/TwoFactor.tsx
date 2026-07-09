import { useEffect, useState, type FormEvent } from 'react'
import { api } from '../api'
import PasswordInput from './PasswordInput'

// two-step verification by emailed code: one button turns it on or off, after the password
export default function TwoFactor({ onChanged }: { onChanged: () => void }) {
  const [enabled, setEnabled] = useState<boolean | null>(null)
  const [asking, setAsking] = useState(false) // the password form is open
  const [password, setPassword] = useState('')
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    api<{ enabled: boolean }>('/me/2fa').then((r) => setEnabled(r.enabled)).catch((e: Error) => setError(e.message))
  }, [])

  function close() {
    setAsking(false)
    setPassword('')
    setError('')
  }

  async function submit(e: FormEvent) {
    e.preventDefault()
    setError('')
    setBusy(true)
    try {
      await api(enabled ? '/me/2fa/disable' : '/me/2fa/enable', { method: 'POST', body: JSON.stringify({ currentPassword: password }) })
      setEnabled(!enabled)
      close()
      onChanged() // other devices were signed out
    } catch (err) {
      setError((err as Error).message)
    } finally {
      setBusy(false)
    }
  }

  if (enabled === null) return error ? <p className="error" role="alert">{error}</p> : <p className="muted">Loading...</p>

  return (
    <div className="settings-form">
      <p>
        <span className={enabled ? 'badge ok' : 'badge'}>{enabled ? 'On' : 'Off'}</span>{' '}
        <span className="muted hint">
          {enabled ? 'A code is emailed to you each time you log in.' : 'Ask for a code sent to your email each time you log in.'}
        </span>
      </p>
      {asking ? (
        <form className="settings-form" onSubmit={submit}>
          <PasswordInput label="Current password" value={password} onChange={setPassword} autoComplete="current-password" />
          {error && <p className="error" role="alert">{error}</p>}
          <div className="row">
            <button type="submit" disabled={busy}>{enabled ? 'Turn off' : 'Turn on'}</button>
            <button type="button" onClick={close} disabled={busy}>Cancel</button>
          </div>
        </form>
      ) : (
        <div>
          <button type="button" onClick={() => setAsking(true)}>{enabled ? 'Turn off' : 'Turn on'}</button>
        </div>
      )}
    </div>
  )
}

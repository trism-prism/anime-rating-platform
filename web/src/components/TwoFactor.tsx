import { useEffect, useState, type FormEvent } from 'react'
import { QRCodeSVG } from 'qrcode.react'
import { api } from '../api'
import PasswordInput from './PasswordInput'

type Status = { enabled: boolean; recoveryCodesLeft: number }
type Setup = { key: string; uri: string }
type Form = 'none' | 'codes' | 'disable'

// authenticator-app two-factor: turn on, turn off, new recovery codes
export default function TwoFactor({ onChanged }: { onChanged: () => void }) {
  const [status, setStatus] = useState<Status | null>(null)
  const [setup, setSetup] = useState<Setup | null>(null) // set while the turn-on steps are open
  const [codes, setCodes] = useState<string[] | null>(null) // recovery codes, shown once
  const [form, setForm] = useState<Form>('none')
  const [code, setCode] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)
  const [copied, setCopied] = useState(false)

  useEffect(() => {
    api<Status>('/me/2fa').then(setStatus).catch((e: Error) => setError(e.message))
  }, [])

  // disables the buttons while a request runs and shows its error
  async function run(action: () => Promise<void>) {
    setError('')
    setBusy(true)
    try {
      await action()
    } catch (err) {
      setError((err as Error).message)
    } finally {
      setBusy(false)
    }
  }

  function close() {
    setSetup(null)
    setForm('none')
    setCode('')
    setPassword('')
    setError('')
  }

  const post = (path: string, body: object) => api<{ recoveryCodes: string[] }>(path, { method: 'POST', body: JSON.stringify(body) })

  const startSetup = () => run(async () => setSetup(await api<Setup>('/me/2fa/setup')))

  const enable = (e: FormEvent) => {
    e.preventDefault()
    return run(async () => {
      const r = await post('/me/2fa/enable', { code })
      setCodes(r.recoveryCodes)
      setStatus({ enabled: true, recoveryCodesLeft: r.recoveryCodes.length })
      close()
      onChanged() // other devices were signed out
    })
  }

  const disable = (e: FormEvent) => {
    e.preventDefault()
    return run(async () => {
      await api('/me/2fa/disable', { method: 'POST', body: JSON.stringify({ currentPassword: password, code }) })
      setStatus({ enabled: false, recoveryCodesLeft: 0 })
      close()
      onChanged()
    })
  }

  const newCodes = (e: FormEvent) => {
    e.preventDefault()
    return run(async () => {
      const r = await post('/me/2fa/recovery-codes', { currentPassword: password })
      setCodes(r.recoveryCodes)
      setStatus({ enabled: true, recoveryCodesLeft: r.recoveryCodes.length })
      close()
      onChanged()
    })
  }

  function copy() {
    navigator.clipboard.writeText(codes!.join('\n')).then(
      () => setCopied(true),
      () => setError('Could not copy. Select the codes and copy them by hand.'),
    )
  }

  if (!status) return error ? <p className="error" role="alert">{error}</p> : <p className="muted">Loading...</p>

  if (codes)
    return (
      <div className="settings-form">
        <p><strong>Save these recovery codes now.</strong> Each one works once if you lose your authenticator app. They will not be shown again.</p>
        <ul className="codes">{codes.map((c) => <li key={c}>{c}</li>)}</ul>
        {error && <p className="error" role="alert">{error}</p>}
        <div className="row">
          <button type="button" onClick={copy}>{copied ? 'Copied' : 'Copy codes'}</button>
          <button type="button" onClick={() => { setCodes(null); setCopied(false); setError('') }}>I have saved them</button>
        </div>
      </div>
    )

  if (setup)
    return (
      <form className="settings-form" onSubmit={enable}>
        <p>1. Scan this code with an authenticator app, or type the key into it.</p>
        <div className="qr"><QRCodeSVG value={setup.uri} size={168} marginSize={1} /></div>
        <p className="muted hint">Key: <code className="key">{setup.key.match(/.{1,4}/g)?.join(' ')}</code></p>
        <p className="muted hint"><a className="link" href={setup.uri}>Open in an app on this device</a></p>
        <label>2. Enter the 6-digit code the app shows
          <input required autoFocus inputMode="numeric" autoComplete="one-time-code" pattern="[0-9 ]{6,7}"
            title="6 digits" value={code} onChange={(e) => setCode(e.target.value)} />
        </label>
        {error && <p className="error" role="alert">{error}</p>}
        <div className="row">
          <button type="submit" disabled={busy}>Turn on</button>
          <button type="button" onClick={close} disabled={busy}>Cancel</button>
        </div>
      </form>
    )

  if (!status.enabled)
    return (
      <div className="settings-form">
        <p className="muted">Off. Ask for a code from an authenticator app each time you log in.</p>
        {error && <p className="error" role="alert">{error}</p>}
        <button type="button" onClick={startSetup} disabled={busy}>Set up authenticator app</button>
      </div>
    )

  return (
    <div className="settings-form">
      <p>
        <span className="badge ok">On</span>{' '}
        <span className="muted hint">{status.recoveryCodesLeft} recovery codes left</span>
      </p>
      {form === 'none' && (
        <div className="row">
          <button type="button" onClick={() => setForm('codes')}>New recovery codes</button>
          <button type="button" onClick={() => setForm('disable')}>Turn off</button>
        </div>
      )}
      {form === 'codes' && (
        <form className="settings-form" onSubmit={newCodes}>
          <p className="muted hint">The old codes stop working.</p>
          <PasswordInput label="Current password" value={password} onChange={setPassword} autoComplete="current-password" />
          {error && <p className="error" role="alert">{error}</p>}
          <div className="row">
            <button type="submit" disabled={busy}>Make new codes</button>
            <button type="button" onClick={close} disabled={busy}>Cancel</button>
          </div>
        </form>
      )}
      {form === 'disable' && (
        <form className="settings-form" onSubmit={disable}>
          <PasswordInput label="Current password" value={password} onChange={setPassword} autoComplete="current-password" />
          <label>Code from the app, or a recovery code
            <input required autoComplete="one-time-code" value={code} onChange={(e) => setCode(e.target.value)} />
          </label>
          {error && <p className="error" role="alert">{error}</p>}
          <div className="row">
            <button type="submit" disabled={busy}>Turn off</button>
            <button type="button" onClick={close} disabled={busy}>Cancel</button>
          </div>
        </form>
      )}
    </div>
  )
}

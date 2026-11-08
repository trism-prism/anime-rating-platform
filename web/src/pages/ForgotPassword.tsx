import { useState, type FormEvent } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { api } from '../api'
import PasswordInput from '../components/PasswordInput'
import { useCountdown } from '../useCountdown'
import { useTitle } from '../useTitle'

type Step = 'email' | 'code' | 'password'

const RESEND_SECONDS = 60 // the server sends one code per address per minute

// three steps on one page: the account email, the emailed code, the new password
export default function ForgotPassword() {
  useTitle('Forgot password')
  const navigate = useNavigate()
  const [step, setStep] = useState<Step>('email')
  const [email, setEmail] = useState('')
  const [code, setCode] = useState('')
  const [password, setPassword] = useState('')
  const [info, setInfo] = useState('')
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)
  const [wait, setWait] = useCountdown()

  async function run(work: () => Promise<void>) {
    setError('')
    setInfo('')
    setBusy(true)
    try {
      await work()
    } catch (err) {
      setError((err as Error).message)
    } finally {
      setBusy(false)
    }
  }

  const sendCode = () => run(async () => {
    await api('/auth/forgot-password', { method: 'POST', body: JSON.stringify({ email }) })
    setStep('code')
    setInfo('If that email has an account, we sent it a 6-digit code.')
    setWait(RESEND_SECONDS)
  })

  const checkCode = () => run(async () => {
    const r = await api<{ ok: boolean }>('/auth/reset-code/verify', { method: 'POST', body: JSON.stringify({ email, code }) })
    if (r.ok) setStep('password')
    else setError('That code is wrong or has expired.')
  })

  const changePassword = () => run(async () => {
    await api('/auth/reset-password', { method: 'POST', body: JSON.stringify({ email, code, newPassword: password }) })
    navigate('/login', { state: { note: 'Password changed, log in.' } })
  })

  function startOver() {
    setStep('email')
    setCode('')
    setPassword('')
    setInfo('')
    setError('')
  }

  function submit(e: FormEvent) {
    e.preventDefault()
    if (step === 'email') sendCode()
    else if (step === 'code') checkCode()
    else changePassword()
  }

  return (
    <form className="auth-form" onSubmit={submit}>
      <h1>Forgot password</h1>
      {step === 'email' && (
        <label>Email
          <input type="email" required autoFocus value={email} onChange={(e) => setEmail(e.target.value)} />
        </label>
      )}
      {step === 'code' && (
        <>
          <p className="muted">Enter the code sent to {email}.</p>
          <label>Code
            <input required autoFocus inputMode="numeric" autoComplete="one-time-code" pattern="[0-9 ]{6,7}"
              title="6 digits" value={code} onChange={(e) => setCode(e.target.value)} />
          </label>
        </>
      )}
      {step === 'password' && (
        <>
          <PasswordInput label="New password" value={password} onChange={setPassword} minLength={12} autoComplete="new-password" />
          <p className="muted hint">At least 12 characters. Passwords found in data breaches are refused. You will be logged out everywhere.</p>
        </>
      )}
      {info && <p className="muted" role="status">{info}</p>}
      {error && <p className="error" role="alert">{error}</p>}
      <button type="submit" className="btn-primary" disabled={busy}>
        {step === 'email' ? 'Send code' : step === 'code' ? 'Check code' : 'Change password'}
      </button>
      {step === 'code' && (
        <button type="button" onClick={sendCode} disabled={busy || wait > 0}>
          {wait > 0 ? `Send the code again (${wait}s)` : 'Send the code again'}
        </button>
      )}
      {step !== 'email' && <button type="button" onClick={startOver} disabled={busy}>Start over</button>}
      <p className="muted"><Link to="/login" className="link">Back to login</Link></p>
    </form>
  )
}

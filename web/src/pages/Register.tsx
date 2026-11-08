import { useState, type FormEvent } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { api } from '../api'
import GoogleLink from '../components/GoogleLink'
import PasswordInput from '../components/PasswordInput'
import { useTitle } from '../useTitle'

export default function Register() {
  useTitle('Register')
  const navigate = useNavigate()
  const [email, setEmail] = useState('')
  const [userName, setUserName] = useState('')
  const [password, setPassword] = useState('')
  const [age, setAge] = useState(false)
  const [terms, setTerms] = useState(false)
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)

  async function submit(e: FormEvent) {
    e.preventDefault()
    setError('')
    setBusy(true)
    try {
      await api('/auth/register', {
        method: 'POST',
        body: JSON.stringify({ email, password, userName: userName || null, ageConfirmed: age, termsAccepted: terms }),
      })
      navigate('/login', { state: { note: 'Account created. Log in, then verify your email to write reviews.' } })
    } catch (err) {
      setError((err as Error).message)
    } finally {
      setBusy(false)
    }
  }

  return (
    <form className="auth-form" onSubmit={submit}>
      <h1>Register</h1>
      <label>Email
        <input type="email" required autoComplete="off" value={email} onChange={(e) => setEmail(e.target.value)} />
      </label>
      <label>Display name (optional)
        <input autoComplete="off" value={userName} onChange={(e) => setUserName(e.target.value)} />
      </label>
      <PasswordInput label="Password" value={password} onChange={setPassword} minLength={12} autoComplete="new-password" />
      <p className="muted hint">At least 12 characters. A passphrase works well. Passwords found in data breaches are refused.</p>
      <label className="check">
        <input type="checkbox" required checked={age} onChange={(e) => setAge(e.target.checked)} />
        I am 13 years old or older
      </label>
      <label className="check">
        <input type="checkbox" required checked={terms} onChange={(e) => setTerms(e.target.checked)} />
        <span>I accept the <Link to="/terms" className="link">Terms of Service</Link> and the <Link to="/privacy" className="link">Privacy policy</Link></span>
      </label>
      {error && <p className="error">{error}</p>}
      <button type="submit" className="btn-primary" disabled={busy}>Create account</button>
      <GoogleLink ticked={age && terms} />
      <p className="muted">Have an account? <Link to="/login" className="link">Log in</Link></p>
    </form>
  )
}

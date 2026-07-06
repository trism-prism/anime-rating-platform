import { useEffect, useRef, useState } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { api } from '../api'
import { useAuth } from '../auth'

export default function ConfirmEmail() {
  const [params] = useSearchParams()
  const { user, refresh } = useAuth()
  const userId = params.get('userId')
  const token = params.get('token')
  const valid = !!userId && !!token
  const [status, setStatus] = useState<'loading' | 'ok' | 'error'>(valid ? 'loading' : 'error')
  const [message, setMessage] = useState(valid ? '' : 'Invalid confirmation link.')
  // the token is single-use and StrictMode runs effects twice in dev
  const called = useRef(false)

  useEffect(() => {
    if (!valid || called.current) return
    called.current = true
    api<{ message: string }>(`/auth/confirm-email?userId=${encodeURIComponent(userId)}&token=${encodeURIComponent(token)}`)
      .then((r) => { setStatus('ok'); setMessage(r.message); if (localStorage.getItem('token')) refresh() })
      .catch((err) => { setStatus('error'); setMessage(err.message) })
  }, [valid, userId, token, refresh])

  return (
    <div className="auth-form">
      <h1>Confirm email</h1>
      {status === 'loading' && <p className="muted">Confirming...</p>}
      {status === 'ok' && <p>{message}</p>}
      {status === 'error' && <p className="error">{message}</p>}
      {status !== 'loading' && (user
        ? <Link to="/account" className="link">Go to account</Link>
        : <Link to="/login" className="link">Go to login</Link>)}
    </div>
  )
}

import { useEffect, useRef, useState } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { api } from '../api'
import { useAuth } from '../auth'

// the link in the mail sent to the new address; every device is signed out, so it ends at the login page
export default function ConfirmEmailChange() {
  const [params] = useSearchParams()
  const { user, refresh } = useAuth()
  const userId = params.get('userId')
  const newEmail = params.get('newEmail')
  const token = params.get('token')
  const valid = !!userId && !!newEmail && !!token
  const [status, setStatus] = useState<'loading' | 'ok' | 'error'>(valid ? 'loading' : 'error')
  const [message, setMessage] = useState(valid ? '' : 'Invalid link')
  // the token is single-use and StrictMode runs effects twice in dev
  const called = useRef(false)

  useEffect(() => {
    if (!valid || called.current) return
    called.current = true
    api<{ message: string }>('/auth/confirm-email-change', {
      method: 'POST',
      body: JSON.stringify({ userId, newEmail, token }),
    })
      .then((r) => { setStatus('ok'); setMessage(r.message); if (user) refresh() }) // drops this browser's ended session
      .catch((err) => { setStatus('error'); setMessage(err.message) })
  }, [valid, userId, newEmail, token, refresh, user])

  return (
    <div className="auth-form">
      <h1>Confirm new email</h1>
      {status === 'loading' && <p className="muted">Confirming</p>}
      {status === 'ok' && <p role="status">{message}</p>}
      {status === 'error' && <p className="error" role="alert">{message}</p>}
      {status !== 'loading' && <Link to="/login" className="link">Go to login</Link>}
    </div>
  )
}

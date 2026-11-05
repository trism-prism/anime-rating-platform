import { useEffect, useRef, useState } from 'react'
import { Link, useNavigate, useSearchParams } from 'react-router-dom'
import { refreshSession } from '../api'
import { useAuth } from '../auth'

// Google sent the browser back via the API, which set the refresh cookie; trade it for an access token
export default function GoogleCallback() {
  const [params] = useSearchParams()
  const { login } = useAuth()
  const navigate = useNavigate()
  const userId = params.get('userId')
  const [error, setError] = useState(userId ? '' : 'Google sign-in failed. Try again or use your password.')
  // a refresh token is single-use and StrictMode runs effects twice in dev
  const called = useRef(false)

  useEffect(() => {
    if (!userId || called.current) return
    called.current = true
    refreshSession(userId)
      .then(async (s) => {
        if (!s) throw new Error('Google sign-in failed. Try again or use your password.')
        await login(s) // throws at the 5-account limit
        navigate('/', { replace: true })
      })
      .catch((err: Error) => setError(err.message))
  }, [userId, login, navigate])

  return (
    <div className="auth-form">
      <h1>Google sign-in</h1>
      {error ? <p className="error" role="alert">{error}</p> : <p className="muted">Signing you in...</p>}
      {error && <Link to="/login" className="link">Back to login</Link>}
    </div>
  )
}

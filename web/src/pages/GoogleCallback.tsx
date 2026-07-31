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
  const [error, setError] = useState(userId ? '' : 'This Google sign-in link is not valid. Start again from the login page.')
  // a refresh token is single-use and StrictMode runs effects twice in dev
  const called = useRef(false)

  useEffect(() => {
    if (!userId || called.current) return
    called.current = true
    async function finish(id: string) {
      let s
      try {
        s = await refreshSession(id)
      } catch {
        return setError('Could not reach the server. Try again.')
      }
      // the cookie the API just set did not come back: blocked cookies, or a wrong cookie path or domain
      if (!s) return setError('Google accepted you, but the session could not be started. Make sure cookies are allowed, then try again.')
      try {
        await login(s)
      } catch (err) {
        return setError((err as Error).message) // the account limit
      }
      navigate('/', { replace: true })
    }
    finish(userId)
  }, [userId, login, navigate])

  return (
    <div className="auth-form">
      <h1>Google sign-in</h1>
      {error ? <p className="error" role="alert">{error}</p> : <p className="muted">Signing you in</p>}
      {error && <Link to="/login" className="link">Back to login</Link>}
    </div>
  )
}

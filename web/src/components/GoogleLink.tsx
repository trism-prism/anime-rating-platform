import { useEffect, useState } from 'react'
import { api } from '../api'

// "or Continue with Google"; shown only when the server has Google sign-in set up
export default function GoogleLink() {
  const [on, setOn] = useState(false)
  useEffect(() => {
    api<{ google: boolean }>('/auth/providers').then((r) => setOn(r.google)).catch(() => {})
  }, [])
  if (!on) return null
  return (
    <>
      <p className="muted">or</p>
      {/* a full page load: the server redirects on to Google */}
      <a className="link" href="/api/auth/google/start">Continue with Google</a>
    </>
  )
}

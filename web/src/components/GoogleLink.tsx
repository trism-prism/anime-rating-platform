import { useEffect, useState } from 'react'
import { api } from '../api'

// "or Continue with Google"; shown only when the server has Google sign-in set up.
// ticked: only on Register, where a new account needs the age and Terms boxes; the server refuses new Google accounts without it
export default function GoogleLink({ ticked }: { ticked?: boolean }) {
  const [on, setOn] = useState(false)
  useEffect(() => {
    api<{ google: boolean }>('/auth/providers').then((r) => setOn(r.google)).catch(() => {})
  }, [])
  if (!on) return null
  return (
    <>
      <p className="muted">or</p>
      {ticked === false ? (
        <p className="muted hint">Tick both boxes above to continue with Google.</p>
      ) : (
        /* a full page load: the server redirects on to Google */
        <a className="link" href={ticked ? '/api/auth/google/start?terms=true' : '/api/auth/google/start'}>Continue with Google</a>
      )}
    </>
  )
}

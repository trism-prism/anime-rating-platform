import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { api } from '../api'

// "or Continue with Google"; shown only when the server has Google sign-in set up.
// ticked: on Register, false until the age and Terms boxes are ticked. On Login the notice below stands in for the boxes.
// Either way the link sends terms=true: the server refuses to create a new Google account without it.
export default function GoogleLink({ ticked, notice }: { ticked?: boolean; notice?: boolean }) {
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
        <>
          <a className="link" href="/api/auth/google/start?terms=true">Continue with Google</a>
          {notice && (
            <p className="muted hint">
              By continuing with Google you confirm you are 13 or older and accept the <Link to="/terms" className="link">Terms</Link> and{' '}
              <Link to="/privacy" className="link">Privacy Policy</Link>.
            </p>
          )}
        </>
      )}
    </>
  )
}

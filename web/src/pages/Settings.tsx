import { useEffect } from 'react'
import { Link, Navigate } from 'react-router-dom'
import { useAuth } from '../auth'
import AniListImport from '../components/AniListImport'
import EmailForm from '../components/EmailForm'
import ExportForm from '../components/ExportForm'
import PasswordForm from '../components/PasswordForm'
import UserNameForm from '../components/UserNameForm'

export default function Settings() {
  const { user, emailVerified, hasPassword, loading, refresh } = useAuth()

  // pick up a verification done in another tab
  useEffect(() => { refresh() }, [refresh])

  if (!user) return <Navigate to="/login" replace />

  return (
    <div className="settings">
      <h1>Settings</h1>

      <h2>Display name</h2>
      <UserNameForm />

      <AniListImport />

      {loading ? null : !hasPassword ? (
        <>
          <h2>Sign-in</h2>
          <p className="muted">This account signs in with Google. To add a password, use Forgot password on the login page.</p>
        </>
      ) : (
        <>
          <h2>Password</h2>
          <PasswordForm onChanged={refresh} />

          <h2>Email</h2>
          <p>
            <span className="muted">Current: </span>{user.email}{' '}
            <span className={emailVerified ? 'badge ok' : 'badge'}>{emailVerified ? 'Verified' : 'Not verified'}</span>
          </p>
          {!emailVerified && (
            <p className="muted hint">
              Verified accounts can write reviews.{' '}
              <Link to="/verify" state={{ from: '/settings' }} className="link">Verify your email</Link>
            </p>
          )}
          <EmailForm onChanged={refresh} />

          <h2>Export your data</h2>
          <ExportForm />
        </>
      )}
    </div>
  )
}

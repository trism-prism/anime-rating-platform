import { useEffect } from 'react'
import { Link, Navigate } from 'react-router-dom'
import { useAuth } from '../auth'
import AniListImport from '../components/AniListImport'
import EmailForm from '../components/EmailForm'
import DeleteAccountForm from '../components/DeleteAccountForm'
import ExportForm from '../components/ExportForm'
import PasswordForm from '../components/PasswordForm'
import { useTitle } from '../useTitle'

export default function Settings() {
  useTitle('Settings')
  const { user, emailVerified, hasPassword, loading, refresh } = useAuth()

  // pick up a verification done in another tab
  useEffect(() => { refresh() }, [refresh])

  if (!user) return <Navigate to="/login" replace />

  return (
    <div className="settings">
      <h1>Settings</h1>
      <div className="settings-layout">
        {/* beside the cards on wide screens, a row of anchors above them on small ones */}
        <nav className="settings-nav" aria-label="Settings sections">
          <a href="#account">Account</a>
          <a href="#import-export">Import and export</a>
          <a href="#danger">Danger zone</a>
        </nav>

        <div className="settings-cards">
          {loading ? null : !hasPassword ? (
            <section className="settings-card" id="account" aria-labelledby="h-signin">
              <h2 id="h-signin">Sign-in</h2>
              <p className="card-desc">This account signs in with Google. To add a password, use Forgot password on the login page.</p>
            </section>
          ) : (
            <>
              <section className="settings-card" id="account" aria-labelledby="h-email">
                <h2 id="h-email">Email</h2>
                <p className="card-desc">
                  {user.email}{' '}
                  <span className={emailVerified ? 'badge ok' : 'badge'}>{emailVerified ? 'Verified' : 'Not verified'}</span>
                </p>
                {!emailVerified && (
                  <p className="card-desc">
                    Verified accounts can write reviews.{' '}
                    <Link to="/verify" state={{ from: '/settings' }} className="link">Verify your email</Link>
                  </p>
                )}
                <EmailForm onChanged={refresh} />
              </section>

              <section className="settings-card" aria-labelledby="h-password">
                <h2 id="h-password">Password</h2>
                <p className="card-desc">Change the password you sign in with.</p>
                <PasswordForm onChanged={refresh} />
              </section>
            </>
          )}

          <section className="settings-card" id="import-export" aria-labelledby="h-import">
            <AniListImport />
          </section>

          {!loading && hasPassword && (
            <section className="settings-card" aria-labelledby="h-export">
              <h2 id="h-export">Export your data</h2>
              <p className="card-desc">Your ratings, reviews, list entries and custom lists, with dates.</p>
              <ExportForm />
            </section>
          )}

          {!loading && (
            <section className="settings-card danger-card" id="danger" aria-labelledby="h-delete">
              <h2 id="h-delete">Delete account</h2>
              <DeleteAccountForm />
            </section>
          )}
        </div>
      </div>
    </div>
  )
}

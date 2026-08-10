import { useState } from 'react'
import { NavLink, Outlet, useLocation, useNavigate } from 'react-router-dom'
import { useAuth } from '../auth'
import { usePending, usePendingSync } from '../pendingRequests'
import AvatarMenu from './AvatarMenu'
import NotificationBell from './NotificationBell'
import TabBar from './TabBar'

// one content column per kind of page, so the left edge holds between sibling pages (widths: --w-* in index.css)
const NARROW = ['/login', '/register', '/forgot-password', '/verify', '/support', '/confirm-email-change', '/auth/callback']
const READING = ['/u', '/reviews', '/lists', '/feed', '/stats', '/privacy', '/terms', '/content-policy', '/faq']
const under = (path: string, roots: string[]) => roots.some((r) => path === r || path.startsWith(r + '/'))
// settings and its sub-pages share the reading width (the page is a nav plus a column of cards)
const columnOf = (path: string) => (under(path, ['/settings']) ? 'reading' : under(path, NARROW) ? 'narrow' : under(path, READING) ? 'reading' : 'wide')

export default function Layout() {
  const { user, emailVerified, loading } = useAuth()
  const { pathname } = useLocation()
  const navigate = useNavigate()
  const [hidden, setHidden] = useState(false) // dismissed until the next page load
  const showBanner = !!user && !loading && !emailVerified && !hidden && pathname !== '/verify'
  const pending = usePending()
  usePendingSync(!!user, pathname)
  const column = `page page-${columnOf(pathname)}`

  return (
    <>
      <a href="#main" className="skip-link" onClick={(e) => { e.preventDefault(); document.getElementById('main')?.focus() }}>Skip to content</a>
      <nav className="nav" aria-label="Site">
        <NavLink to="/" end className="brand">AnimeRate</NavLink>
        <span className="spacer" />
        {/* phones get these links in the bottom tab bar */}
        <NavLink to="/" end className="nav-text">Home</NavLink>
        <NavLink to="/trending" className="nav-text">Trending</NavLink>
        <NavLink to="/rankings" className="nav-text">Rankings</NavLink>
        <NavLink to="/my-list" className="nav-text">My List</NavLink>
        {user ? (
          <>
            <NavLink to="/feed" className="nav-text">
              Friends
              {pending > 0 && <><span className="count-badge inline" aria-hidden="true">{pending > 9 ? '9+' : pending}</span><span className="sr-only">, {pending} {pending === 1 ? 'request' : 'requests'} waiting</span></>}
            </NavLink>
            <NotificationBell />
            <AvatarMenu />
          </>
        ) : !loading && (
          <>
            <NavLink to="/login" className="nav-text">Login</NavLink>
            <NavLink to="/register" className="nav-text">Register</NavLink>
          </>
        )}
      </nav>
      {showBanner && (
        <div className="banner" role="status">
          <span>Verify your email to post reviews.</span>
          <button type="button" className="link-button" onClick={() => navigate('/verify', { state: { from: pathname } })}>Verify</button>
          <button type="button" className="icon-button" aria-label="Dismiss email verification reminder" onClick={() => setHidden(true)}>&times;</button>
        </div>
      )}
      <main id="main" className={column} tabIndex={-1}>
        <Outlet />
      </main>
      <TabBar /> {/* fixed to the bottom on phones; placed before the footer so keyboard order reaches it first */}
      <footer className={`${column} muted`}>
        <NavLink to="/privacy">Privacy</NavLink>
        <NavLink to="/terms">Terms</NavLink>
        <NavLink to="/content-policy">Content policy</NavLink>
        <NavLink to="/faq">FAQ</NavLink>
        <NavLink to="/support">Support</NavLink>
      </footer>
    </>
  )
}

import { useState } from 'react'
import { NavLink, Outlet, useLocation, useNavigate } from 'react-router-dom'
import { useAuth } from '../auth'
import AvatarMenu from './AvatarMenu'
import NotificationBell from './NotificationBell'

export default function Layout() {
  const { user, emailVerified, loading } = useAuth()
  const { pathname } = useLocation()
  const navigate = useNavigate()
  const [hidden, setHidden] = useState(false) // dismissed until the next page load
  const showBanner = !!user && !loading && !emailVerified && !hidden && pathname !== '/verify'

  return (
    <>
      <nav className="nav">
        <NavLink to="/" end className="brand">AnimeRate</NavLink>
        <span className="spacer" />
        <NavLink to="/" end>Home</NavLink>
        <NavLink to="/trending">Trending</NavLink>
        <NavLink to="/rankings">Rankings</NavLink>
        <NavLink to="/my-list">My List</NavLink>
        {user ? (
          <>
            <NotificationBell />
            <AvatarMenu />
          </>
        ) : (
          <>
            <NavLink to="/login">Login</NavLink>
            <NavLink to="/register">Register</NavLink>
          </>
        )}
      </nav>
      {showBanner && (
        <div className="banner" role="status">
          <span>Verify your email to start writing reviews</span>
          <button type="button" onClick={() => navigate('/verify', { state: { from: pathname } })}>Verify</button>
          <button type="button" className="icon-button" aria-label="Dismiss" onClick={() => setHidden(true)}>&times;</button>
        </div>
      )}
      <main className="page">
        <Outlet />
      </main>
      <footer className="page muted">
        <NavLink to="/privacy">Privacy</NavLink>
        <NavLink to="/terms">Terms</NavLink>
        <NavLink to="/content-policy">Content policy</NavLink>
        <NavLink to="/faq">FAQ</NavLink>
        <NavLink to="/support">Support</NavLink>
      </footer>
    </>
  )
}

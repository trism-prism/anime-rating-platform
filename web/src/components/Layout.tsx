import { NavLink, Outlet } from 'react-router-dom'
import { useAuth } from '../auth'
import AvatarMenu from './AvatarMenu'

export default function Layout() {
  const { user } = useAuth()

  return (
    <>
      <nav className="nav">
        <NavLink to="/" end className="brand">AnimeRate</NavLink>
        <NavLink to="/trending">Trending</NavLink>
        <NavLink to="/rankings">Rankings</NavLink>
        <NavLink to="/my-list">My List</NavLink>
        <span className="spacer" />
        {user ? (
          <AvatarMenu />
        ) : (
          <>
            <NavLink to="/login">Login</NavLink>
            <NavLink to="/register">Register</NavLink>
          </>
        )}
      </nav>
      <main className="page">
        <Outlet />
      </main>
      <footer className="page muted">
        <NavLink to="/privacy">Privacy</NavLink>
      </footer>
    </>
  )
}

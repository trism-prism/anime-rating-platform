import { NavLink, Outlet, useNavigate } from 'react-router-dom'
import { useAuth } from '../auth'

export default function Layout() {
  const navigate = useNavigate()
  const { user, logout } = useAuth()

  return (
    <>
      <nav className="nav">
        <NavLink to="/" end className="brand">AnimeRate</NavLink>
        <NavLink to="/trending">Trending</NavLink>
        <NavLink to="/rankings">Rankings</NavLink>
        <NavLink to="/my-list">My List</NavLink>
        <span className="spacer" />
        {user ? (
          <>
            <span className="muted">{user.userName}</span>
            <button onClick={() => { logout(); navigate('/') }}>Logout</button>
          </>
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
    </>
  )
}

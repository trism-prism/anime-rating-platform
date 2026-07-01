import { useState } from 'react'
import { NavLink, Outlet, useNavigate } from 'react-router-dom'

export default function Layout() {
  const navigate = useNavigate()
  const [token, setToken] = useState(localStorage.getItem('token'))

  function logout() {
    localStorage.removeItem('token')
    setToken(null)
    navigate('/')
  }

  return (
    <>
      <nav className="nav">
        <NavLink to="/" end className="brand">AnimeRate</NavLink>
        <NavLink to="/trending">Trending</NavLink>
        <NavLink to="/rankings">Rankings</NavLink>
        <NavLink to="/my-list">My List</NavLink>
        <span className="spacer" />
        {token ? <button onClick={logout}>Logout</button> : <NavLink to="/login">Login</NavLink>}
      </nav>
      <main className="page">
        <Outlet />
      </main>
    </>
  )
}

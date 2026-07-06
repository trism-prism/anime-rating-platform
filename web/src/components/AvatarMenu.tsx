import { useEffect, useRef, useState, type KeyboardEvent } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { useAuth } from '../auth'

// round avatar button with a popover menu for the signed-in account
export default function AvatarMenu() {
  const { user, accounts, isAdmin, logout, switchTo } = useAuth()
  const navigate = useNavigate()
  const [open, setOpen] = useState(false)
  const box = useRef<HTMLDivElement>(null)
  const button = useRef<HTMLButtonElement>(null)

  const items = () => Array.from(box.current?.querySelectorAll<HTMLElement>('[role="menuitem"]') ?? [])

  useEffect(() => {
    if (!open) return
    items()[0]?.focus()
    const onDown = (e: MouseEvent) => {
      if (!box.current?.contains(e.target as Node)) setOpen(false)
    }
    document.addEventListener('mousedown', onDown)
    return () => document.removeEventListener('mousedown', onDown)
  }, [open])

  if (!user) return null

  function onKeyDown(e: KeyboardEvent) {
    if (e.key === 'Escape') {
      setOpen(false)
      button.current?.focus()
    } else if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
      e.preventDefault()
      const list = items()
      const i = list.indexOf(document.activeElement as HTMLElement)
      const next = e.key === 'ArrowDown' ? i + 1 : i - 1
      list[(next + list.length) % list.length]?.focus()
    }
  }

  async function doSwitch(userId: string) {
    setOpen(false)
    try {
      if (!(await switchTo(userId))) navigate('/login')
    } catch {
      // server unreachable: stay signed in as before
    }
  }

  async function doLogout() {
    setOpen(false)
    await logout()
    navigate('/')
  }

  return (
    <div
      className="avatar-menu"
      ref={box}
      onKeyDown={onKeyDown}
      // Tab moved on; relatedTarget is null when Safari doesn't focus a clicked button, so those clicks must not close it
      onBlur={(e) => { if (e.relatedTarget && !e.currentTarget.contains(e.relatedTarget)) setOpen(false) }}
    >
      <button
        ref={button}
        className="avatar"
        aria-haspopup="menu"
        aria-expanded={open}
        aria-label={`Account menu for ${user.userName}`}
        onClick={() => setOpen(!open)}
      >
        {user.userName.charAt(0).toUpperCase()}
      </button>
      {open && (
        <div className="menu" role="menu">
          <div className="menu-label">{user.userName}</div>
          <Link to="/settings" role="menuitem" onClick={() => setOpen(false)}>Settings</Link>
          {isAdmin && <Link to="/admin" role="menuitem" onClick={() => setOpen(false)}>Admin</Link>}
          <div className="menu-label">Switch account</div>
          {accounts.filter((a) => a.userId !== user.userId).map((a) => (
            <button key={a.userId} role="menuitem" onClick={() => doSwitch(a.userId)}>{a.userName}</button>
          ))}
          <Link to="/login" role="menuitem" onClick={() => setOpen(false)}>Add account</Link>
          <button role="menuitem" onClick={doLogout}>Log out</button>
        </div>
      )}
    </div>
  )
}

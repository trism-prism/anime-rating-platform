import { useEffect, useRef, useState, type KeyboardEvent } from 'react'
import { Link, useLocation, useNavigate } from 'react-router-dom'
import { useAuth } from '../auth'
import Avatar from './Avatar'

const icon = { width: 16, height: 16, viewBox: '0 0 16 16', fill: 'none', stroke: 'currentColor', strokeWidth: 1.5, strokeLinecap: 'round', strokeLinejoin: 'round', 'aria-hidden': true } as const

// two arrows stacked: one pointing right above one pointing left
const SwitchIcon = () => (
  <svg {...icon}>
    <path d="M2 5h11M10 2l3 3-3 3" />
    <path d="M14 11H3M6 8l-3 3 3 3" />
  </svg>
)

const PlusIcon = () => (
  <svg {...icon}>
    <path d="M8 3v10M3 8h10" />
  </svg>
)

// round avatar button with a popover menu for the signed-in account
export default function AvatarMenu() {
  const { user, accounts, isAdmin, emailVerified, loading, logout, switchTo } = useAuth()
  const navigate = useNavigate()
  const { pathname } = useLocation()
  const [open, setOpen] = useState(false)
  const [switching, setSwitching] = useState(false) // the account list under "Switch account"
  const box = useRef<HTMLDivElement>(null)
  const button = useRef<HTMLButtonElement>(null)
  const switchButton = useRef<HTMLButtonElement>(null)

  const items = (selector: string) => Array.from(box.current?.querySelectorAll<HTMLElement>(selector) ?? [])
  const mainItems = () => items('.menu > [role="menuitem"]')
  const subItems = () => items('.submenu > [role="menuitem"]')

  useEffect(() => {
    if (!open) return
    mainItems()[0]?.focus()
    const onDown = (e: MouseEvent) => {
      if (!box.current?.contains(e.target as Node)) setOpen(false)
    }
    document.addEventListener('mousedown', onDown)
    return () => document.removeEventListener('mousedown', onDown)
  }, [open])

  useEffect(() => {
    if (switching) subItems()[0]?.focus()
  }, [switching])

  if (!user) return null

  function onKeyDown(e: KeyboardEvent) {
    const inSub = !!(e.target as HTMLElement).closest('.submenu')
    if (e.key === 'Escape') {
      // the list closes first, then the menu
      if (switching) {
        setSwitching(false)
        switchButton.current?.focus()
      } else {
        setOpen(false)
        button.current?.focus()
      }
    } else if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
      e.preventDefault()
      const list = inSub ? subItems() : mainItems()
      const i = list.indexOf(document.activeElement as HTMLElement)
      const next = e.key === 'ArrowDown' ? i + 1 : i - 1
      list[(next + list.length) % list.length]?.focus()
    } else if (e.key === 'ArrowRight' && e.target === switchButton.current) {
      e.preventDefault()
      setSwitching(true)
    } else if (e.key === 'ArrowLeft' && inSub) {
      e.preventDefault()
      setSwitching(false)
      switchButton.current?.focus()
    }
  }

  // a full page load, so every page, form and cache starts fresh for the new account
  async function doSwitch(userId: string) {
    setOpen(false)
    try {
      if (await switchTo(userId)) window.location.assign('/')
      else navigate('/login')
    } catch {
      // server unreachable: stay signed in as before
    }
  }

  async function doLogout() {
    setOpen(false)
    await logout()
    window.location.assign('/')
  }

  const others = accounts.filter((a) => a.userId !== user.userId)
  const unverified = !loading && !emailVerified

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
        className="avatar-button"
        aria-haspopup="menu"
        aria-expanded={open}
        aria-label={`Account menu for ${user.userName}${unverified ? ', email not verified' : ''}`}
        onClick={() => { setSwitching(false); setOpen(!open) }} // the account list always starts closed
      >
        <Avatar userName={user.userName} />
        {unverified && <span className="avatar-dot" aria-hidden="true" />}
      </button>
      {open && (
        <div className="menu" role="menu" aria-label="Account">
          <div className="menu-label" role="presentation">{user.userName}</div>
          {unverified && <Link to="/verify" state={{ from: pathname }} role="menuitem" onClick={() => setOpen(false)}>Verify email</Link>}
          <Link to={`/u/${encodeURIComponent(user.userName)}`} role="menuitem" onClick={() => setOpen(false)}>Profile</Link>
          <Link to="/settings" role="menuitem" onClick={() => setOpen(false)}>Settings</Link>
          <Link to="/settings/notifications" role="menuitem" onClick={() => setOpen(false)}>Notification preferences</Link>
          {isAdmin && <Link to="/admin" role="menuitem" onClick={() => setOpen(false)}>Admin</Link>}
          <button
            ref={switchButton}
            className="menu-switch"
            role="menuitem"
            aria-haspopup="menu"
            aria-expanded={switching}
            aria-controls="switch-menu"
            onClick={() => setSwitching(!switching)}
          >
            <SwitchIcon /> Switch account
          </button>
          {switching && (
            <div className="submenu" id="switch-menu" role="menu" aria-label="Switch account">
              {others.map((a) => (
                <button key={a.userId} role="menuitem" onClick={() => doSwitch(a.userId)}>
                  <Avatar userName={a.userName} small /> {a.userName}
                </button>
              ))}
              <Link to="/login" role="menuitem" onClick={() => setOpen(false)}>
                <PlusIcon /> Add account
              </Link>
            </div>
          )}
          <button role="menuitem" onClick={doLogout}>Log out</button>
        </div>
      )}
    </div>
  )
}

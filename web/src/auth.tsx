import { createContext, useCallback, useContext, useEffect, useState, type ReactNode } from 'react'
import { api, getActive, refreshSession, setLostHandler, setSession, type SignedIn } from './api'

// no secrets here: the access token stays in memory and refresh tokens are HttpOnly cookies
export type Account = { userId: string; userName: string; email: string }
type Auth = {
  user: Account | null // the active account
  accounts: Account[] // everyone signed in on this browser
  emailVerified: boolean
  isAdmin: boolean
  loading: boolean // true until the first /me has answered for a remembered account
  login: (s: SignedIn) => Promise<void>
  logout: () => Promise<void>
  switchTo: (userId: string) => Promise<boolean>
  refresh: () => Promise<void>
}

const MAX_ACCOUNTS = 5
const AuthContext = createContext<Auth>(null!)

function loadAccounts(): Account[] {
  // the old version kept a 7-day token in localStorage; remove it
  localStorage.removeItem('token')
  localStorage.removeItem('user')
  try {
    return JSON.parse(localStorage.getItem('accounts') ?? '[]')
  } catch {
    return []
  }
}

export function AuthProvider({ children }: { children: ReactNode }) {
  const [accounts, setAccounts] = useState<Account[]>(loadAccounts)
  const [activeId, setActiveId] = useState<string | null>(() => {
    const id = localStorage.getItem('activeUser')
    setSession(id, null) // api.ts then fetches the first access token by itself
    return id
  })
  const [emailVerified, setEmailVerified] = useState(false)
  const [isAdmin, setIsAdmin] = useState(false)
  const [loading, setLoading] = useState(() => !!activeId)

  useEffect(() => {
    localStorage.setItem('accounts', JSON.stringify(accounts))
    if (activeId) localStorage.setItem('activeUser', activeId)
    else localStorage.removeItem('activeUser')
  }, [accounts, activeId])

  // forget an account; if it was the active one, nobody is signed in until the caller picks another
  const removeAccount = useCallback((id: string) => {
    setAccounts((a) => a.filter((x) => x.userId !== id))
    if (getActive() !== id) return
    setSession(null, null)
    setActiveId(null)
    setEmailVerified(false)
    setIsAdmin(false)
    setLoading(false)
  }, [])

  useEffect(() => setLostHandler(removeAccount), [removeAccount])

  // reload verified/admin flags; a dead session is handled inside api()
  const refresh = useCallback(async () => {
    try {
      const me = await api<{ emailVerified: boolean; isAdmin: boolean }>('/me')
      setEmailVerified(me.emailVerified)
      setIsAdmin(me.isAdmin)
    } catch {
      // keep the old flags
    } finally {
      setLoading(false)
    }
  }, [])

  // page load: remembered account, no access token yet (api() fetches one first)
  useEffect(() => {
    if (getActive()) refresh()
  }, [refresh])

  async function login(s: SignedIn) {
    const known = accounts.some((a) => a.userId === s.userId)
    if (!known && accounts.length >= MAX_ACCOUNTS) {
      // the server already opened a session: close it again
      await api('/auth/logout', { method: 'POST', body: JSON.stringify({ userId: s.userId }) }).catch(() => {})
      throw new Error(`You can be signed in to ${MAX_ACCOUNTS} accounts at most. Log out of one first.`)
    }
    const account = { userId: s.userId, userName: s.userName, email: s.email }
    setAccounts([...accounts.filter((a) => a.userId !== s.userId), account])
    setSession(s.userId, s.accessToken)
    setEmailVerified(s.emailVerified)
    setIsAdmin(false)
    setLoading(true)
    setActiveId(s.userId)
    refresh()
  }

  // false when that account's session is gone (it is removed); throws when the server can't be reached
  const switchTo = useCallback(async (userId: string) => {
    const s = await refreshSession(userId)
    if (!s) {
      removeAccount(userId)
      return false
    }
    setSession(userId, s.accessToken)
    setEmailVerified(s.emailVerified)
    setIsAdmin(false)
    setLoading(true)
    setActiveId(userId)
    refresh()
    return true
  }, [removeAccount, refresh])

  // signs out the active account only, then falls back to another one if there is one
  const logout = useCallback(async () => {
    const id = getActive()
    if (!id) return
    await api('/auth/logout', { method: 'POST', body: JSON.stringify({ userId: id }) }).catch(() => {})
    removeAccount(id)
    const next = accounts.find((a) => a.userId !== id)
    if (next) await switchTo(next.userId).catch(() => {})
  }, [accounts, removeAccount, switchTo])

  const user = accounts.find((a) => a.userId === activeId) ?? null

  return (
    <AuthContext.Provider value={{ user, accounts, emailVerified, isAdmin, loading, login, logout, switchTo, refresh }}>
      {children}
    </AuthContext.Provider>
  )
}

export const useAuth = () => useContext(AuthContext)

import { createContext, useCallback, useContext, useEffect, useState, type ReactNode } from 'react'
import { api, getActive, refreshSession, setLostHandler, setSession, type SignedIn } from './api'

// no secrets here: the access token stays in memory and refresh tokens are HttpOnly cookies
export type Account = { userId: string; userName: string; email: string }
type Auth = {
  user: Account | null // the active account
  accounts: Account[] // everyone signed in on this browser
  emailVerified: boolean
  hasPassword: boolean // false for accounts made through Google
  isAdmin: boolean
  loading: boolean // true until the first /me has answered for a remembered account
  login: (s: SignedIn) => Promise<void>
  logout: () => Promise<void>
  switchTo: (userId: string) => Promise<boolean>
  rename: (userName: string) => void // after the display name was changed on the server
  refresh: () => Promise<void>
}

const MAX_ACCOUNTS = 5
const AuthContext = createContext<Auth>(null!)

// storage can be blocked (private modes, policies): then nobody stays signed in across reloads, but the app still runs
const store = {
  get: (k: string) => { try { return localStorage.getItem(k) } catch { return null } },
  set: (k: string, v: string) => { try { localStorage.setItem(k, v) } catch { /* blocked */ } },
  remove: (k: string) => { try { localStorage.removeItem(k) } catch { /* blocked */ } },
}

function loadAccounts(): Account[] {
  // the old version kept a 7-day token in localStorage; remove it
  store.remove('token')
  store.remove('user')
  try {
    return JSON.parse(store.get('accounts') ?? '[]')
  } catch {
    return []
  }
}

// written at once (not only by the effect below) for callers that reload the page right after
function persist(accounts: Account[], activeId: string | null) {
  store.set('accounts', JSON.stringify(accounts))
  if (activeId) store.set('activeUser', activeId)
  else store.remove('activeUser')
}

export function AuthProvider({ children }: { children: ReactNode }) {
  const [accounts, setAccounts] = useState<Account[]>(loadAccounts)
  const [activeId, setActiveId] = useState<string | null>(() => {
    const id = store.get('activeUser')
    setSession(id, null) // api.ts then fetches the first access token by itself
    return id
  })
  const [emailVerified, setEmailVerified] = useState(false)
  const [hasPassword, setHasPassword] = useState(true)
  const [isAdmin, setIsAdmin] = useState(false)
  const [loading, setLoading] = useState(() => !!activeId)

  useEffect(() => persist(accounts, activeId), [accounts, activeId])

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
      const me = await api<{ emailVerified: boolean; hasPassword: boolean; isAdmin: boolean }>('/me')
      setEmailVerified(me.emailVerified)
      setHasPassword(me.hasPassword)
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
    persist(accounts, userId)
    refresh()
    return true
  }, [accounts, removeAccount, refresh])

  // Signs out the active account only. The caller then reloads the site, which opens the next account if there
  // is one (its access token is fetched on load), so only the stored choice is updated here.
  const logout = useCallback(async () => {
    const id = getActive()
    if (!id) return
    await api('/auth/logout', { method: 'POST', body: JSON.stringify({ userId: id }) }).catch(() => {})
    const rest = accounts.filter((a) => a.userId !== id)
    const next = rest[0]?.userId ?? null
    removeAccount(id)
    setActiveId(next)
    persist(rest, next)
  }, [accounts, removeAccount])

  const rename = useCallback((userName: string) => {
    setAccounts((all) => all.map((a) => (a.userId === getActive() ? { ...a, userName } : a)))
  }, [])

  const user = accounts.find((a) => a.userId === activeId) ?? null

  return (
    <AuthContext.Provider value={{ user, accounts, emailVerified, hasPassword, isAdmin, loading, login, logout, switchTo, rename, refresh }}>
      {children}
    </AuthContext.Provider>
  )
}

export const useAuth = () => useContext(AuthContext)

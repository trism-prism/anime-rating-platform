import { createContext, useCallback, useContext, useEffect, useState, type ReactNode } from 'react'
import { api, ApiError } from './api'

type User = { token: string; email: string; userName: string }
type Auth = {
  user: User | null
  emailVerified: boolean
  isAdmin: boolean
  loading: boolean // true until /me has answered for a stored token
  login: (u: User & { emailVerified?: boolean }) => void
  logout: () => void
  refresh: () => Promise<void>
}

const KEY = 'user'
const AuthContext = createContext<Auth>(null!)

function load(): User | null {
  try {
    return JSON.parse(localStorage.getItem(KEY) ?? 'null')
  } catch {
    return null
  }
}

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<User | null>(load)
  const [emailVerified, setEmailVerified] = useState(false)
  const [isAdmin, setIsAdmin] = useState(false)
  const [loading, setLoading] = useState(() => !!load())

  const logout = useCallback(() => {
    localStorage.removeItem(KEY)
    localStorage.removeItem('token')
    setUser(null)
    setEmailVerified(false)
    setIsAdmin(false)
    setLoading(false)
  }, [])

  // reload verified/admin flags; a 401 means the token is dead
  const refresh = useCallback(async () => {
    try {
      const me = await api<{ emailVerified: boolean; isAdmin: boolean }>('/me')
      setEmailVerified(me.emailVerified)
      setIsAdmin(me.isAdmin)
    } catch (err) {
      if (err instanceof ApiError && err.status === 401) logout()
    } finally {
      setLoading(false)
    }
  }, [logout])

  const token = user?.token
  useEffect(() => {
    if (token) refresh()
  }, [token, refresh])

  // api.ts reads the bare 'token' key for the Authorization header
  function login(u: User & { emailVerified?: boolean }) {
    const { emailVerified: verified, ...rest } = u
    localStorage.setItem(KEY, JSON.stringify(rest))
    localStorage.setItem('token', rest.token)
    setEmailVerified(!!verified)
    setLoading(true)
    setUser(rest)
  }

  return (
    <AuthContext.Provider value={{ user, emailVerified, isAdmin, loading, login, logout, refresh }}>
      {children}
    </AuthContext.Provider>
  )
}

export const useAuth = () => useContext(AuthContext)

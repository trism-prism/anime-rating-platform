import { createContext, useContext, useState, type ReactNode } from 'react'

type User = { token: string; email: string; userName: string }
type Auth = { user: User | null; login: (u: User) => void; logout: () => void }

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

  // api.ts reads the bare 'token' key for the Authorization header
  function login(u: User) {
    localStorage.setItem(KEY, JSON.stringify(u))
    localStorage.setItem('token', u.token)
    setUser(u)
  }

  function logout() {
    localStorage.removeItem(KEY)
    localStorage.removeItem('token')
    setUser(null)
  }

  return <AuthContext.Provider value={{ user, login, logout }}>{children}</AuthContext.Provider>
}

export const useAuth = () => useContext(AuthContext)

import { useSyncExternalStore } from 'react'
import { useAuth } from './auth'

// Which ratings a signed-in person sees across the site: everyone's, their friends', or only their own.
// One value for every page, kept like the theme: in memory, mirrored to localStorage, all storage calls in try/catch.
export type Scope = 'all' | 'friends' | 'me'
export const SCOPE_OPTIONS = [{ value: 'all', label: 'All' }, { value: 'friends', label: 'Friends only' }, { value: 'me', label: 'Only me' }]

const KEY = 'ratings-scope'
export const isScope = (v: unknown): v is Scope => v === 'all' || v === 'friends' || v === 'me'

function read(): Scope {
  try {
    const v = localStorage.getItem(KEY)
    if (isScope(v)) return v
  } catch { /* storage blocked: all */ }
  return 'all'
}

let current: Scope = read()
const listeners = new Set<() => void>()
const subscribe = (cb: () => void) => { listeners.add(cb); return () => { listeners.delete(cb) } }

export function setScope(next: Scope) {
  try {
    if (next === 'all') localStorage.removeItem(KEY)
    else localStorage.setItem(KEY, next)
  } catch { /* storage blocked: the choice lasts until reload */ }
  if (next === current) return
  current = next
  listeners.forEach((l) => l())
}

if (typeof window !== 'undefined') {
  window.addEventListener('storage', (e) => { // another tab changed it
    if (e.key !== KEY && e.key !== null) return
    current = read()
    listeners.forEach((l) => l())
  })
}

// signed out is always All, whatever is stored, so a stale value never filters a visitor's view
export function useScope(): [Scope, (s: Scope) => void] {
  const stored = useSyncExternalStore(subscribe, () => current)
  const { user } = useAuth()
  return [user ? stored : 'all', setScope]
}

// "/series/3" + friends -> "/series/3?scope=friends"; All adds nothing, so default requests stay as they were
export function withScope(path: string, scope: Scope) {
  return scope === 'all' ? path : `${path}${path.includes('?') ? '&' : '?'}scope=${scope}`
}

// the sentence for a list with nothing in it under a scope; `fallback` is the All wording
export function scopeEmpty(scope: Scope, what: string, fallback: string) {
  if (scope === 'friends') return `None of your friends have ${what} this yet.`
  if (scope === 'me') return `You haven't ${what} this yet.`
  return fallback
}

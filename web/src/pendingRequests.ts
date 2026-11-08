import { useEffect, useSyncExternalStore } from 'react'
import { api } from './api'
import type { FriendsData } from './types'

// how many friend requests wait for an answer from you; shared by the nav badges
let count = 0
const subs = new Set<() => void>()

export function setPending(n: number) {
  if (n === count) return
  count = n
  subs.forEach((f) => f())
}

let seq = 0 // an older answer arriving late must not overwrite a newer count
export function refreshPending() {
  const id = ++seq
  api<FriendsData>('/me/friends').then((d) => { if (id === seq) setPending(d.incoming.length) }).catch(() => {}) // retried at the next poll
}

export const usePending = () =>
  useSyncExternalStore((cb) => { subs.add(cb); return () => { subs.delete(cb) } }, () => count)

// keeps the count fresh while someone is signed in: on every page change, once a minute, on returning to the tab
export function usePendingSync(signedIn: boolean, pathname: string) {
  useEffect(() => {
    if (!signedIn) { setPending(0); return }
    if (pathname !== '/feed') refreshPending() // the Friends page loads the list itself
    const tick = () => { if (document.visibilityState === 'visible') refreshPending() }
    const id = setInterval(tick, 60_000)
    document.addEventListener('visibilitychange', tick)
    return () => { clearInterval(id); document.removeEventListener('visibilitychange', tick) }
  }, [signedIn, pathname])
}

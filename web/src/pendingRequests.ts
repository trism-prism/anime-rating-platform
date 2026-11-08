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

export function refreshPending() {
  api<FriendsData>('/me/friends').then((d) => setPending(d.incoming.length)).catch(() => {}) // retried at the next poll
}

export const usePending = () =>
  useSyncExternalStore((cb) => { subs.add(cb); return () => { subs.delete(cb) } }, () => count)

// keeps the count fresh while someone is signed in: on every page change, once a minute, on returning to the tab
export function usePendingSync(signedIn: boolean, pathname: string) {
  useEffect(() => {
    if (!signedIn) { setPending(0); return }
    refreshPending()
    const tick = () => { if (document.visibilityState === 'visible') refreshPending() }
    const id = setInterval(tick, 60_000)
    document.addEventListener('visibilitychange', tick)
    return () => { clearInterval(id); document.removeEventListener('visibilitychange', tick) }
  }, [signedIn, pathname])
}

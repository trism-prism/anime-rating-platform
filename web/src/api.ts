// same-site: Vercel (and the Vite dev server) forward /api to the API, so the refresh cookie is first-party
const BASE = '/api'

export class ApiError extends Error {
  status: number
  constructor(message: string, status: number) {
    super(message)
    this.status = status
  }
}

// what /auth/login and /auth/refresh return
export type SignedIn = { accessToken: string; userId: string; email: string; userName: string; emailVerified: boolean }

// the access token lives in memory only; the long-lived refresh token is an HttpOnly cookie
let token: string | null = null
let activeUser: string | null = null
let onLost: (userId: string) => void = () => {}

export function setSession(userId: string | null, accessToken: string | null) {
  activeUser = userId
  token = accessToken
}
export const getActive = () => activeUser
// called when an account's session is gone for good (refresh answered 401)
export function setLostHandler(fn: (userId: string) => void) {
  onLost = fn
}

async function request<T>(path: string, options: RequestInit, bearer: string | null): Promise<T> {
  const headers = new Headers(options.headers)
  if (bearer) headers.set('Authorization', `Bearer ${bearer}`)
  if (options.body) headers.set('Content-Type', 'application/json')

  const res = await fetch(BASE + path, { ...options, headers })
  const data = await res.json().catch(() => null)
  if (!res.ok) {
    // the API sends either {message} or {errors:[...]}
    const msg = (typeof data === 'string' ? data : null) ?? data?.message ?? (Array.isArray(data?.errors) ? data.errors.join(' ') : null)
    throw new ApiError(msg ?? (res.status >= 500 ? 'Server error, try again' : `Request failed (${res.status})`), res.status)
  }
  return data as T
}

const flights = new Map<string, Promise<SignedIn | null>>()

// null = the session is over; throws on a network or server error. One refresh at a time per account:
// calls in this tab share it, and the lock makes other tabs wait, so each sends the cookie the last one received
export function refreshSession(userId: string): Promise<SignedIn | null> {
  let p = flights.get(userId)
  if (!p) {
    const run = async () => {
      try {
        return await request<SignedIn>('/auth/refresh', { method: 'POST', body: JSON.stringify({ userId }) }, null)
      } catch (err) {
        if (err instanceof ApiError && err.status === 401) return null
        throw err
      }
    }
    p = (navigator.locks ? navigator.locks.request(`refresh-${userId}`, run) : run()).finally(() => flights.delete(userId))
    flights.set(userId, p)
  }
  return p
}

// get a new access token for the active account; if its session is over, tell the app
async function renew(userId: string) {
  let s: SignedIn | null
  try {
    s = await refreshSession(userId)
  } catch {
    return // offline or server down: the request below fails on its own
  }
  if (!s) onLost(userId)
  else if (activeUser === userId) token = s.accessToken
}

export async function api<T>(path: string, options: RequestInit = {}): Promise<T> {
  let user = activeUser
  const plain = path.startsWith('/auth/') // login and friends: a 401 there is just an answer
  if (user && !token && !plain) {
    await renew(user) // after a page load there is no access token yet
    user = activeUser // null if that session turned out to be over
  }
  const used = token
  try {
    return await request<T>(path, options, used)
  } catch (err) {
    if (!(err instanceof ApiError && err.status === 401 && user && !plain)) throw err
    // expired access token: refresh once (unless another call just did) and retry once
    if (token === used) await renew(user)
    if (!token) throw err
    return request<T>(path, options, token)
  }
}

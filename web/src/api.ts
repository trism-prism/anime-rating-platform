const BASE = import.meta.env.VITE_API_URL ?? 'http://localhost:5276'

export class ApiError extends Error {
  status: number
  constructor(message: string, status: number) {
    super(message)
    this.status = status
  }
}

export async function api<T>(path: string, options: RequestInit = {}): Promise<T> {
  const headers = new Headers(options.headers)
  const token = localStorage.getItem('token')
  if (token) headers.set('Authorization', `Bearer ${token}`)
  if (options.body) headers.set('Content-Type', 'application/json')

  const res = await fetch(BASE + path, { ...options, headers })
  const data = await res.json().catch(() => null)
  if (!res.ok) {
    // the API sends either {message} or {errors:[...]}
    const msg = (typeof data === 'string' ? data : null) ?? data?.message ?? (Array.isArray(data?.errors) ? data.errors.join(' ') : null)
    throw new ApiError(msg ?? `Request failed (${res.status})`, res.status)
  }
  return data as T
}

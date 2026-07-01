const BASE = import.meta.env.VITE_API_URL ?? 'http://localhost:5276'

export async function api<T>(path: string, options: RequestInit = {}): Promise<T> {
  const headers = new Headers(options.headers)
  const token = localStorage.getItem('token')
  if (token) headers.set('Authorization', `Bearer ${token}`)
  if (options.body) headers.set('Content-Type', 'application/json')

  const res = await fetch(BASE + path, { ...options, headers })
  const data = await res.json().catch(() => null)
  if (!res.ok) throw new Error(data?.message ?? `Request failed (${res.status})`)
  return data as T
}

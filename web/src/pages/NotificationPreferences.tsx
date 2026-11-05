import { useCallback, useEffect, useState } from 'react'
import { Navigate } from 'react-router-dom'
import { api } from '../api'
import { useAuth } from '../auth'
import type { NotificationPref } from '../types'

const LABELS: Record<string, string> = {
  FriendRequest: 'Someone sends me a friend request',
  FriendAccepted: 'Someone accepts my friend request',
}

export default function NotificationPreferences() {
  const { user } = useAuth()
  const [prefs, setPrefs] = useState<NotificationPref[] | null>(null)
  const [error, setError] = useState('')
  const [saveError, setSaveError] = useState('')

  const load = useCallback(() => {
    api<NotificationPref[]>('/me/notification-preferences').then((p) => { setPrefs(p); setError('') }).catch((e: Error) => setError(e.message))
  }, [])

  useEffect(() => {
    if (user) load()
  }, [user, load])

  if (!user) return <Navigate to="/login" replace />

  // shown at once, put back if the server refuses
  async function toggle(p: NotificationPref) {
    setSaveError('')
    const set = (enabled: boolean) => setPrefs((l) => l && l.map((x) => (x.type === p.type ? { ...x, enabled } : x)))
    set(!p.enabled)
    try {
      await api(`/me/notification-preferences/${p.type}`, { method: 'PUT', body: JSON.stringify({ enabled: !p.enabled }) })
    } catch (e) {
      set(p.enabled)
      setSaveError((e as Error).message)
    }
  }

  return (
    <div className="settings">
      <h1>Notification preferences</h1>
      <p className="muted">Notify me when:</p>
      {error && <p className="error" role="alert">{error} <button onClick={() => { setError(''); load() }}>Retry</button></p>}
      {!prefs && !error && <p className="muted">Loading...</p>}
      {prefs && (
        <ul className="prefs">
          {prefs.map((p) => (
            <li key={p.type}>
              <label>
                <input type="checkbox" checked={p.enabled} onChange={() => toggle(p)} /> {LABELS[p.type] ?? p.type}
              </label>
            </li>
          ))}
        </ul>
      )}
      {saveError && <p className="error" role="alert">{saveError}</p>}
    </div>
  )
}
